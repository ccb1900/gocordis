using Gocordis.Core;

namespace Gocordis.Extensions;

public static class EnvKeys
{
    public static readonly Key<IEnvService> Service = new("gocordis.extension.env");
}

public interface IEnvService : IAsyncDisposable
{
    void Apply(IReadOnlyDictionary<string, string> values, bool processWins = true);
}
