using CefSharp;
using CefSharp.OffScreen;

namespace TechBrowser.Gdi;

internal static class Program
{
    private static readonly object LogLock = new();
    private static readonly string LogDirectory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName
                                                  ?? AppContext.BaseDirectory;
    private static readonly string ApplicationLogPath = Path.Combine(LogDirectory, "Tech-Browser.log");
    private static readonly string CefLogPath = Path.Combine(LogDirectory, "Tech-Browser-cef.log");

    // Second smoke-test page; the first page links to it with target=_blank.
    internal static readonly string SmokePopupUrl = "data:text/html," + Uri.EscapeDataString(
        "<title>SMOKE-POPUP</title><body style='background:white'><h1 style='color:#123456'>POPUP OK</h1><select><option>A</option><option>B</option></select>");

    [STAThread]
    private static void Main(string[] args)
    {
        var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        try
        {
            // Each launch starts a fresh log so nothing from earlier sessions stays on the endpoint.
            foreach (var oldLog in new[] { ApplicationLogPath, CefLogPath })
            {
                try { File.Delete(oldLog); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            WriteLog($"Start; Version={typeof(Program).Assembly.GetName().Version}; OS={Environment.OSVersion}; Base={AppContext.BaseDirectory}");
            Application.ThreadException += (_, eventArgs) => WriteLog("UI exception", eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                WriteLog("Unhandled exception", eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));

            // Backstage runs on an unthemed desktop; these values explain black controls in the field.
            WriteLog($"Display; HighContrast={SystemInformation.HighContrast}; Control={SystemColors.Control.ToArgb():X8}; ControlText={SystemColors.ControlText.ToArgb():X8}; Window={SystemColors.Window.ToArgb():X8}; BitsPerPixel={Screen.PrimaryScreen?.BitsPerPixel}");

            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = BrowserForm.SelfTest() ? 0 : 1;
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                MultiThreadedMessageLoop = true,
                LogSeverity = LogSeverity.Warning,
                LogFile = CefLogPath,
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
                Environment.ExitCode = 1;
                // A message box would wait for a click that never comes in an unattended smoke test.
                if (!smokeTest)
                {
                    MessageBox.Show(
                        $"Chromium konnte nicht initialisiert werden.\n\nLog: {ApplicationLogPath}",
                        "Tech Browser",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return;
            }

            try
            {
                var startUrl = smokeTest
                    ? "data:text/html," + Uri.EscapeDataString(
                        "<body style='background:white;margin:0'><a target='_blank' style='display:block;width:400px;height:200px;color:#123456' href='" +
                        SmokePopupUrl + "'>GDI OK</a>")
                    : FindStartUrl(args);

                WriteLog($"Opening browser; SmokeTest={smokeTest}; StartUrl={Redact(startUrl)}");
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
            if (!smokeTest)
            {
                MessageBox.Show(
                    $"Tech Browser konnte nicht gestartet werden.\n\n{exception.Message}\n\nLog: {ApplicationLogPath}",
                    "Tech Browser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            Environment.ExitCode = 1;
        }
    }

    // Logs must not keep visited addresses: only scheme and host are written.
    internal static string Redact(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "(no address)";
        return uri.Host.Length > 0 ? $"{uri.Scheme}://{uri.Authority}" : $"{uri.Scheme}:";
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
