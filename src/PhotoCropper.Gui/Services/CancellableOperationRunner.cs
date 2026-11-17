namespace PhotoCropper.Gui.Services;

internal enum OperationOutcome
{
    /// <summary>The operation finished (successfully; failures are rethrown instead).</summary>
    Completed,

    /// <summary>The operation ignored cancellation for the whole grace period and was left running.</summary>
    Abandoned
}

/// <summary>
/// Runs a cancellable operation so that cancelling waits for it to actually stop, instead of returning
/// while it is still running. Kept free of UI types so the behaviour can be unit-tested.
/// </summary>
internal static class CancellableOperationRunner
{
    /// <summary>
    /// Runs <paramref name="action"/> with <paramref name="cts"/>'s token.
    /// </summary>
    /// <param name="onCancelling">Called once when cancellation is requested while the operation is still running.</param>
    /// <returns>
    /// <see cref="OperationOutcome.Completed"/> when the operation finished, or <see cref="OperationOutcome.Abandoned"/>
    /// when it was still running <paramref name="gracePeriod"/> after cancellation. An abandoned operation keeps
    /// using the token, so ownership of <paramref name="cts"/> passes to the runner, which disposes it when the
    /// operation finally ends; otherwise the caller still owns it.
    /// </returns>
    /// <exception cref="OperationCanceledException">The operation stopped because it was cancelled.</exception>
    /// <remarks>Any other exception thrown by the operation is rethrown as is.</remarks>
    public static async Task<OperationOutcome> RunAsync(
        Func<CancellationToken, Task> action,
        CancellationTokenSource cts,
        TimeSpan gracePeriod,
        Action? onCancelling = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(cts);

        Task actionTask = action(cts.Token);

        var cancelSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cts.Token.Register(() => cancelSignal.TrySetResult()))
        {
            await Task.WhenAny(actionTask, cancelSignal.Task);
        }

        if (!actionTask.IsCompleted)
        {
            onCancelling?.Invoke();

            try
            {
                await actionTask.WaitAsync(gracePeriod);
            }
            catch (TimeoutException)
            {
                // Still running; handled below.
            }
            catch (Exception)
            {
                // The operation stopped (by cancelling or failing); its outcome is rethrown below.
            }

            if (!actionTask.IsCompleted)
            {
                _ = actionTask.ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Abandoned operation failed after cancellation: {t.Exception.GetBaseException().Message}");
                    }
                    cts.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return OperationOutcome.Abandoned;
            }
        }

        await actionTask;
        return OperationOutcome.Completed;
    }
}
