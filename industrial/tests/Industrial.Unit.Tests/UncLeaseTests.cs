using Industrial.Core;

namespace Industrial.Unit.Tests;

/// <summary>Lease/refcount logic is tested without Windows by substituting the
/// native SMB call. Only WindowsNetworkConnection touches mpr.dll.</summary>
public sealed class UncLeaseTests : IDisposable
{
    public UncLeaseTests() => UncConnectionLease.ReleaseAll();

    public void Dispose() => UncConnectionLease.ReleaseAll();

    [Fact]
    public async Task TwoConsumersShareOneSessionUntilBothRelease()
    {
        var native = new FakeNetworkConnection();
        var first = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        var second = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);

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
            UncConnectionLease.Acquire(@"\\server\share", "user", "bad", null, native)));
        Assert.Equal(0, native.Cancels);
    }

    [Fact]
    public async Task ReleasedSessionIsRemovedSoReacquireCreatesNewNativeSession()
    {
        var native = new FakeNetworkConnection();
        var first = UncConnectionLease.Acquire(
            @"\\SERVER\share\", "user", "secret", null, native);
        await first.DisposeAsync();

        var second = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        Assert.NotSame(first, second);
        Assert.Equal(2, native.Adds);
        Assert.Equal(1, native.Cancels);
        await second.DisposeAsync();
        Assert.Equal(2, native.Cancels);
    }

    [Fact]
    public async Task DoubleDisposeKeepsReferenceCountAndSessionCleanupIdempotent()
    {
        var native = new FakeNetworkConnection();
        var lease = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(1, native.Cancels);
        var again = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        Assert.NotSame(lease, again);
        Assert.Equal(2, native.Adds);
        await again.DisposeAsync();
    }

    [Fact]
    public void DifferentCredentialsForTheSameShareNeverReuseOneSession()
    {
        var native = new FakeNetworkConnection();
        var first = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret-a", null, native);
        var second = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret-b", null, native);

        Assert.NotSame(first, second);
        Assert.Equal(2, native.Adds);
    }

    [Fact]
    public async Task EquivalentShareSpellingsAndAccountsShareOneSession()
    {
        var native = new FakeNetworkConnection();
        var first = UncConnectionLease.Acquire(
            @"\\SERVER\share\", "User", "secret", null, native);
        var second = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);

        Assert.Same(first, second);
        Assert.Equal(1, native.Adds);

        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(1, native.Cancels);
    }

    [Fact]
    public void IncompleteUncPathIsRejected()
    {
        var native = new FakeNetworkConnection();
        Assert.Throws<ArgumentException>(() => UncConnectionLease.Acquire(
            @"\\server", "user", "secret", null, native));
        Assert.Equal(0, native.Adds);
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
