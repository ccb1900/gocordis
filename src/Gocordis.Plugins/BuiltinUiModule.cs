using System.Security.Cryptography;
using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Shared registration for a builtin plugin's same-origin ESM client
/// module. Each plugin declares its OWN module and owner; this helper only
/// avoids repeating the hash/path bookkeeping, never the lifecycle.</summary>
public static class BuiltinUiModule
{
    public static async ValueTask RegisterAsync(
        Context context,
        UiContributionRegistry ui,
        ContributionOwner owner,
        string moduleId,
        string packageId,
        string version)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "client-modules", moduleId);
        var entry = Path.Combine(source, "ui.js");
        var hash = File.Exists(entry)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))[..12].ToLowerInvariant()
            : "unavailable";
        await context.RegisterUiClientModuleAsync(ui, owner, new UiClientModuleDefinition(
            moduleId,
            $"/client-modules/{moduleId}/ui.js",
            hash,
            "ui.js",
            source,
            packageId,
            version,
            hash));
    }
}
