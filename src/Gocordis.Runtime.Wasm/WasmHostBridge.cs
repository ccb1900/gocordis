namespace Gocordis.Runtime.Wasm;

/// <summary>
/// Capability bridge for WASM guests. The guest imports "gocordis.report";
/// the host routes it to a registered callback without exposing a file system,
/// network or arbitrary host API (untrusted guest, no security sandbox claim).
/// </summary>
public sealed class WasmHostBridge
{
    private readonly List<int> _reports = [];

    public IReadOnlyList<int> Reports => _reports;

    public Action<int>? OnReport { get; set; }

    public void Report(int code)
    {
        _reports.Add(code);
        OnReport?.Invoke(code);
    }
}
