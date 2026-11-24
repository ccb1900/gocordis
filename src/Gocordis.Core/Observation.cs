namespace Gocordis.Core;

/// <summary>
/// Paper §3.3.2 / Def 33: recovery and confluence equalities are read up to
/// observational equivalence. Fiber ids and activation/generation counters are
/// generative episode identities, not observable key-level state.
///
/// Observable projection (Def 31–37, Thm 80): component name, fiber state,
/// declared requires/provides, committed provider bindings, failure type and
/// message, failed inverse (cleanup) records, and effect-failure attributes
/// (slot, label, install/inverse, exception type/message). A failed unwind is
/// therefore NEVER equivalent to a clean one.
///
/// Deliberately ignored: Fiber.Id, Generation, Revision, ParentId and
/// ActivationId — replay retries and rehome revisions are episode counters,
/// not key-level observations. Provided VALUES are not part of this projection;
/// value equality belongs to the capability contract, not to ≃_K.
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
               a.Failure == b.Failure &&
               a.FailureType == b.FailureType &&
               a.CleanupIncomplete == b.CleanupIncomplete &&
               a.CleanupFailures.SequenceEqual(b.CleanupFailures, StringComparer.Ordinal) &&
               EffectSignature(a).SequenceEqual(EffectSignature(b), StringComparer.Ordinal) &&
               Bindings(a, left).OrderBy(x => x, StringComparer.Ordinal)
                   .SequenceEqual(Bindings(b, right).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IEnumerable<string> EffectSignature(FiberSnapshot fiber) =>
        fiber.EffectFailures.Select(record => string.Join('|',
            record.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record.Label,
            record.IsInstall ? "install" : "inverse",
            record.ExceptionType,
            record.Message));

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
