using CefSharp;
using CefSharp.OffScreen;

namespace TechBrowser.Gdi;

internal sealed class BrowserForm : Form
{
    private static readonly Color ToolbarColor = Color.FromArgb(246, 248, 250);
    private readonly ChromiumWebBrowser browser;
    private readonly BrowserSurface surface;
    private readonly TextBox addressBox;
    private readonly Button backButton;
    private readonly Button forwardButton;
    private readonly Button reloadButton;
    private readonly Button goButton;
    private readonly Label statusLabel;
    private readonly bool smokeTest;
    private int smokeTestCompleted;
    private volatile bool smokePopupLoaded;
    private volatile bool smokeFirstPageLoaded;
    private readonly float browserScale;
    private int capturePending;

    public BrowserForm(string startUrl, bool smokeTest)
    {
        this.smokeTest = smokeTest;

        Text = "Tech Browser GDI";
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(640, 420);
        BackColor = Color.White;

        backButton = CreateButton("←", "Zurück");
        forwardButton = CreateButton("→", "Vorwärts");
        reloadButton = CreateButton("↻", "Neu laden");
        var homeButton = CreateButton("⌂", "Leere Seite");
        goButton = CreateButton("Los", "Adresse öffnen");
        goButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        addressBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F),
            Margin = new Padding(5, 8, 5, 7),
            BackColor = Color.White,
            ForeColor = Color.Black,
            PlaceholderText = "Adresse oder Suchbegriff"
        };

        statusLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F),
            Margin = new Padding(5, 0, 8, 0)
        };

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = ToolbarColor,
            Padding = new Padding(6),
            ColumnCount = 7,
            RowCount = 1
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        toolbar.Controls.Add(backButton, 0, 0);
        toolbar.Controls.Add(forwardButton, 1, 0);
        toolbar.Controls.Add(reloadButton, 2, 0);
        toolbar.Controls.Add(homeButton, 3, 0);
        toolbar.Controls.Add(addressBox, 4, 0);
        toolbar.Controls.Add(goButton, 5, 0);
        toolbar.Controls.Add(statusLabel, 6, 0);

        surface = new BrowserSurface
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            TabStop = true
        };

        Controls.Add(surface);
        Controls.Add(toolbar);

        browser = new ChromiumWebBrowser(
            startUrl,
            browserSettings: null,
            requestContext: null,
            automaticallyCreateBrowser: false,
            onAfterBrowserCreated: null,
            useLegacyRenderHandler: true);

        // Render at the Windows display scale so pages are not tiny on high-resolution screens.
        browserScale = DeviceDpi / 96F;
        browser.DeviceScaleFactor = browserScale;
        surface.BrowserScale = browserScale;
        browser.LifeSpanHandler = new SameWindowLifeSpanHandler();
        browser.RequestHandler = new PromptingRequestHandler(this);

        browser.BrowserInitialized += BrowserInitialized;
        browser.Paint += BrowserPaint;
        browser.AddressChanged += BrowserAddressChanged;
        browser.TitleChanged += BrowserTitleChanged;
        browser.LoadingStateChanged += BrowserLoadingStateChanged;
        browser.LoadError += BrowserLoadError;
        browser.ConsoleMessage += (_, eventArgs) =>
        {
            if (eventArgs.Level == LogSeverity.Error)
            {
                Program.WriteLog($"Console error at {Program.Redact(eventArgs.Source)}:{eventArgs.Line}");
            }
        };

        surface.BrowserHostProvider = GetBrowserHost;
        surface.SizeChanged += (_, _) => ResizeBrowser();

        backButton.Click += (_, _) => browser.Back();
        forwardButton.Click += (_, _) => browser.Forward();
        reloadButton.Click += (_, _) =>
        {
            if (browser.IsLoading) browser.Stop();
            else browser.Reload();
        };
        homeButton.Click += (_, _) => Navigate("about:blank");
        goButton.Click += (_, _) => NavigateFromAddressBox();
        addressBox.KeyDown += AddressBoxKeyDown;
        addressBox.Enter += (_, _) => addressBox.SelectAll();

        Load += (_, _) =>
        {
            browser.CreateBrowser();
            if (!smokeTest) return;
            BeginInvoke(Hide);
            // Chromium ignores input until the new page has produced a frame, so the click is
            // repeated until the second page loads. A test that never finishes must fail
            // instead of blocking the build.
            var ticks = 0;
            var smokeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            smokeTimer.Tick += (_, _) =>
            {
                if (smokeTestCompleted != 0)
                {
                    smokeTimer.Stop();
                    return;
                }
                if (++ticks > 60)
                {
                    smokeTimer.Stop();
                    Program.WriteLog("Smoke test timed out");
                    Environment.ExitCode = 2;
                    Close();
                    return;
                }

                var host = GetBrowserHost();
                if (host is null) return;
                if (smokePopupLoaded)
                {
                    host.Invalidate(PaintElementType.View);
                }
                else if (smokeFirstPageLoaded)
                {
                    var click = new CefSharp.MouseEvent(50, 50, CefEventFlags.None);
                    host.SendMouseMoveEvent(click, mouseLeave: false);
                    host.SendMouseClickEvent(click, MouseButtonType.Left, mouseUp: false, clickCount: 1);
                    host.SendMouseClickEvent(click, MouseButtonType.Left, mouseUp: true, clickCount: 1);
                }
            };
            smokeTimer.Start();
        };
        FormClosed += (_, _) => browser.Dispose();
    }

    internal static Button CreateButton(string text, string? tooltip)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Symbol", 15F, FontStyle.Bold),
            Margin = new Padding(2),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        // Paint the button ourselves: themed and high-contrast rendering comes out black in Backstage.
        button.Paint += (_, eventArgs) =>
        {
            eventArgs.Graphics.Clear(ToolbarColor);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                button.Enabled ? Color.Black : Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        if (tooltip is not null) new ToolTip().SetToolTip(button, tooltip);
        return button;
    }

    private IBrowserHost? GetBrowserHost()
    {
        return browser.IsBrowserInitialized ? browser.GetBrowserHost() : null;
    }

    private void BrowserInitialized(object? sender, EventArgs eventArgs)
    {
        Program.WriteLog("Browser initialized");
        ResizeBrowser();
    }

    private void ResizeBrowser()
    {
        if (!browser.IsBrowserInitialized || surface.ClientSize.Width < 1 || surface.ClientSize.Height < 1) return;
        browser.Size = new Size((int)(surface.ClientSize.Width / browserScale), (int)(surface.ClientSize.Height / browserScale));
    }

    private void BrowserPaint(object? sender, OnPaintEventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref capturePending, 1) != 0 || surface.IsDisposed || !surface.IsHandleCreated) return;
        surface.BeginInvoke(CaptureFrame);
    }

    private void CaptureFrame()
    {
        // Cleared before the copy: a repaint that arrives while copying must schedule another capture.
        Interlocked.Exchange(ref capturePending, 0);

        using var screenshot = browser.ScreenshotOrNull(PopupBlending.Blend);
        if (screenshot is null) return;

        // Flatten onto white in memory so no alpha reaches the screen; Backstage shows it as black.
        var frame = new Bitmap(screenshot.Width, screenshot.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(screenshot, 0, 0);
        }

        if (smokeTest && smokePopupLoaded && ShowsSmokePopupPage(frame) && Interlocked.Exchange(ref smokeTestCompleted, 1) == 0)
        {
            var smokeImage = Environment.GetEnvironmentVariable("TECH_BROWSER_SMOKE_IMAGE");
            if (!string.IsNullOrWhiteSpace(smokeImage))
            {
                frame.Save(smokeImage, System.Drawing.Imaging.ImageFormat.Png);
            }
            BeginInvoke(Close);
        }
        surface.SetFrame(frame);
    }

    // Only the second smoke-test page paints a green block, so a blank frame or a frame
    // of the first page cannot pass.
    private static bool ShowsSmokePopupPage(Bitmap frame)
    {
        for (var y = 0; y < frame.Height; y += 4)
        {
            for (var x = 0; x < frame.Width; x += 4)
            {
                var pixel = frame.GetPixel(x, y);
                if (pixel.G > pixel.R + 60 && pixel.G > pixel.B + 60) return true;
            }
        }
        return false;
    }

    private void BrowserAddressChanged(object? sender, AddressChangedEventArgs eventArgs)
    {
        Program.WriteLog($"Address changed: {Program.Redact(eventArgs.Address)}");
        UpdateUi(() => addressBox.Text = eventArgs.Address);
    }

    private void BrowserTitleChanged(object? sender, TitleChangedEventArgs eventArgs)
    {
        UpdateUi(() => Text = string.IsNullOrWhiteSpace(eventArgs.Title) ? "Tech Browser GDI" : $"{eventArgs.Title} – Tech Browser GDI");
    }

    private void BrowserLoadingStateChanged(object? sender, LoadingStateChangedEventArgs eventArgs)
    {
        Program.WriteLog($"Loading state: IsLoading={eventArgs.IsLoading}; Back={eventArgs.CanGoBack}; Forward={eventArgs.CanGoForward}");
        if (smokeTest && !eventArgs.IsLoading) AdvanceSmokeTest(eventArgs.Browser);
        UpdateUi(() =>
        {
            backButton.Enabled = eventArgs.CanGoBack;
            forwardButton.Enabled = eventArgs.CanGoForward;
            reloadButton.Text = eventArgs.IsLoading ? "×" : "↻";
            statusLabel.Text = eventArgs.IsLoading ? "Lädt …" : string.Empty;
        });
    }

    // Smoke test: the timer clicks the "new window" link on the first page and waits for the second.
    // This exercises mouse input, popup redirection and rendering in the packaged build.
    private void AdvanceSmokeTest(IBrowser smokeBrowser)
    {
        if (smokeBrowser.MainFrame.Url == Program.SmokePopupUrl) smokePopupLoaded = true;
        else smokeFirstPageLoaded = true;
    }

    private void BrowserLoadError(object? sender, LoadErrorEventArgs eventArgs)
    {
        if (eventArgs.ErrorCode == CefErrorCode.Aborted) return;
        Program.WriteLog($"Load error: Url={Program.Redact(eventArgs.FailedUrl)}; Code={eventArgs.ErrorCode}; Text={eventArgs.ErrorText}");
        UpdateUi(() => statusLabel.Text = $"Fehler: {eventArgs.ErrorText}");
    }

    private void AddressBoxKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.SuppressKeyPress = true;
        NavigateFromAddressBox();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // The page receives Tab and arrow keys, so the address bar needs its own shortcut.
        if (keyData is (Keys.Control | Keys.L) or Keys.F6)
        {
            addressBox.Focus();
            return true;
        }
        if (addressBox.Focused && keyData == Keys.Enter)
        {
            NavigateFromAddressBox();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void NavigateFromAddressBox()
    {
        Navigate(NormalizeAddress(addressBox.Text));
        surface.Focus();
    }

    private void Navigate(string address)
    {
        Program.WriteLog($"Navigate requested: {Program.Redact(address)}");
        statusLabel.Text = "Öffne …";
        browser.LoadUrl(address);
    }

    private void UpdateUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }

    internal static string NormalizeAddress(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (value.Length == 0 || value == "about:blank") return "about:blank";
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp ||
             absolute.Scheme == Uri.UriSchemeHttps ||
             absolute.Scheme == Uri.UriSchemeFile))
        {
            return absolute.ToString();
        }

        if (LooksLikeLocalAddress(value))
        {
            return $"http://{value}";
        }

        if (value.Contains('.') && !value.Contains(' ')) return $"https://{value}";
        // A single word is an internal host name such as "nas" or "printer01:8080", not a search.
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9-]+(:\d+)?(/.*)?$")) return $"http://{value}";
        return $"https://www.google.com/search?q={Uri.EscapeDataString(value)}";
    }

    // Run by the package verification through --self-test; failures are written to the log.
    internal static bool SelfTest()
    {
        (string Input, string Expected)[] cases =
        [
            ("https://example.com/a", "https://example.com/a"),
            ("192.168.1.1", "http://192.168.1.1"),
            ("localhost:8080/status", "http://localhost:8080/status"),
            ("example.com/path", "https://example.com/path"),
            ("printer01", "http://printer01"),
            ("nas:8080", "http://nas:8080"),
            ("about:blank", "about:blank"),
            ("", "about:blank"),
            ("drucker firmware", "https://www.google.com/search?q=drucker%20firmware")
        ];

        var passed = true;
        foreach (var (input, expected) in cases)
        {
            var actual = NormalizeAddress(input);
            if (actual == expected) continue;
            Program.WriteLog($"Self-test failed: '{input}' gave '{actual}', expected '{expected}'");
            passed = false;
        }
        return passed;
    }

    private static bool LooksLikeLocalAddress(string value)
    {
        if (value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || value.StartsWith('['))
        {
            return true;
        }

        var endOfHost = value.IndexOfAny(['/', ':', '?', '#']);
        var host = endOfHost < 0 ? value : value[..endOfHost];
        if (host.Length < 7 || host.Any(character => character != '.' && !char.IsAsciiDigit(character)))
        {
            return false;
        }

        var parts = host.Split('.');
        return parts.Length == 4 && parts.All(part =>
            byte.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _));
    }
}
