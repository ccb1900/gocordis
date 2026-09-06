# backfill — power-loss-safe industrial CSV collector (demo)

A runnable demo of the Dynamic Composable Runtime for the scenario:

> An industrial PC collects CSV data produced on another machine and shared over
> Windows SMB (UNC). It runs either as a long-lived service or is invoked by the
> OS task scheduler. A run collects *yesterday's* data, so nothing is watched.
> The source path changes per date and carries business metadata. If the host
> loses power and misses its time point, no day of data may be lost when it
> comes back days later.

## The core idea

**The scheduler is not the source of truth; the journal is.**

Every run (service tick or timer invocation) is a catch-up pass:

```text
expected days = [Since .. yesterday]
collect every expected day that is NOT marked in the completion journal
mark a day ONLY after its output file is fully written and fsynced
```

Consequences:

- host powered off for N days → next run simply collects the N unmarked days;
- power loss between two days → at most the not-yet-marked days are lost, and
  the next run re-collects exactly those;
- repeated runs are safe (output per day is deterministic, overwritten).

## Remote (UNC/SMB) semantics

The source machine is another host, so "the file is not there" is ambiguous:

| kind | meaning | handling |
|---|---|---|
| `missing` | source file does not exist (`os.ErrNotExist`) | NOT marked done (unless `-missing-ok`); recorded in attempts, retried next pass — the file may simply be "not synced yet" |
| `unreachable` | share/network/access error while opening | recorded in attempts, retried next pass |
| `parse` | file readable but malformed | recorded in attempts, retried next pass |
| `write` / `journal` | local output/journal failure | recorded in attempts, retried next pass |

A day enters the **completion journal** only after success, and is then removed
from the **attempts ledger** (`<journal dir>/attempts.log`, atomic + fsynced).
`attempts.log` records count + kind + last error per day and survives process
restarts, so a timer run that fails can print/alert on what is outstanding.

> Recommendation for UNC sources: keep `-missing-ok=false`. Only enable it when
> an absent file provably means "no data that day". On a remote source an absent
> file usually means "not available yet".

## Deployment rules for UNC

1. **Journal and output live on the LOCAL machine** — never on the SMB share.
   If the share is down, the collector must still be able to read/write its own
   state. The source is read-only.
2. **Path template carries the UNC root**, e.g. on Windows:

   ```text
   \\nas\plant-data\{site}\{line}\{YYYY}\{MM}\{DD}\raw.csv
   ```

   Long UNC paths may need the `\\?\UNC\server\share\...` prefix (MAX_PATH).
3. **Service account permissions**: when running as a Windows service, the
   service account needs read access to the share (domain rights or persisted
   credentials via `cmdkey`/`net use`). Do not rely on interactive mapped
   drive letters — they are not visible to services.
4. **Timeouts**: stdlib file I/O has no per-call timeout; when an SMB target is
   unreachable the OS may block for tens of seconds before failing. The day
   stays unmarked and is retried next pass; tune SMB client timeouts if needed.
5. **Trigger discipline**: only one trigger should run the collector against one
   journal at a time (service OR task scheduler, not both), otherwise add an
   external lock.
6. **Time zone**: "yesterday" = the collector host's local day. Keep source and
   collector hosts on the same time zone (or define the boundary explicitly).

## Run modes

Both modes are the same binary; the difference is `-period`.

```bash
# Timer / one-shot mode: one catch-up pass, then exit (exit code 1 on failure)
go run ./cmd/backfill -seed
go run ./cmd/backfill -seed -since 2026-09-01

# Service mode: first pass immediately, then repeat every period until Ctrl-C
go run ./cmd/backfill -service -period 5s -seed
```

Flags:

