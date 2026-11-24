namespace Gocordis.Plugins;

/// <summary>
/// Paper §5.2.2, phases 1–2. A CLR module is identified by an assembly/URL;
/// imports are the assembly dependency graph. Shared contracts, Core and other
/// non-replaceable assemblies are externals: they bound the traversal instead
/// of being accepted.
/// </summary>
public sealed record HmrRequest(
    IReadOnlySet<string> Stashed,
    IReadOnlySet<string> Externals,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Imports,
    IReadOnlyList<PluginManifest> Entries);

public sealed record HmrPlan(
    IReadOnlySet<string> Accepted,
    IReadOnlySet<string> Declined,
    IReadOnlyList<string> StaleEntryIds,
    IReadOnlyList<PluginManifest> Desired,
    // Changed externals (shared contracts, Gocordis.Core, the host) cannot be
    // hot-swapped. They are reported as a user-visible restart-required boundary.
    IReadOnlyList<string>? RestartRequired = null);

/// <summary>
/// Three-phase HMR that submits ONE declaration batch to PluginRegistry.
/// It never owns fibers, modules or a second lifecycle state machine: Core's
/// retire→reinsert and the registry's transaction are the only authorities.
/// In CLR terms, the module cache backup is the old collectible ALC/payload,
/// which is kept alive by the old fiber until the replacement commits.
/// </summary>
public static class HmrEngine
{
    public static HmrPlan Plan(
        HmrRequest request, Func<PluginManifest, PluginManifest>? replaceModule = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        // get_imports is read in both directions: a changed module invalidates
        // what it imports AND what imports it. This is the CLR equivalent of
        // following the ES module dependency graph outwards from stashed files.
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (url, imports) in request.Imports)
        {
            foreach (var imported in imports)
            {
                if (!reverse.TryGetValue(imported, out var importers))
                {
                    reverse[imported] = importers = new HashSet<string>(StringComparer.Ordinal);
                }

                importers.Add(url);
            }
        }

        var declined = new HashSet<string>(request.Externals, StringComparer.Ordinal);
        var accepted = new HashSet<string>(request.Stashed, StringComparer.Ordinal);
        // Externals are never hot-replaced even when a source watcher reports
        // them as changed: they require a host restart instead.
        accepted.ExceptWith(declined);
        var pending = new HashSet<string>(StringComparer.Ordinal);
        void Seed(string url)
        {
            foreach (var imported in ImportsOf(request, url, reverse))
            {
                if (!accepted.Contains(imported) && !declined.Contains(imported) && pending.Add(imported))
                {
                    Seed(imported);
                }
            }
        }

        foreach (var url in request.Stashed)
        {
            Seed(url);
        }

        bool progress;
        do
        {
            progress = false;
            foreach (var url in pending.ToArray())
            {
                var imports = ImportsOf(request, url, reverse);
                if (imports.Any(accepted.Contains))
                {
                    accepted.Add(url);
                    pending.Remove(url);
                    progress = true;
                }
                else if (imports.All(declined.Contains))
                {
                    declined.Add(url);
                    pending.Remove(url);
                    progress = true;
                }
                else
                {
                    foreach (var imported in imports)
                    {
                        if (!accepted.Contains(imported) && !declined.Contains(imported))
                        {
                            pending.Add(imported);
                        }
                    }
                }
            }
        }
        while (progress);

        // Undecided modules (import cycles) and anything not reached from the
        // changed set default to declined: they are the immutable boundary.
        foreach (var url in pending)
        {
            declined.Add(url);
        }

        foreach (var url in request.Imports.Keys)
        {
            if (!accepted.Contains(url))
            {
                declined.Add(url);
            }
        }

        var stale = new List<string>();
        foreach (var entry in request.Entries)
        {
            var module = entry.Url ?? entry.Id;
            var tree = Dependencies(request, module, declined, reverse);
            if (tree.Overlaps(accepted))
            {
                stale.Add(entry.Id);
                foreach (var url in tree)
                {
                    accepted.Add(url);
                }
            }
        }

        var desired = request.Entries
            .Select(entry => stale.Contains(entry.Id, StringComparer.Ordinal) && replaceModule is not null
                ? replaceModule(entry)
                : entry)
            .ToArray();

        var restartRequired = request.Stashed
            .Where(url => request.Externals.Contains(url))
            .OrderBy(url => url, StringComparer.Ordinal)
            .ToArray();
        return new HmrPlan(accepted, declined, stale, desired, restartRequired);
    }

    /// <summary>Phase 3: commit the planned declarations in one registry
    /// transaction. A failed reload rolls back declarations, fibers and the
    /// collectible ALC generations through the existing Core path.
    ///
    /// Cancellation is PREFLIGHT-ONLY: the token is observed exactly once, at
    /// entry, before planning and before the registry transaction starts. Once
    /// the transaction has begun, cancellation is deliberately ignored — even
    /// if a staged component cancels the caller's token — so a reload is never
    /// interrupted halfway through its single declaration batch. A caller that
    /// wants to abandon the commit must cancel before calling this method.
    /// </summary>
    public static async Task<HmrPlan> ReloadAsync(
        PluginRegistry registry,
        HmrRequest request,
        Func<PluginManifest, PluginManifest>? replaceModule = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "HMR reload cancellation is preflight-only: cancel before the registry transaction " +
                "starts; a started transaction cannot be interrupted.",
                cancellationToken);
        }

        var plan = Plan(request, replaceModule);
        if (plan.StaleEntryIds.Count == 0)
        {
            return plan;
        }

        // No token crosses this boundary. PluginRegistry.ApplyAsync validates,
        // stages and commits as ONE atomic transaction; its RollbackAsync
        // restores the previous declarations if any step fails for a real
        // reason. Cancellation is not one of those reasons by design.
        await registry.ApplyAsync(plan.Desired);
        return plan;
    }

    private static IReadOnlyList<string> ImportsOf(
        HmrRequest request, string url, IReadOnlyDictionary<string, HashSet<string>> reverse)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (request.Imports.TryGetValue(url, out var imports))
        {
            foreach (var imported in imports)
            {
                result.Add(imported);
            }
        }

        if (reverse.TryGetValue(url, out var importers))
        {
            foreach (var importer in importers)
            {
                result.Add(importer);
            }
        }

        return [.. result];
    }

    private static HashSet<string> Dependencies(
        HmrRequest request, string root, HashSet<string> declined,
        IReadOnlyDictionary<string, HashSet<string>> reverse)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        void Traverse(string url)
        {
            if (result.Contains(url) || declined.Contains(url))
            {
                return;
            }

            result.Add(url);
            foreach (var child in ImportsOf(request, url, reverse))
            {
                Traverse(child);
            }
        }

        Traverse(root);
        return result;
    }
}
