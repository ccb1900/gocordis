namespace Gocordis.Core;

/// <summary>
/// Paper §3.3.2 / Def 31–37: recovery and confluence equalities are read up to
/// observational equivalence. Fiber ids and activation/generation counters are
/// generative episode identities, not observable key-level state.
///
/// Observable projection (Def 31–37, Thm 80): component name, fiber state,
/// declared requires/provides WITH their key identities (instance + value type
/// + name), home realm and per-key routes, committed bindings with the
/// provider's full semantic identity, failure type and message, failed inverse
/// records, and effect-failure attributes. Runtime-wide cleanup/effect failure
/// ledgers are observed too: a fiber may go Gone while its failed inverse is
/// still retained, and that is never equivalent to a clean recovery.
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
        var a = CanonicalOrder(left);
        var b = CanonicalOrder(right);
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].Canonical, b[i].Canonical, StringComparison.Ordinal) ||
                !SameFiberIdentities(a[i].Fiber, b[i].Fiber))
            {
                return false;
            }
        }

        return SameGlobalFailures(left, right);
    }

    /// <summary>
    /// Sort fibers by their FULL semantic projection, not by name alone: two
    /// same-name fibers can differ in state, realm or declared keys. Tokens for
    /// key/realm instances are assigned in a deterministic pre-walk so the
    /// final order is independent of the input array order.
    /// </summary>
    private static OrderedFiber[] CanonicalOrder(RuntimeSnapshot snapshot)
    {
        var identities = new IdentityTable();
        var fibers = snapshot.Fibers
            .Select(fiber => (Fiber: fiber, Shape: Shape(fiber)))
            .OrderBy(entry => entry.Shape, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in fibers)
        {
            foreach (var key in KeysOf(entry.Fiber))
            {
                identities.Key(key);
            }

            identities.Realm(entry.Fiber.HomeRealm);
            foreach (var route in entry.Fiber.Routes)
            {
                identities.Realm(route.Realm);
            }

            foreach (var binding in entry.Fiber.SemanticBindings)
            {
                identities.Realm(binding.Provider.Realm);
            }
        }

        return fibers
            .Select(entry => new OrderedFiber(entry.Fiber, FullProjection(entry.Fiber, identities)))
            .OrderBy(entry => entry.Canonical, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record OrderedFiber(FiberSnapshot Fiber, string Canonical);

    private static string Shape(FiberSnapshot fiber) => string.Join("|",
        Encode(fiber.Name), fiber.State.ToString(), Encode(fiber.Failure ?? string.Empty),
        Encode(fiber.FailureType ?? string.Empty), fiber.CleanupIncomplete ? "1" : "0",
        Sequence(fiber.CleanupFailures), Sequence(EffectSignature(fiber)),
        Sequence(fiber.Requires), Sequence(fiber.Provides),
        Encode(fiber.HomeRealm?.Name ?? string.Empty),
        Sequence(fiber.Routes.Select(route => Encode(route.Realm.Name))),
        Sequence(LegacyBindings(fiber)));

    private static string FullProjection(FiberSnapshot fiber, IdentityTable identities) => string.Join("|",
        Encode(fiber.Name), fiber.State.ToString(), Encode(fiber.Failure ?? string.Empty),
        Encode(fiber.FailureType ?? string.Empty), fiber.CleanupIncomplete ? "1" : "0",
        Sequence(fiber.CleanupFailures), Sequence(EffectSignature(fiber)),
        Sequence(fiber.Requires), Sequence(fiber.Provides),
        Sequence(fiber.RequiredKeys.Select(key => identities.Key(key))),
        Sequence(fiber.ProvidedKeys.Select(key => identities.Key(key))),
        identities.Realm(fiber.HomeRealm),
        Sequence(fiber.Routes.Select(route =>
            $"{identities.Key(route.Key)}@{identities.Realm(route.Realm)}")),
        Sequence(fiber.SemanticBindings.Select(binding =>
            $"{identities.Key(binding.Key)}->{ProviderProjection(binding.Provider, identities)}")),
        Sequence(LegacyBindings(fiber)));

    private static string ProviderProjection(ProviderIdentitySnapshot provider, IdentityTable identities) =>
        $"{Encode(provider.Name)}:{provider.State}:{identities.Realm(provider.Realm)}:" +
        $"[{Sequence(provider.Requires.Select(identities.Key))}]:" +
        $"[{Sequence(provider.Provides.Select(identities.Key))}]";

    private static bool SameFiberIdentities(FiberSnapshot a, FiberSnapshot b)
    {
        if (!SameKeys(a.RequiredKeys, b.RequiredKeys) || !SameKeys(a.ProvidedKeys, b.ProvidedKeys) ||
            !SameRealm(a.HomeRealm, b.HomeRealm))
        {
            return false;
        }

        if (a.Routes.Count != b.Routes.Count || a.SemanticBindings.Count != b.SemanticBindings.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Routes.Count; i++)
        {
            if (!SameKey(a.Routes[i].Key, b.Routes[i].Key) ||
                !SameRealm(a.Routes[i].Realm, b.Routes[i].Realm))
            {
                return false;
            }
        }

        for (var i = 0; i < a.SemanticBindings.Count; i++)
        {
            var left = a.SemanticBindings[i];
            var right = b.SemanticBindings[i];
            if (!SameKey(left.Key, right.Key) ||
                !SameProvider(left.Provider, right.Provider))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameProvider(ProviderIdentitySnapshot a, ProviderIdentitySnapshot b) =>
        a.Name == b.Name && a.State == b.State && SameRealm(a.Realm, b.Realm) &&
        SameKeys(a.Requires, b.Requires) && SameKeys(a.Provides, b.Provides);

    private static bool SameKeys(IReadOnlyList<ICapabilityKey> a, IReadOnlyList<ICapabilityKey> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!SameKey(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Key equality is reference identity plus its semantic tuple. Two keys
    /// with the same name but different instances or value types are different
    /// capabilities (paper Def 12/§5.1); a name string is never sufficient.
    /// </summary>
    private static bool SameKey(ICapabilityKey? a, ICapabilityKey? b) =>
        ReferenceEquals(a, b) &&
        (a is null ||
         (a.GetType() == b!.GetType() && a.Name == b.Name && a.ValueType == b.ValueType));

    private static bool SameRealm(Realm? a, Realm? b) =>
        ReferenceEquals(a, b) && (a is null || a.Name == b!.Name);

    private static bool SameGlobalFailures(RuntimeSnapshot left, RuntimeSnapshot right)
    {
        var cleanupLeft = left.CleanupFailures
            .Select(record => $"{Encode(record.FiberName)}|{Encode(record.Message)}")
            .OrderBy(text => text, StringComparer.Ordinal).ToArray();
        var cleanupRight = right.CleanupFailures
            .Select(record => $"{Encode(record.FiberName)}|{Encode(record.Message)}")
            .OrderBy(text => text, StringComparer.Ordinal).ToArray();
        if (!cleanupLeft.SequenceEqual(cleanupRight, StringComparer.Ordinal))
        {
            return false;
        }

        var effectsLeft = left.EffectFailures
            .Select(record => EffectSignature(record, left))
            .OrderBy(text => text, StringComparer.Ordinal).ToArray();
        var effectsRight = right.EffectFailures
            .Select(record => EffectSignature(record, right))
            .OrderBy(text => text, StringComparer.Ordinal).ToArray();
        return effectsLeft.SequenceEqual(effectsRight, StringComparer.Ordinal);
    }

    private static string EffectSignature(EffectFailureRecord record, RuntimeSnapshot snapshot)
    {
        var fiberName = snapshot.Fibers.FirstOrDefault(fiber => fiber.Id == record.FiberId)?.Name ?? string.Empty;
        return string.Join("|",
            Encode(fiberName),
            record.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Encode(record.Label),
            record.IsInstall ? "install" : "inverse",
            Encode(record.ExceptionType),
            Encode(record.Message));
    }

    private static IEnumerable<string> EffectSignature(FiberSnapshot fiber) =>
        fiber.EffectFailures.Select(record => string.Join('|',
            record.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record.Label,
            record.IsInstall ? "install" : "inverse",
            record.ExceptionType,
            record.Message));

    /// <summary>
    /// The legacy string view is still projected so name-only comparisons keep
    /// working; provider ids are rewritten to names (or to a realm/key token
    /// when the full identity is available).
    /// </summary>
    private static IEnumerable<string> LegacyBindings(FiberSnapshot fiber)
    {
        foreach (var binding in fiber.BoundProviders)
        {
            var separator = binding.IndexOf("->", StringComparison.Ordinal);
            if (separator < 0)
            {
                yield return Encode(binding);
                continue;
            }

            var key = binding[..separator];
            var idText = binding[(separator + 2)..];
            if (Guid.TryParse(idText, out _))
            {
                var semantic = fiber.SemanticBindings.FirstOrDefault(b =>
                    b.Key.Name == key);
                yield return semantic is not null
                    ? $"{Encode(key)}->{Encode(semantic.Provider.Name)}"
                    : $"{Encode(key)}->?";
            }
            else
            {
                yield return Encode(binding);
            }
        }
    }

    private static IEnumerable<ICapabilityKey> KeysOf(FiberSnapshot fiber)
    {
        foreach (var key in fiber.RequiredKeys)
        {
            yield return key;
        }

        foreach (var key in fiber.ProvidedKeys)
        {
            yield return key;
        }

        foreach (var route in fiber.Routes)
        {
            yield return route.Key;
        }

        foreach (var binding in fiber.SemanticBindings)
        {
            yield return binding.Key;
            foreach (var key in binding.Provider.Requires)
            {
                yield return key;
            }

            foreach (var key in binding.Provider.Provides)
            {
                yield return key;
            }
        }
    }

    private static string Sequence(IEnumerable<string> values) =>
        "[" + string.Join(",", values.Select(Encode)) + "]";

    private static string Encode(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal);

    /// <summary>Reference-identity token table; tokens are stable within one projection pass.</summary>
    private sealed class IdentityTable
    {
        private readonly Dictionary<ICapabilityKey, string> _keys =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Realm, string> _realms =
            new(ReferenceEqualityComparer.Instance);

        public string Key(ICapabilityKey? key)
        {
            if (key is null)
            {
                return "K?";
            }

            if (!_keys.TryGetValue(key, out var token))
            {
                token = "K" + _keys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _keys[key] = token;
            }

            return token;
        }

        public string Realm(Realm? realm)
        {
            if (realm is null)
            {
                return "R?";
            }

            if (!_realms.TryGetValue(realm, out var token))
            {
                token = "R" + _realms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _realms[realm] = token;
            }

            return token;
        }
    }
}
