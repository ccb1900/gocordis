namespace Gocordis.Runtime.Tests;

/// <summary>
/// Collectible AssemblyLoadContext reclamation is GC-timing sensitive when
/// xUnit runs other plugin tests in parallel. Force a blocking compacting
/// collection instead of relying on opportunistic gen2 collections.
/// </summary>
internal static class GcAssert
{
    internal static bool Reclaimed(WeakReference reference, int attempts = 20)
    {
        for (var attempt = 0; attempt < attempts && reference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            Thread.Sleep(5);
        }

        return !reference.IsAlive;
    }
}
