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
