# proc — 进程外插件后端(paper §6.2 Cross-process invocation)

Go 原生代码无法在运行时安装/卸载(`plugin` 包不能卸载、锁工具链、无 Windows)。
本包给出进程外的正解:**插件 = 独立 OS 进程**,经 stdin/stdout 上的行分隔
JSON-RPC 2.0 通信;宿主侧一个组件把它链接为 remote provider。安装/卸载 =
进程生命周期,原生性能,语言不限(任何能读写 stdio 的可执行文件)。

## 边界(务必阅读)

- **进程边界不是安全沙箱**:插件以宿主用户权限运行。不受信代码请用
  `extensions/loader/wasm`(论文 §6.3:沙箱需要外部机制)。
- **异步容忍契约**(论文 §6.2 caveat):跨进程调用有延迟、可能中途失败——
  每条调用路径都返回 error,调用方不得假设顺序或零失败。
- v1 限制:每连接**顺序调用**(一个在飞请求);插件→宿主的方法请求不支持
  (会收到 -32601);插件崩溃后**不自动重启**(论文:无自动重试;恢复 =
  重新启用/revision)。

## 用法

```go
// 1. 宿主声明契约:能力 key + 接口 + RPC 绑定(放应用的 contracts 包)
type Codec interface {
    Encode(string) (string, error)
}
var CodecKey = runtime.NewKey[Codec]("app.codec")

backend := proc.NewBackend(CodecKey,
    func(call proc.Caller) Codec {
        return &remoteCodec{call: call} // 把方法映射到 JSON-RPC
    },
    proc.WithArgs("-serve"),
)

// 2. 经 Loader 装载(Source = 可执行文件路径),Factory 注册进 config
ld.RegisterBackend(proc.BackendType, backend)
m, _ := ld.Load(ctx, loader.Artifact{ID: "codec", BackendType: proc.BackendType,
    Source: "./build/plugin-codec", Version: "v2"})
reg.Register(m.Type, config.FactoryFunc(m.Factory.Create))

// 3. 之后的一切都是标准 Runtime 语义:Enabled 开关、调和、HMR 替换、
//    失败隔离、依赖门控……卸载时激活撤销优雅停进程(exit 前收 shutdown)。
```

## 插件侧(任何语言,协议 4 条)

1. 启动后在 stdout 写一行握手头:`GORIDIS-PROC-PLUGIN 1`;
2. 之后按行服务 JSON-RPC 2.0:请求 `{"jsonrpc":"2.0","id":N,"method":"m","params":{...}}`,
   响应 `{"jsonrpc":"2.0","id":N,"result":{...}}` 或 `{"error":{"code":C,"message":"M"}}`
   (**每条消息写一行并立即 flush**);
3. 收到方法 `"shutdown"` → 回包后退出(宿主超时后强杀);
4. 其余行为自由;stderr 不承载协议。

Go 插件直接用本包:`proc.Serve(map[string]proc.Handler{...})`(阻塞,关闭后返回)。

## 生命周期

与 WASM 后端同构:**每次激活物化一个全新进程**,Apply 内 spawn + 握手 +
`ctx.Effect`(逆 = shutdown → 有界等待 → kill → wait),之后 Provide 契约能力
——Active ⇔ 进程在服务;卸载先于终态。进程属激活所有,Backend.Close 只拒绝
新 Load,不杀活进程。激活 context 取消是兜底强杀。
