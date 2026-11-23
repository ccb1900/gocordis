# Review Round 11 — Live Product Run（历史记录）

> 归档说明：以下提交 `effd312c` / 分支 `migration/dotnet-complete` / 443/443 计数均为**当时的历史快照**，
> 不代表当前 HEAD。当前状态见 [`../current-status.md`](../current-status.md)。

代码提交：`effd312c`。分支：`migration/dotnet-complete`。

本轮不是静态审查：直接生成应用、真实编译/打包/安装一个 Managed 插件、启动
Web 控制台并打开浏览器检查页面。

## 发现并关闭

1. 控制面页面声明 `renderer: "plugins"`，Console 只注册了 `plugin-explorer`，
   页面显示“渲染器未注册”。已改为 `plugin-explorer`，并加产品测试断言。
2. CLI `plugin pack` 没有 `--backend/--entry-assembly/--entry-type`，
   `plugin create` 生成的 C# 插件无法用标准 CLI 打成可执行的 Managed 包。
   已补齐三个选项并加包往返测试。
3. 空列表文案误导用户去“激活 plugin-explorer 组件”。实际上
   `plugin-explorer` 只是 Console renderer，数据源是控制面 `/api/plugins`。
   文案已改为“安装并声明一个插件后刷新；数据来自插件控制面 /api/plugins”。

## 实时运行证据

```bash
# 应用与插件
gocordis plugin create /tmp/gocordis-demo/plugin --id hello --name Hello --type hello --version 1.0.0
cd /tmp/gocordis-demo/plugin && dotnet build hello.csproj -c Release
gocordis plugin pack payload hello-1.0.0.gocordis-plugin \
  --id hello --version 1.0.0 --type hello --host-range "[1.0,)" \
  --backend managed --entry-assembly hello.dll --entry-type hello.Plugin
gocordis plugin install hello-1.0.0.gocordis-plugin --store /tmp/gocordis-run/store --host-version 1.0.0
gocordis-platform --manifest /tmp/gocordis-run/gocordis.app.json \
  --store /tmp/gocordis-run/store --address http://127.0.0.1:0 \
  --host-version 1.0.0 --host web
```

运行实例：`http://127.0.0.1:63091/`。

实际 HTTP 结果：

- `/api/meta` → `gocordis-console`
- `/api/plugins` → `hello` 为 `Active`，`installedVersion=1.0.0`，
  components=`Hello`，capabilities 含 `hello.log` 与 `managed-instance:hello@1.0.0`
- `/api/plugins/store` → 真实 checksum、declared=true、running=true
- `/api/ui/manifest` → `plugin-control-plane` 页，renderer=`plugin-explorer`

浏览器实际视口显示插件表格、启停开关、版本/声明/状态列和详情面板；观察流在线，
暗色主题正常。SSE 与主题状态在侧栏可见。

## 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 443/443，连续 3 次全绿
```

CI 未核验。
