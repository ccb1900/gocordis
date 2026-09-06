// Command backfill is a runnable demo of an industrial CSV collector with
// power-loss-safe catch-up semantics, built on the Dynamic Composable Runtime.
//
// Scenario (from the user):
//   - an industrial PC collects CSV files; it runs either as a long-lived
//     service or is invoked by the OS task scheduler (systemd / Windows Task
//     Scheduler) at a daily time point;
//   - the run collects YESTERDAY's data, so there is no need to watch files;
//   - the source path changes per date and carries metadata ({site}/{line}/...);
//   - the host may lose power and miss its time point; when it comes back days
//     later, no day of data may be lost.
//
// Design mapped onto the framework:
//
//	"the scheduler is not the source of truth; the JOURNAL is."
//	Every run (service tick or timer invocation) is a catch-up pass that
//	collects [Since .. yesterday] minus the per-day completion journal, marking
//	each day only after its output is fully written. Power loss / late start
//	only ever leaves unmarked days, which the next run re-collects.
//
// The framework's role: the collector is a runtime.Component; each run is one
// Activation; per-day failures become a Failed Fiber (isolated); in service
// mode the same Fiber stays Active and re-runs the pass on its own schedule,
// and Ctrl-C unwinds it through the Kernel's normal cleanup path.
//
// Remote sources (Windows SMB/UNC) are supported by putting the UNC root in
// the template (\\nas\plant-data\{site}\...): the collector only READS the
// source and writes journal/out on the LOCAL machine. Days that are missing or
// unreachable never enter the completion journal - they accumulate in
// extensions/attempts.log (count + last error) and are retried by the next
// pass, so a file that arrives late is never lost. See README.md.
//
// Run:
//
//	go run ./cmd/backfill -seed -since 2026-09-01        # one pass (timer mode)
//	go run ./cmd/backfill -service -period 5s -seed ...  # long-lived service
//
// Flags: -site -line -since -data -period -service -missing-ok -seed
package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Plugin: the backfill collector component (this is the "business body").
// ---------------------------------------------------------------------------

// backfillComp runs catch-up passes. One Apply = at least one full pass; with a
// Period > 0 the same activation stays resident and repeats the pass.
type backfillComp struct {
	id  string
	cfg Config
}

func (c *backfillComp) Name() string                  { return "backfill:" + c.id }
func (c *backfillComp) Inject() []runtime.Dependency  { return nil }
func (c *backfillComp) Provide() []runtime.Capability { return nil }

func (c *backfillComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	stop := make(chan struct{})
	done := make(chan struct{})

	run := func() error {
		reports, err := RunCatchUp(ctx.Context(), c.cfg)
		for _, r := range reports {
			if r.Err != nil {
				fmt.Printf("   [%s] %s ERROR: %v\n", c.id, formatDate(r.Date), r.Err)
				continue
			}
			fmt.Printf("   [%s] %s collected %d rows -> %s\n", c.id, formatDate(r.Date), r.Rows, filepath.Base(r.Out))
		}
		if err != nil {
			fmt.Printf("   [%s] pass finished with errors: %v\n", c.id, err)
		} else {
			fmt.Printf("   [%s] pass complete (no missing days)\n", c.id)
		}
		if out, aerr := snapshotAttempts(c.cfg); aerr == nil && len(out) > 0 {
			for _, a := range out {
				fmt.Printf("   [%s] OUTSTANDING %s kind=%s attempts=%d last=%s\n",
					c.id, formatDate(a.Date), a.Kind, a.Count, a.LastError)
			}
		}
		return err
	}

	firstErr := run()
	if firstErr != nil && c.cfg.FailOnError {
		// Timer/CLI mode: surface the failure so the process exits non-zero.
		return nil, firstErr
	}

	if c.cfg.Period <= 0 {
		return nil, nil // one-shot activation (timer mode)
	}

	// Service mode: stay resident and repeat the pass on a fixed period.
	go func() {
		defer close(done)
		t := time.NewTicker(c.cfg.Period)
		defer t.Stop()
		for {
			select {
			case <-stop:
				return
			case <-ctx.Context().Done():
				return
			case <-t.C:
				_ = run() // service keeps running; errors are logged, not fatal
			}
		}
	}()

	return func() error {
		close(stop)
		<-done
		return nil
	}, nil
}

// ---------------------------------------------------------------------------
// Host: factories + manifest + run modes
// ---------------------------------------------------------------------------

type adapterFactory struct {
	build func(config.ComponentConfig) (runtime.Component, error)
}

func (a *adapterFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return a.build(cc)
}

