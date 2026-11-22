# Review-Fix Round 5

本轮关闭剩余“远程 package registry”缺口，明确做只读插件源，不包装成 Koishi 市场服务。

| 能力 | 实现 | 红测 |
| --- | --- | --- |
| HTTP 插件源 | `HttpPluginPackageSource` 读取 index.json、按 NuGet 范围选最高版本、校验文件 SHA-256 与包完整性、下载到临时文件 | `HttpSourceResolvesHighestMatchingPackageAndVerifiesChecksum` |
| 本地只读 registry | `PluginSourceServer` 扫描目录生成 `/index.json`，只服务索引中的包文件，拒绝路径穿越，坏包跳过不 500 | `LocalRegistryServesIndexAndPackageWithTraversalRejected` |
| CLI | `gocordis registry serve` 启动只读源；`gocordis plugin fetch` 从源下载包到文件 | `CliFetchAndRegistryServeAreWired` |

边界：

- registry 只读，不安装、不鉴权、不写 manifest。
- 默认 loopback；远程绑定需显式 opt-in。
- 不是 Koishi 市场服务端；不包含发布、评分、权限或云端分发。
- `plugin fetch` 产物是普通 `.gocordis-plugin`，可交给现有 install/doctor。

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 392/392，连续 3 次
npm test                 → 85 passed
npm run build            → success
```
