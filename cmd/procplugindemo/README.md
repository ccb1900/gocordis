# procplugindemo — 进程外插件示例(两部分:应用本体 + 插件)

宿主应用经 `extensions/loader/proc` 链接一个**独立进程**插件:插件崩了调用报
错但不拖垮应用;manifest 里翻 `enabled` 开关即停/起进程,消费者自动跟随。

## 运行

```bash
# 1. 构建插件(独立可执行文件)
go build -o build/plugin-echo ./cmd/procplugindemo/pluginecho

# 2. 在 demo 目录运行宿主(它在工作目录读 manifest.toml)
cd cmd/procplugindemo && go run . -plugin ../../build/plugin-echo
```

每 2 秒一条:`[consumer] plugin says: HELLO!` —— 这是**跨进程调用**。

## 试这些

| 操作 | 现象 |
|---|---|
| `pkill -f plugin-echo`(杀进程) | 消费者每 2s 报 `call failed: plugin call: … unavailable`;应用本体无恙(§4.4:无自动重试) |
| manifest 中 `echo` 条目改 `enabled = false` 保存 | 插件进程被优雅停止;消费者自动撤回到 Pending(gating) |
| 改回 `enabled = true` 保存 | 新进程被拉起,消费者自动重新激活 |
| Ctrl-C | 顺序关停:声明源 → 控制器 → runtime(先停插件进程,再收宿主) |

## 结构

```text
main.go            宿主:契约(Codec + Key)+ proc 后端 + Factory 注册 + consumer 组件 + 配置调和
pluginecho/main.go 插件:proc.Serve 三个方法面("encode";"shutdown" 由 Serve 代管)
manifest.toml      声明存储:哪些组件启用(持久化的唯一状态)
```

宿主与插件**互不 import**;两者共享的只有"方法名 + JSON 形状"(协议即契约)。
真正的热装卸需求(不改文件、动态下发代码)把插件写成 WASM 模块走
`extensions/loader/wasm`;本 demo 的进程形态适合**原生性能 + 可信插件**。