func main() {
	// -- data root & runtime knobs -------------------------------------------
	var (
		site, line string
		format     string
		since      string
		data       string
		period     time.Duration
		service    bool
		missingOK  bool
		seed       bool
	)
	flag.StringVar(&site, "site", "plant-1", "metadata: site id")
	flag.StringVar(&line, "line", "line-a", "metadata: production line")
	flag.StringVar(&format, "format", "csv", "source format: csv | tsv")
	flag.StringVar(&since, "since", "", "earliest expected day YYYY-MM-DD (default: 3 days ago)")
	flag.StringVar(&data, "data", "", "data root: {data}/in, {data}/out, {data}/journal (default: temp dir)")
	flag.DurationVar(&period, "period", 0, "service mode period between passes (0 = one-shot)")
	flag.BoolVar(&service, "service", false, "stay resident and repeat every -period")
	flag.BoolVar(&missingOK, "missing-ok", false, "treat an absent source file as 'no data' and mark the day done")
	flag.BoolVar(&seed, "seed", false, "generate deterministic sample source files for Since..yesterday")
	flag.Parse()

	if since == "" {
		since = time.Now().AddDate(0, 0, -3).Format("2006-01-02")
	}
	if data == "" {
		d, err := os.MkdirTemp("", "backfill-*")
		if err != nil {
			log.Fatal(err)
		}
		data = d
	}
	tmpl := filepath.Join(data, "in", "{site}", "{line}", "{YYYY}", "{MM}", "{DD}", "raw.csv")
	if seed {
		if err := seedSources(map[string]string{"site": site, "line": line}, since, tmpl, format); err != nil {
			log.Fatal(err)
		}
	}

	cfg := Config{
		Site:         site,
		Line:         line,
		Format:       format,
		Template:     tmpl,
		Since:        since,
		JournalPath:  filepath.Join(data, "journal", "done.log"),
		AttemptsPath: filepath.Join(data, "journal", "attempts.log"),
		OutDir:       filepath.Join(data, "out"),
		MissingOK:    missingOK,
		FailOnError:  !service,
		Period:       period,
		Now:          time.Now,
	}

	fmt.Printf("== backfill collector demo ==\n")
	fmt.Printf("   site=%s line=%s since=%s\n", site, line, since)
	fmt.Printf("   template=%s\n", tmpl)
	fmt.Printf("   mode=%s\n", map[bool]string{true: "service (periodic)", false: "timer (one pass, then exit)"}[service])

	// Framework wiring: Runtime + Config FactoryRegistry + Config Controller.
	rt, err := runtime.New()
	if err != nil {
		log.Fatalf("runtime.New: %v", err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("backfill", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return &backfillComp{id: cc.ID, cfg: cfg}, nil
	}}); err != nil {
		log.Fatalf("register: %v", err)
	}
	ctrl := config.NewController(rt, reg)

	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancel()

	// Manifest: one collector. Reconcile hands the Component to the Runtime;
	// the Activation does the catch-up pass.
	if err := ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{{ID: "collector", Type: "backfill"}}}); err != nil {
		log.Fatalf("reconcile: %v", err)
	}
	fiber := ownedFiber(ctrl, "collector")

	if !service {
		// Timer mode: wait for the one-shot Activation to finish.
		rctx, rcancel := context.WithTimeout(context.Background(), 30*time.Second)
		defer rcancel()
		if err := fiber.Ready(rctx); err != nil {
			fmt.Printf("== collector Failed (partial progress saved; re-run is safe): %v ==\n", err)
			if out, aerr := snapshotAttempts(cfg); aerr == nil {
				for _, a := range out {
					fmt.Printf("   OUTSTANDING %s kind=%s attempts=%d last=%s\n",
						formatDate(a.Date), a.Kind, a.Count, a.LastError)
				}
			}
			_ = ctrl.Close()
			_ = rt.Close(context.Background())
			os.Exit(1)
		}
		fmt.Printf("== collector Active; outputs in %s ==\n", cfg.OutDir)
		if err := ctrl.Close(); err != nil {
			log.Fatalf("ctrl.Close: %v", err)
		}
		if err := rt.Close(context.Background()); err != nil {
			log.Fatalf("rt.Close: %v", err)
		}
		fmt.Println("== done ==")
		return
	}

	// Service mode: keep running until Ctrl-C, then unwind gracefully.
	<-ctx.Done()
	fmt.Println("\n== shutting down (kernel unwinds the collector) ==")
	sctx, scancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer scancel()
	if err := ctrl.CloseContext(sctx); err != nil {
		log.Fatalf("ctrl.CloseContext: %v", err)
	}
	if err := rt.Close(context.Background()); err != nil {
		log.Fatalf("rt.Close: %v", err)
	}
	fmt.Println("== stopped ==")
}

func ownedFiber(ctrl *config.Controller, id string) *runtime.Fiber {
	for _, o := range ctrl.Owned() {
		if o.ID == id {
			return o.Fiber
		}
	}
	return nil
}

// seedSources writes one deterministic source file per day in
// [Since .. yesterday] so the demo has something to collect without external
// files. The separator follows the selected source format (csv/tsv).
func seedSources(meta map[string]string, since, tmpl, format string) error {
	s, err := parseDate(since)
	if err != nil {
		return err
	}
	sep := ","
	if format == "tsv" {
		sep = "\t"
	}
	y := yesterday(time.Now())
	for d := today(s); !d.After(y); d = d.AddDate(0, 0, 1) {
		p := renderPath(tmpl, meta, d)
		if _, err := os.Stat(p); err == nil {
			continue // keep existing files (a re-run does not fabricate new days)
		}
		if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
			return err
		}
		ts := d.Format("2006-01-02T15:04:05")
		content := "ts" + sep + "value\n" + ts + sep + "1\n" + ts + sep + "2\n"
		if err := os.WriteFile(p, []byte(content), 0o644); err != nil {
			return err
		}
	}
	return nil
}
