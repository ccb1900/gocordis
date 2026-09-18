# gocordis Windows 兼容性审查 (R14, 2026-09-09)

范围:全仓(kernel / extensions / cmd / integration / client)。
方法:`GOOS=windows go build ./...` + `go vet`(含全部测试二进制交叉编译,**零错误**)
+ 平台相关点静态走读(exec 位、信号、socket、路径、build tag、CI)。
**按用户要求:只评审、不修复**,每项给证据与修复建议。

## 结论先行

**内核与绝大多数扩展 100% 纯 Go、零平台约束文件(`//go:build` 全仓为空),
`GOOS=windows` 下全仓编译/vet/测试编译全部通过。** 唯一的硬阻断在
`extensions/loader/proc` 的可执行校验(Windows 文件没有执行位,校验恒失败),
另有一批文档/CI/默认值层面的 Windows 缺口。

---

## 发现清单

### W-1(P1,proc 后端 Windows 阻断)
- **证据**:`extensions/loader/proc/backend.go:240`
  `st.Mode()&0o111 == 0 → "not executable"`。Windows 的 `os.Stat` 不产生执行位
  (常规文件恒为 0666 类),因此**任何**合法插件可执行文件(.exe)都会被拒绝——
  proc 后端在 Windows 完全不可用。
- **对照**:用户在 `extensions/console/procplugin/component.go
  resolveExecutable` 已做过 Windows `.exe` 补全处理——同一问题在 loader/proc
  侧漏掉了,两处不一致。
- **建议**:`runtime.GOOS == "windows"` 时跳过执行位检查(仅校验 regular
  file),或校验 `.exe/.bat/.cmd` 扩展名;与 procplugin 的 resolveExecutable
  对齐。

### W-2(P2,CI 无 Windows 覆盖——历史"Windows 验证待办"的根因)
- **证据**:`.github/workflows/ci.yml` 仅 `runs-on: ubuntu-latest`。
- **影响**:本轮 `GOOS=windows` 交叉编译证明编译层零成本可达,但没有 CI
  守住就会回退(本审查即为第一次系统性验证)。
- **建议**:①加 `GOOS=windows go build ./... && go vet ./...` 编译矩阵 job
  (免费);②加 windows-latest 测试 job(`go test ./...`;`-race` 需
  CGO_ENABLED=1 + mingw,windows-latest 镜像自带,可后补)。

### W-3(P3,文档/示例)
- procplugindemo README 的排障命令 `pkill -f plugin-echo` 是 unix 工具;
  构建产物名 `build/plugin-echo` 在 Windows 是 `build\plugin-echo.exe`
  (`-o` 不会自动补 .exe),默认 `-plugin` 路径随之不匹配。
- **建议**:README 补 Windows 构建/运行/排障三行
  (`go build -o build\plugin-echo.exe ...`、`taskkill /IM plugin-echo.exe /F`)。

### W-4(P3,信号语义)
- `proc.Serve` 的 SIGINT/SIGTERM 优雅停止:Windows 不投递 SIGTERM(Ctrl+C 的
  SIGINT 由 Go 映射,半生效);`syscall.SIGTERM` 常量在 Windows 可编译。
- **影响**:无害——主停机路径是宿主 shutdown RPC + 超时 kill,行为仍正确。
- **建议**:proc README 注明"Windows 下优雅信号不投递,以 shutdown RPC 为准"。

### W-5(P3,httpd demo)
- `ServerConfig.Network: "unix"` 选项在 Windows 不可用(需 Win10+ 且支持面
  受限);默认 tcp 不受影响。
- **建议**:httpd 文档注明 unix 仅 unix 系。

---

## 已核查且判定**跨平台良好**的点

