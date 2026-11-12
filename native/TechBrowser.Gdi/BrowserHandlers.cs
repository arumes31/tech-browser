using CefSharp;
using CefSharp.Handler;

namespace TechBrowser.Gdi;

// Chromium would open "new window" links in a window of its own, which Backstage cannot show.
// shortcut: only the address is carried over, so a form that posts data to a new window arrives
// without its data; upgrade to a second offscreen browser if a portal depends on that.
internal sealed class SameWindowLifeSpanHandler : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser newBrowser)
    {
        newBrowser = null!;
        browser.MainFrame.LoadUrl(targetUrl);
        return true;
    }
}

// Without these prompts Chromium cancels HTTP logins and untrusted certificates,
// which is what most device and management interfaces use.
internal sealed class PromptingRequestHandler(Form owner) : RequestHandler
{
    protected override bool GetAuthCredentials(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        string originUrl,
        bool isProxy,
        string host,
        int port,
        string realm,
        string scheme,
        IAuthCallback callback)
    {
        owner.BeginInvoke(() =>
        {
            using (callback)
            {
                var credentials = CredentialDialog.Ask(owner, host, realm);
                if (credentials is null) callback.Cancel();
                else callback.Continue(credentials.Value.User, credentials.Value.Password);
            }
        });
        return true;
    }

    protected override bool OnCertificateError(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefErrorCode errorCode,
        string requestUrl,
        ISslInfo sslInfo,
        IRequestCallback callback)
    {
        // CefSharp passes no callback when the error cannot be overridden; the request is cancelled.
        if (callback is null) return false;

        owner.BeginInvoke(() =>
        {
            using (callback)
            {
                var host = Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri) ? uri.Host : requestUrl;
                var answer = MessageBox.Show(
                    owner,
                    $"Das Zertifikat von {host} ist nicht vertrauenswürdig ({errorCode}).\n\nTrotzdem fortfahren?",
                    "Tech Browser",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Yes) callback.Continue(true);
                else callback.Cancel();
            }
        });
        return true;
    }
}

internal static class CredentialDialog
{
    public static (string User, string Password)? Ask(IWin32Window owner, string host, string realm)
    {
        using var dialog = new Form
        {
            Text = "Anmeldung",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(340, 146),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.White,
            ForeColor = Color.Black
        };
        var info = new Label
        {
            Text = string.IsNullOrEmpty(realm) ? host : $"{host} – {realm}",
            AutoEllipsis = true,
            Bounds = new Rectangle(12, 12, 316, 20)
        };
        var user = new TextBox { Bounds = new Rectangle(12, 38, 316, 24), PlaceholderText = "Benutzername", BackColor = Color.White, ForeColor = Color.Black };
        var password = new TextBox { Bounds = new Rectangle(12, 70, 316, 24), PlaceholderText = "Kennwort", UseSystemPasswordChar = true, BackColor = Color.White, ForeColor = Color.Black };
        var ok = CreateDialogButton("Anmelden", new Rectangle(128, 106, 96, 28), DialogResult.OK);
        var cancel = CreateDialogButton("Abbrechen", new Rectangle(232, 106, 96, 28), DialogResult.Cancel);
        dialog.Controls.AddRange([info, user, password, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(owner) == DialogResult.OK ? (user.Text, password.Text) : null;
    }

    private static Button CreateDialogButton(string text, Rectangle bounds, DialogResult result)
    {
        var button = BrowserForm.CreateButton(text, tooltip: null);
        button.Dock = DockStyle.None;
        button.Bounds = bounds;
        button.Font = new Font("Segoe UI", 9F);
        button.TabStop = true;
        button.DialogResult = result;
        return button;
    }
}
