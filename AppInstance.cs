using System.Threading;

namespace AppFlow;

internal static class AppInstance
{
    private const string MutexName = @"Local\AppFlow.MainInstance.4F4DA0A5";
    private const string ActivateEventName = @"Local\AppFlow.ActivateMainWindow.4F4DA0A5";
    private static EventWaitHandle? activateEvent;
    private static CancellationTokenSource? listenerCancellation;

    public static string InstanceMutexName => MutexName;

    public static void RequestActivation()
    {
        // The primary instance may still be creating the event during startup.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    public static void ListenForActivation(Action activate)
    {
        activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        listenerCancellation = new CancellationTokenSource();
        var signal = activateEvent;
        var token = listenerCancellation.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (signal.WaitOne(500))
                    {
                        try { activate(); }
                        catch (InvalidOperationException) { return; }
                    }
                }
                catch (ObjectDisposedException) { return; }
            }
        });
    }

    public static void StopListening()
    {
        listenerCancellation?.Cancel();
        listenerCancellation?.Dispose();
        listenerCancellation = null;
        activateEvent?.Dispose();
        activateEvent = null;
    }
}
