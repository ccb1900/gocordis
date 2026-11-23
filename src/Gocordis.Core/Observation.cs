namespace Gocordis.Core;

/// <summary>
/// Paper §3.3.2 / Def 33: recovery and confluence equalities are read up to
/// observational equivalence. Fiber ids, names and activation generations are
/// generative/episode counters, not observable key-level state: two schedules
/// that reach the same declarations, states and bindings are equivalent even
/// when a consumer was retried a different number of times on its way there.
/// </summary>
public static class ObservationEquivalence
{
    public static bool Equivalent(RuntimeSnapshot left, RuntimeSnapshot right)
    {
        var a = left.Fibers.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var b = right.Fibers.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!Equivalent(a[i], b[i], left, right))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Equivalent(FiberSnapshot a, FiberSnapshot b, RuntimeSnapshot left, RuntimeSnapshot right)
    {
        return a.Name == b.Name &&
               a.State == b.State &&
               a.Requires.SequenceEqual(b.Requires) &&
               a.Provides.SequenceEqual(b.Provides) &&
               (a.Failure is null) == (b.Failure is null) &&
               Bindings(a, left).OrderBy(x => x, StringComparer.Ordinal)
                   .SequenceEqual(Bindings(b, right).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Bindings(FiberSnapshot fiber, RuntimeSnapshot snapshot)
    {
        // Replace provider ids with the provider's component name so two runs
        // with different generative ids still compare equal.
        var names = snapshot.Fibers.ToDictionary(f => f.Id, f => f.Name);
        foreach (var binding in fiber.BoundProviders)
        {
            var separator = binding.IndexOf("->", StringComparison.Ordinal);
            if (separator < 0)
            {
                yield return binding;
                continue;
            }

            var key = binding[..separator];
            var idText = binding[(separator + 2)..];
            yield return Guid.TryParse(idText, out var id) && names.TryGetValue(id, out var name)
                ? $"{key}->{name}"
                : binding;
        }
    }
}
