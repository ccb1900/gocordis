> **C# 迁移状态**：完整迁移在 [`migration/dotnet-complete`](https://github.com/ccb1900/gocordis/tree/migration/dotnet-complete) 分支。
> 该分支包含统一 Runtime、Managed/Process/WASM 后端、插件包/CLI、Web/WinForms/Avalonia UI 契约与完整验收账本，
> 入口见 [docs/migration/final-acceptance.md](docs/migration/final-acceptance.md)。
> 本文件以下部分描述 Go 基线，Go 仓库已冻结，不再作为新应用入口。

# gocordis — 动态可组合运行时(Go)

以论文 *[A Programming Paradigm for Spatiotemporal Composability](docs/论文.pdf)*
为唯一规范来源的插件运行时实现:可逆 effect(时间维)、反应式 coeffect(空间维)、
统一 context,以及其上的声明式装载、配置调和与热替换。

- **用户手册(从这里开始)**:[docs/guide/user-manual.md](docs/guide/user-manual.md)
- **开发者手册(框架内幕)**:[docs/guide/developer-manual.md](docs/guide/developer-manual.md)
- **LLM/代理开发规范**:[AGENTS.md](AGENTS.md)(十二铁律 + 两部分项目结构 + 禁止清单)
- 语义对照与路线图:[docs/ROADMAP.md](docs/ROADMAP.md)
- 设计裁决:[docs/adr/](docs/adr/) · 评审记录:[docs/review/](docs/review/)
- 可运行示例:[cmd/](cmd/)(collector / backfill / httpd / wasmhmr / host / example)

## 一条命令启动控制台

```sh
go run ./cmd/platform        # 打开 http://127.0.0.1:8170/
```

无需 npm:`web/console/dist` 被 gitignore,新克隆里没有构建产物,此时 webui
直接由 `go:embed` 提供**内嵌 bootstrap UI**(`extensions/console/webui/bootstrap/`)。
它用原生 JS 显示:页面导航(`/api/ui/pages`)、运行时快照表格与计数
(`/api/query/runtime/snapshot`:fibers / providers / deps / effects)、框架仪表盘
计数(`/api/query/framework/dashboard`)。查询尚未注册时对应区块给出提示,其余照常工作。

Bootstrap 的定位是**开箱即用的基础诊断界面**,不是完整 Console 的等价实现:
Scopes 表格、事件时间线、诊断明细与插件管理操作仍以完整 SPA 为准。它刻意保持只读、
零依赖,避免为两种前端维护同一套复杂能力。

需要完整的 React SPA(可选,仅 UI 更丰富,API 不变):

```sh
make console                # = cd web/console && npm ci && npm run build
```

只要 dist 中存在 `index.html`,就自动改用完整 SPA;删除 dist 即回退到 bootstrap。

地址与环境变量:`GOCORDIS_ADDR`(默认 `127.0.0.1:8170`)、`GOCORDIS_WEB_DIST`
(指定 dist 目录,不存在时同样回退到 bootstrap)、`GOCORDIS_MANIFEST`、`GOCORDIS_CATALOG_DIR`。
控制台默认无鉴权,勿直接暴露到公网。详见 [用户手册 §13](docs/guide/user-manual.md#13)。

```go
rt, _ := runtime.New()
f, _ := rt.Load(&MyComponent{})
_ = f.Ready(ctx)   // Active ⇔ 组件真实在服务
```

质量门:`make verify`(gofmt + vet + test + race),另含确定性种子 fuzz。
