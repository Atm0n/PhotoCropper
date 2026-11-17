using Avalonia;

namespace PhotoCropper.Gui
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Last-resort handlers: record the failure before the process goes down. Exceptions on the UI
            // thread (e.g. from async void event handlers) are handled separately in App, which keeps the
            // app running so unsaved work isn't lost.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Services.CrashLogger.WriteCrashLog(
                    e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()),
                    e.IsTerminating ? "Unhandled exception (terminating)" : "Unhandled exception");

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Services.CrashLogger.WriteCrashLog(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
