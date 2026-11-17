using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class CancellableOperationRunnerTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task RunAsync_WhenOperationFinishes_ShouldReturnCompleted()
    {
        using var cts = new CancellationTokenSource();
        bool ran = false;

        var outcome = await CancellableOperationRunner.RunAsync(async _ =>
        {
            await Task.Yield();
            ran = true;
        }, cts, Grace);

        outcome.ShouldBe(OperationOutcome.Completed);
        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenOperationFails_ShouldRethrowItsException()
    {
        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            CancellableOperationRunner.RunAsync(_ => throw new InvalidOperationException("boom"), cts, Grace));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "run is awaited before cts is disposed")]
    public async Task RunAsync_WhenCancelled_ShouldNotReturnUntilOperationHasStopped()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stillRunningAfterCancel = false;
        bool finished = false;
        int cancellingCalls = 0;

        var run = CancellableOperationRunner.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskScheduler.Default);
            // Cleanup after observing cancellation takes a while; the runner must wait for it.
            stillRunningAfterCancel = true;
            await Task.Delay(200, CancellationToken.None);
            finished = true;
            token.ThrowIfCancellationRequested();
        }, cts, Grace, () => cancellingCalls++);

        await started.Task;
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
        stillRunningAfterCancel.ShouldBeTrue();
        finished.ShouldBeTrue("the runner returned before the cancelled operation finished");
        cancellingCalls.ShouldBe(1);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership passes to the runner when the operation is abandoned")]
    public async Task RunAsync_WhenOperationIgnoresCancellation_ShouldAbandonItAfterGracePeriod()
    {
        var cts = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = CancellableOperationRunner.RunAsync(async _ =>
        {
            started.SetResult();
            await release.Task; // ignores the token, like a hung driver
        }, cts, TimeSpan.FromMilliseconds(200));

        await started.Task;
        await cts.CancelAsync();

        (await run).ShouldBe(OperationOutcome.Abandoned);

        // The abandoned operation still holds the token, so the runner must not have disposed the CTS yet.
        Should.NotThrow(() => _ = cts.Token.WaitHandle);

        release.SetResult();
    }

    [Fact]
    public async Task RunAsync_WhenTimeoutElapses_ShouldBehaveLikeCancel()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CancellableOperationRunner.RunAsync(token => Task.Delay(Timeout.Infinite, token), cts, Grace));
    }
}
