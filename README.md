# gocordis — 动态可组合运行时(Go)

以论文 *[A Programming Paradigm for Spatiotemporal Composability](docs/论文.pdf)*
为唯一规范来源的插件运行时实现:可逆 effect(时间维)、反应式 coeffect(空间维)、
统一 context,以及其上的声明式装载、配置调和与热替换。

- **应用开发指南(从这里开始)**:[docs/guide/developing-applications.md](docs/guide/developing-applications.md)
- 语义对照与路线图:[docs/ROADMAP.md](docs/ROADMAP.md)
- 设计裁决:[docs/adr/](docs/adr/) · 评审记录:[docs/review/](docs/review/)
- 可运行示例:[cmd/](cmd/)(collector / backfill / httpd / wasmhmr / host / example)

```go
rt, _ := runtime.New()
f, _ := rt.Load(&MyComponent{})
_ = f.Ready(ctx)   // Active ⇔ 组件真实在服务
```

质量门:`make verify`(gofmt + vet + test + race),另含确定性种子 fuzz。
