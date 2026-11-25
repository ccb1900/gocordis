using Industrial.Core;

namespace Industrial.Unit.Tests;

/// <summary>Lease/refcount logic is tested without Windows by substituting the
/// native SMB call. Only WindowsNetworkConnection touches mpr.dll.</summary>
public sealed class UncLeaseTests
{
    [Fact]
    public async Task TwoConsumersShareOneSessionUntilBothRelease()
    {
        var native = new FakeNetworkConnection();
        var first = UncConnectionLease.Acquire(
            "share-1", @"\\server\share", "user", "secret", null, native);
        var second = UncConnectionLease.Acquire(
            "share-1", @"\\server\share", "user", "secret", null, native);

        Assert.Same(first, second);
        Assert.Equal(1, native.Adds);

        await first.DisposeAsync();
        Assert.Equal(0, native.Cancels);
        await second.DisposeAsync();
        Assert.Equal(1, native.Cancels);
    }

    [Fact]
    public async Task CredentialConflictIsReportedExplicitly()
    {
        var native = new FakeNetworkConnection { AddResult = 1219 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            UncConnectionLease.Acquire("share-2", @"\\server\share", "user", "bad", null, native)));
        Assert.Equal(0, native.Cancels);
    }

    private sealed class FakeNetworkConnection : INativeNetworkConnection
    {
        public int AddResult { get; set; }
        public int Adds { get; private set; }
        public int Cancels { get; private set; }

        public int Add(string remote, string userName, string password)
        {
            Adds++;
            return AddResult;
        }

        public int Cancel(string remote)
        {
            Cancels++;
            return 0;
        }
    }
}
