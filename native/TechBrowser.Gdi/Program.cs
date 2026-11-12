using CefSharp;
using CefSharp.OffScreen;

namespace TechBrowser.Gdi;

internal static class Program
{
    private static readonly object LogLock = new();
    private static readonly string LogDirectory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName
                                                  ?? AppContext.BaseDirectory;
    private static readonly string ApplicationLogPath = Path.Combine(LogDirectory, "Tech-Browser.log");

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            WriteLog($"Start; Version={typeof(Program).Assembly.GetName().Version}; OS={Environment.OSVersion}; Base={AppContext.BaseDirectory}");
            Application.ThreadException += (_, eventArgs) => WriteLog("UI exception", eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                WriteLog("Unhandled exception", eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));

            // Backstage runs on an unthemed desktop; these values explain black controls in the field.
            WriteLog($"Display; HighContrast={SystemInformation.HighContrast}; Control={SystemColors.Control.ToArgb():X8}; ControlText={SystemColors.ControlText.ToArgb():X8}; Window={SystemColors.Window.ToArgb():X8}; BitsPerPixel={Screen.PrimaryScreen?.BitsPerPixel}");

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                MultiThreadedMessageLoop = true,
                LogSeverity = LogSeverity.Warning,
                LogFile = Path.Combine(LogDirectory, "Tech-Browser-cef.log"),
                PersistSessionCookies = false,
                // Opaque white: a transparent offscreen buffer shows as black where a page paints nothing.
                BackgroundColor = Cef.ColorSetARGB(255, 255, 255, 255)
            };

            // High contrast on the Backstage desktop makes Chromium repaint buttons, menus and icons in system colours.
            settings.CefCommandLineArgs["disable-features"] = "ForcedColors";
            settings.CefCommandLineArgs["disable-blink-features"] = "ForcedColors";

            settings.CefCommandLineArgs["disable-gpu"] = "1";
            settings.CefCommandLineArgs["disable-gpu-compositing"] = "1";
            settings.CefCommandLineArgs["disable-gpu-rasterization"] = "1";
            settings.CefCommandLineArgs["disable-direct-composition"] = "1";
            settings.CefCommandLineArgs["disable-backgrounding-occluded-windows"] = "1";
            settings.CefCommandLineArgs["disable-renderer-backgrounding"] = "1";

            WriteLog("Initializing CEF");
            if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
            {
                WriteLog("CEF initialization returned false");
                MessageBox.Show(
                    $"Chromium konnte nicht initialisiert werden.\n\nLog: {ApplicationLogPath}",
                    "Tech Browser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
                var startUrl = smokeTest
                    ? "data:text/html,<html><body style='background:white;color:%23123456'><h1>GDI OK</h1></body></html>"
                    : FindStartUrl(args);

                WriteLog($"Opening browser; SmokeTest={smokeTest}; StartUrl={startUrl}");
                Application.Run(new BrowserForm(startUrl, smokeTest));
            }
            finally
            {
                Cef.Shutdown();
                WriteLog("CEF shutdown complete");
            }
        }
        catch (Exception exception)
        {
            WriteLog("Fatal startup exception", exception);
            MessageBox.Show(
                $"Tech Browser konnte nicht gestartet werden.\n\n{exception.Message}\n\nLog: {ApplicationLogPath}",
                "Tech Browser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    internal static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    ApplicationLogPath,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}" +
                    (exception is null ? string.Empty : exception + Environment.NewLine));
            }
        }
        catch
        {
            // Logging must never prevent the diagnostic browser from starting.
        }
    }

    private static string FindStartUrl(IEnumerable<string> args)
    {
        return args.FirstOrDefault(value =>
                   Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
               ?? "about:blank";
    }
}
