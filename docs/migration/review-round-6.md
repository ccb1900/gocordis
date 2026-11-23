# Review-Fix Round 6

本轮关闭“包签名 / 来源真实性”缺口，使用成熟 .NET RSA，不破坏既有无签名包。

| 能力 | 实现 | 红测 |
| --- | --- | --- |
| 密钥生成 | `plugin keygen` 生成 RSA-2048 PKCS#8 private PEM + SPKI public PEM，默认不覆盖 | `SignCommandAndDoctorPublicKeyAreWired` |
| 包签名 | `plugin sign` 对规范摘要（manifest + payload 排序长度前缀哈希）做 RS256 签名，写入 `signature.json` | `SignVerifyRoundTripPreservesPackageIntegrity` |
| 验签 | `VerifyAsync` / `doctor --public-key`；缺签名、算法错误、篡改统一 `PackageSignatureException` 并映射 error diagnostic | `TamperedSignedPackageFailsVerification`、`UnsignedPackageWithPublicKeyFailsDoctor` |
| 兼容性 | Reader 跳过 `signature.json`，原有 manifest checksum / payload hash 不变；无签名包照常可读 | 现有全部包测试 |

边界：签名证明规范摘要完整性，不等于证书链或信任链；生产分发仍需密钥保管与发布策略。

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 396/396，连续 3 次
npm test                 → 85 passed
npm run build            → success
```