| flag | default | meaning |
|---|---|---|
| `-site` / `-line` | `plant-1` / `line-a` | metadata substituted into the path template |
| `-format` | `csv` | source format: `csv` or `tsv` (see extension points) |
| `-since` | 3 days ago | earliest expected day `YYYY-MM-DD` |
| `-data` | temp dir | data root: `{data}/in` (seeded sources), `{data}/out`, `{data}/journal` |
| `-period` | `0` | service repeat period; `0` = one-shot |
| `-service` | `false` | stay resident and repeat every `-period` |
| `-missing-ok` | `false` | absent source = "no data", mark done (local sources only!) |
| `-seed` | `false` | generate deterministic sample sources for `Since..yesterday` |

Production wiring notes (framework):

- the collector is a `runtime.Component`; one catch-up pass = one Activation;
- per-day failure surfaces as Fiber `Failed` only in timer mode
  (`FailOnError=true`); in service mode errors are logged and retried on the
  next tick without killing the resident fiber;
- Ctrl-C unwinds the service through the Kernel cleanup path.

## Extension points: what to change when requirements change

The catch-up loop, journal, attempts ledger, cancellation and power-loss
semantics are storage/format-agnostic. Three seams absorb every business change
named below:

```text
Config.Template + Config.Extra   -> WHERE the source file is
Config.Format + a Decoder        -> WHAT the source file looks like
Config.Sink + a Sink             -> WHERE the normalized day goes
```

| Your change | Code to touch | Everything else |
|---|---|---|
| CSV 变体（分号分隔、无表头、注释行…） | add a `Decoder`/options in `decoderFor` (e.g. a `delimitedDecoder{comma: ';'}`) + set `Config.Format` | unchanged |
| 增加另一种源格式（TSV/JSONL/Parquet…） | implement `Decoder` (`Decode(io.Reader) ([][]string, error)`) + register it in `decoderFor`; source file is opened by the same template/date logic | unchanged |
| 源文件列顺序/结构变了 | normalize inside the `Decoder` so rows are already in the canonical column order downstream expects | unchanged |
| 路径新增业务元数据（`{batch}` `{shift}` `{machine}`…） | add keys to `Config.Extra` and use them in `Config.Template` (`renderPath` resolves any `{key}` + date placeholders) | unchanged |
| 输出从本地文件换成数据库 | implement `Sink` (`Write(date, rows) error`); commit per date and make it idempotent (e.g. `BEGIN; DELETE FROM day WHERE date=?; INSERT ...; COMMIT`), because a power loss before the journal mark re-collects a day | unchanged |
| 输出换成消息队列/对象存储 | same: a new `Sink`; keep idempotency per date | unchanged |
| 采集触发/部署方式 | only `main.go` (service vs timer) | unchanged |
| 换框架无关（journal 目录、保留策略） | `Config.JournalPath` / retention tooling | unchanged |

A database `Sink` sketch (offline-safe, idempotent per date):

```go
type dbSink struct{ db *sql.DB } // driver not vendored in this repo

func (s dbSink) Write(date time.Time, rows [][]string) error {
    tx, _ := s.db.Begin()
    defer tx.Rollback()
    _, _ = tx.Exec(`DELETE FROM collected WHERE day = ?`, date.Format("2006-01-02"))
    stmt, _ := tx.Prepare(`INSERT INTO collected (day, row_no, fields) VALUES (?, ?, ?)`)
    for i, row := range rows {
        // row_no = i + 1 makes re-collection overwrite the same rows
        if _, err := stmt.Exec(date.Format("2006-01-02"), i+1, strings.Join(row, "\x1f")); err != nil {
            return err
        }
    }
    return tx.Commit() // only after this returns does the journal mark the day
}
```

## Files

```text
core.go        business core (journal, attempts ledger, planner, render, CSV)
main.go        framework wiring: Component + Config Controller + run modes
core_test.go   B-01..B-07 semantic tests
```

Tests: `go test ./cmd/backfill/` — covers late start, power loss between days,
cancellation, path rendering, torn journal tail, remote defer/retry across
restarts, and `missing-ok` write-off semantics.
