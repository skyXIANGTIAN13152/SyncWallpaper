using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace SyncWallpaper.Tests;

internal static class WpfTestThread
{
    public static void Run(Action test, int timeoutSeconds = 20)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try
                {
                    // WPF also owns hidden HWNDs. Close their dispatcher on its
                    // owning STA before native thread teardown can call back into WPF.
                    if (!dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown();
                    Assert.IsTrue(dispatcher.HasShutdownFinished, "The WPF dispatcher must finish shutting down before its test thread exits.");
                }
                catch (Exception ex)
                {
                    failure = failure is null ? ex : new AggregateException(failure, ex);
                }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(timeoutSeconds)), "WPF test thread did not complete and shut down.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
