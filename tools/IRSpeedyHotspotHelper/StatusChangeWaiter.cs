namespace IRSpeedy.Hotspot;

// Kept independent of WinRT so synchronous callbacks, races and timeouts can be tested.
internal static class StatusChangeWaiter
{
    internal static async Task WaitAsync<T>(Func<Action<T>, Action> subscribe,
        Action request, Func<T> readStatus, Func<T, bool> succeeded,
        Func<T, Exception?> failure, TimeSpan timeout, Func<Exception> timedOut)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(T state)
        {
            try
            {
                if (succeeded(state)) completion.TrySetResult(true);
                else if (failure(state) is Exception error) completion.TrySetException(error);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }

        // Subscribe BEFORE requesting the transition: Start/Stop may raise an event inline.
        Action unsubscribe = subscribe(Observe);
        try
        {
            request();
            // Also cover implementations that change status before delivering their event.
            if (!completion.Task.IsCompleted) Observe(readStatus());
            try { await completion.Task.WaitAsync(timeout).ConfigureAwait(false); }
            catch (TimeoutException) { throw timedOut(); }
        }
        finally
        {
            unsubscribe();
            // An event racing the deadline may fault after WaitAsync has timed out.
            // Observe that exception without letting it escape an event callback.
            _ = completion.Task.ContinueWith(task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
