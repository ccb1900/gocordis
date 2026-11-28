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

    /// <summary>Holds Cancel open to make the audit race deterministic: the old
    /// owner has dropped its dictionary entry in the buggy implementation, but
    /// its native Cancel has not disconnected the SMB session yet.</summary>
    [Fact]
    public async Task ConcurrentAcquireCannotReuseASessionWhoseCancelIsInFlight()
    {
        var native = new FakeNetworkConnection
        {
            CancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var first = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        var second = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        Assert.Same(first, second);

        // First owner only drops one reference; no Cancel yet (refcount intact).
        await Task.Run(async () => await first.DisposeAsync());
        Assert.Equal("Add", native.CallSequence);

        // Last owner transitions the session to Releasing and blocks inside Cancel.
        var dispose = Task.Run(async () => await second.DisposeAsync());
        await native.CancelEntered!.Task;

        var acquired = Task.Run(() => UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native));
        Assert.False(acquired.IsCompleted);

        native.ReleaseCancel!.SetResult();
        var replacement = await acquired;
        await dispose;

        Assert.NotSame(second, replacement);
        // The new Add must follow the completed Cancel of the old session;
        // Add|Cancel:start|Add|Cancel:end is the race this test guards against.
        Assert.Equal("Add | Cancel:start | Cancel:end | Add", native.CallSequence);
        Assert.Equal(2, native.Adds);
        Assert.Equal(1, native.Cancels);

        await replacement.DisposeAsync();
        Assert.Equal("Add | Cancel:start | Cancel:end | Add | Cancel:start | Cancel:end",
            native.CallSequence);
    }

    [Fact]
    public async Task DisposeDuringReleaseWindowDoesNotCancelTwice()
    {
        var native = new FakeNetworkConnection
        {
            CancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var lease = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);

        var dispose = Task.Run(async () => await lease.DisposeAsync());
        await native.CancelEntered!.Task;

        // A late second owner must observe Releasing and not call Cancel again.
        await lease.DisposeAsync();
        Assert.Equal("Add | Cancel:start", native.CallSequence);

        native.ReleaseCancel!.SetResult();
        await dispose;
        Assert.Equal("Add | Cancel:start | Cancel:end", native.CallSequence);
        Assert.Equal(1, native.Cancels);

        var replacement = UncConnectionLease.Acquire(
            @"\\server\share", "user", "secret", null, native);
        Assert.NotSame(lease, replacement);
        await replacement.DisposeAsync();
    }

    private sealed class FakeNetworkConnection : INativeNetworkConnection
    {
        private readonly object _gate = new();
        private readonly List<string> _calls = [];

        public int AddResult { get; set; }
        public int Adds { get; private set; }
        public int Cancels { get; private set; }
        public TaskCompletionSource? CancelEntered { get; set; }
        public TaskCompletionSource? ReleaseCancel { get; set; }

        public string CallSequence
        {
            get { lock (_gate) return string.Join(" | ", _calls); }
        }

        public int Add(string remote, string userName, string password)
        {
            lock (_gate)
            {
                Adds++;
                _calls.Add("Add");
            }
            return AddResult;
        }

        public int Cancel(string remote)
        {
            lock (_gate)
            {
                Cancels++;
                _calls.Add("Cancel:start");
            }
            CancelEntered?.TrySetResult();
            ReleaseCancel?.Task.GetAwaiter().GetResult();
            lock (_gate) _calls.Add("Cancel:end");
            return 0;
        }
    }
}