- **内核 runtime/**:零 `//go:build` 约束、零 syscall 依赖、零硬编码绝对路径
  ——100% 纯 Go 可移植;
- **wazero(WASM)**:纯 Go,官方支持 Windows;`WithCloseOnContextDone` 已接
  (R12 P2-1 顺带补了执行上限与制品大小上限,均为平台无关修复);
- **fsnotify(watch)**:Windows 官方映射 ReadDirectoryChangesW;v0.1 语义在
  Windows 成立(包注释明示);
- **proc JSON-RPC 协议**:stdio 管道为二进制模式,`\n` 帧在 Windows 语义一致;
- **TOML/scheduler/broker/observe/hub/registry/patch/bundle**:纯 Go;
- **cmd/procplugindemo**:`filepath.Abs/Join` 全程使用,manifest 双路径解析
  健壮;`signal.Notify(os.Interrupt, syscall.SIGTERM)` 在 Windows 可编译,
  Ctrl+C(SIGINT)路径生效;
- **integration**:全部经 `t.TempDir()`/`os.Executable()`/helper-process 门控,
  无 unix 专断;
- **client/(TS)**:fetch/EventSource 浏览器侧,平台无关。

---

## 处置建议优先级

**W-1 是唯一功能阻断**(proc 后端 Windows 不可用),修复约 5 行;W-2 是防
回退的 CI 投资;W-3/W-4/W-5 为文档补丁。确认后可一轮全部落地。


---

## R14b (2026-09-09, 同日):方法论修正 — Windows 分支的可回归测试化

用户指出 R14 的盲区:静态审查抓不到"mac 能跑、Windows 不行"的**运行时路径
语义**类问题(configwatch 相对路径 URI 即为一例,用户已修)。

修复分两层:

1. **可测试性重构**(`extensions/watch/file.go`):URI/路径的纯逻辑抽取为
   goos 参数化的纯函数——`uriToSlashPath`(scheme/host/解码)、
   `slashPathToOS`(盘符剥离 + 分隔符翻转)、`isAbsSlashPath`(含 UNC)、
   `fileURItoPath`/生产路径只是三者的组合。Windows 分支从此**在 macOS 上
   即可回归测试**。
2. **回归测试**(`file_uri_test.go`,七项):Windows 盘符 URI 解析、
   POSIX 路径、百分号解码(中文/空格)、非 file scheme 与 hosted URI 拒绝、
   slash→OS 双向转换、绝对性判定(含 UNC/drive-relative)。

**方法论修正记录**:静态审查(交叉编译 + 关键词走查)只能覆盖编译级与
字面量级;路径语义类缺陷的系统性防线 = ①纯函数抽取 + 双 goos 参数化测试
(本轮)②CI Windows 编译矩阵(本轮已加)③windows-latest 测试 job(后补)。


---

## R14b (2026-09-09, 同日):用户质问后的深挖 — 发现并修复 4 项运行时缺陷

用户以 configwatch 相对路径为例指出 W-2/W-3 之外存在**运行时路径/编码语义**
缺陷。逐类排查后发现并修复:

| # | 缺陷 | 修复 |
|---|---|---|
| F-A | **TOML manifest 带 UTF-8 BOM 无法解析**(Windows 记事本"UTF-8 with BOM"格式);UTF-16 LE(PowerShell 5.1 `Out-File` 默认)直接失败且报因不明 | Parse 入口剥离 UTF-8 BOM;UTF-16 → 拒绝并给出"re-save as UTF-8"可行动错误;CRLF 已由 go-toml 正常处理 |
| F-B | **CSV 数据源带 UTF-8 BOM**(backfill/collector 的 Windows SMB 场景恰好是 Windows 来源):encoding/csv 不剥 BOM,首列被污染 | backfill `delimitedDecoder` 与 collector 入口统一剥 BOM(Peek+Discard,不破坏流式) |
| F-C | **writeFileAtomic 在 Windows 的两个坑**:① 目标被杀毒/索引器短暂占用时 rename 报共享冲突,原"单次立即重试"通常撞同一把锁;② 固定 `.tmp` 名在服务+计划任务重叠运行时互踩 | CreateTemp 同目录唯一临时名 + 5 次退避重试(100ms×n);失败清理临时文件 |
| F-D | **proc 子进程句柄继承导致 Wait 永久挂起**:插件派生的子进程继承 stdout/stderr 句柄,插件退出后 cmd.Wait 仍无限等 | `cmd.WaitDelay = 5s`(Go 1.20+);优雅 RPC → 超时 kill → WaitDelay 兜底三层 |

全部修复可在 macOS 上验证(字节/逻辑层,不依赖 Windows 主机):
BOM 三态探针、writeFileAtomic 覆盖写、backfill/collector 全套件、proc 套件
+ race 全绿。

**方法论沉淀**(修正 R14 的"静态审查"局限):Windows 兼容审查的正确姿势 =
①交叉编译(编译级)②**编码语义探针**(BOM/UTF-16/CRLF,纯字节逻辑可在
macOS 上复现)③**文件生命周期走查**(create/open/rename/sync 在 Windows
的共享冲突语义)④信号/句柄继承差异。本节即按 ②③ 补做的第二轮。
