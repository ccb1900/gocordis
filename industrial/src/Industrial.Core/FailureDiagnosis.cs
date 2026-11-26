namespace Industrial.Core;

/// <summary>User-facing classification of an operational failure. The raw
/// exception text stays available in `detail`; the UI shows category, message
/// and the next action a plant operator can actually take.</summary>
public sealed record FailureDiagnosis(string Code, string Message, string Action);

public static class FailureDiagnoses
{
    public static FailureDiagnosis Classify(string? error)
    {
        var text = error ?? string.Empty;
        if (Contains(text, "not found", "找不到", "no longer registered"))
        {
            return new FailureDiagnosis("RESOURCE_MISSING",
                "找不到配置引用的数据源、凭证或资源。",
                "到“资源/凭证”页面检查 ID 是否存在且已启用。");
        }
        if (Contains(text, "credential", "unauthorized", "authentication", "access denied", "1219", "凭据"))
        {
            return new FailureDiagnosis("CREDENTIAL",
                "目标拒绝了当前凭证。",
                "检查凭证内容并在“凭证”页面轮换后重试。");
        }
        if (Contains(text, "connection refused", "timeout", "timed out", "socket", "network", "无法连接"))
        {
            return new FailureDiagnosis("CONNECTION",
                "无法连接目标地址。",
                "检查地址、端口、网络和防火墙，再用“测试连接”确认。");
        }
        if (Contains(text, "csv", "parse", "malformed", "解析", "bad data"))
        {
            return new FailureDiagnosis("PARSE",
                "源文件中有无法解析的记录。",
                "检查文件编码、分隔符与表头配置，查看文件级详情中的错误行。");
        }
        if (Contains(text, "lease", "contested", "held by another", "租约", "claim"))
        {
            return new FailureDiagnosis("LEASE",
                "本次交付中断，租约被回收或被其他运行持有。",
                "等待自动恢复完成；如仍失败，再从交付记录重试该目标。");
        }
        if (Contains(text, "validation", "invalid workflow", "required", "校验", "配置错误"))
        {
            return new FailureDiagnosis("CONFIGURATION",
                "节点配置未通过校验。",
                "打开设计器点击“校验”，按定位的问题修改节点配置。");
        }
        if (Contains(text, "identity", "changed on disk", "文件身份", "contenthash"))
        {
            return new FailureDiagnosis("IDENTITY",
                "源文件在采集过程中发生变化。",
                "确认文件名与内容未被覆盖，然后重新补采该文件。");
        }
        return new FailureDiagnosis("OTHER",
            string.IsNullOrWhiteSpace(text) ? "暂无错误详情。" : text,
            "查看执行记录中的原始错误并联系维护人员。");
    }

    private static bool Contains(string text, params string[] needles) => needles.Any(needle =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
