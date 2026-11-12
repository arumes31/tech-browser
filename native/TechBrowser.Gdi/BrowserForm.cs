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

        browser.BrowserInitialized += BrowserInitialized;
        browser.Paint += BrowserPaint;
        browser.AddressChanged += BrowserAddressChanged;
        browser.TitleChanged += BrowserTitleChanged;
        browser.LoadingStateChanged += BrowserLoadingStateChanged;
        browser.LoadError += BrowserLoadError;
        browser.ConsoleMessage += (_, eventArgs) =>
            Program.WriteLog($"Console [{eventArgs.Level}] {eventArgs.Source}:{eventArgs.Line} {eventArgs.Message}");

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
            if (smokeTest) BeginInvoke(Hide);
        };
        FormClosed += (_, _) => browser.Dispose();
    }

    private static Button CreateButton(string text, string tooltip)
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
        new ToolTip().SetToolTip(button, tooltip);
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
        browser.Size = surface.ClientSize;
    }

    private void BrowserPaint(object? sender, OnPaintEventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref capturePending, 1) != 0 || surface.IsDisposed || !surface.IsHandleCreated) return;
        surface.BeginInvoke(CaptureFrame);
    }

    private void CaptureFrame()
    {
        try
        {
            using var screenshot = browser.ScreenshotOrNull(PopupBlending.Blend);
            if (screenshot is null) return;

            // Flatten onto white in memory so no alpha reaches the screen; Backstage shows it as black.
            var frame = new Bitmap(screenshot.Width, screenshot.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.White);
                graphics.DrawImageUnscaled(screenshot, 0, 0);
            }

            if (smokeTest)
            {
                var smokeImage = Environment.GetEnvironmentVariable("TECH_BROWSER_SMOKE_IMAGE");
                if (!string.IsNullOrWhiteSpace(smokeImage))
                {
                    frame.Save(smokeImage, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            surface.SetFrame(frame);

            if (smokeTest && Interlocked.Exchange(ref smokeTestCompleted, 1) == 0)
            {
                BeginInvoke(Close);
            }
        }
        finally
        {
            Interlocked.Exchange(ref capturePending, 0);
        }
    }

    private void BrowserAddressChanged(object? sender, AddressChangedEventArgs eventArgs)
    {
        Program.WriteLog($"Address changed: {eventArgs.Address}");
        UpdateUi(() => addressBox.Text = eventArgs.Address);
    }

    private void BrowserTitleChanged(object? sender, TitleChangedEventArgs eventArgs)
    {
        UpdateUi(() => Text = string.IsNullOrWhiteSpace(eventArgs.Title) ? "Tech Browser GDI" : $"{eventArgs.Title} – Tech Browser GDI");
    }

    private void BrowserLoadingStateChanged(object? sender, LoadingStateChangedEventArgs eventArgs)
    {
        Program.WriteLog($"Loading state: IsLoading={eventArgs.IsLoading}; Back={eventArgs.CanGoBack}; Forward={eventArgs.CanGoForward}");
        UpdateUi(() =>
        {
            backButton.Enabled = eventArgs.CanGoBack;
            forwardButton.Enabled = eventArgs.CanGoForward;
            reloadButton.Text = eventArgs.IsLoading ? "×" : "↻";
            statusLabel.Text = eventArgs.IsLoading ? "Lädt …" : string.Empty;
        });
    }

    private void BrowserLoadError(object? sender, LoadErrorEventArgs eventArgs)
    {
        if (eventArgs.ErrorCode == CefErrorCode.Aborted) return;
        Program.WriteLog($"Load error: Url={eventArgs.FailedUrl}; Code={eventArgs.ErrorCode}; Text={eventArgs.ErrorText}");
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
        Program.WriteLog($"Navigate requested: {address}");
        statusLabel.Text = "Öffne …";
        browser.LoadUrl(address);
    }

    private void UpdateUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }

    private static string NormalizeAddress(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (value.Length == 0) return "about:blank";
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
        return $"https://www.google.com/search?q={Uri.EscapeDataString(value)}";
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
