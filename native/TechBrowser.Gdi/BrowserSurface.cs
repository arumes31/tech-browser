using CefSharp;

namespace TechBrowser.Gdi;

internal sealed class BrowserSurface : Control
{
    private readonly object frameLock = new();
    private Bitmap? frame;

    public Func<IBrowserHost?>? BrowserHostProvider { get; set; }

    // Screen pixels per browser pixel; Chromium expects mouse positions in browser pixels.
    public float BrowserScale { get; set; } = 1F;

    public BrowserSurface()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
    }

    public void SetFrame(Bitmap newFrame)
    {
        Bitmap? oldFrame;
        lock (frameLock)
        {
            oldFrame = frame;
            frame = newFrame;
        }
        oldFrame?.Dispose();

        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(Invalidate);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Color.White);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        lock (frameLock)
        {
            if (frame is not null)
            {
                eventArgs.Graphics.DrawImageUnscaled(frame, 0, 0);
                return;
            }
        }

        TextRenderer.DrawText(
            eventArgs.Graphics,
            "Chromium wird initialisiert …",
            Font,
            ClientRectangle,
            Color.DimGray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        BrowserHostProvider?.Invoke()?.SendMouseMoveEvent(ToCefMouseEvent(eventArgs), mouseLeave: false);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        BrowserHostProvider?.Invoke()?.SendMouseMoveEvent(new CefSharp.MouseEvent(0, 0, CefEventFlags.None), mouseLeave: true);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        BrowserHostProvider?.Invoke()?.SendFocusEvent(true);
        BrowserHostProvider?.Invoke()?.SendMouseClickEvent(ToCefMouseEvent(eventArgs), ToCefButton(eventArgs.Button), mouseUp: false, clickCount: eventArgs.Clicks);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        BrowserHostProvider?.Invoke()?.SendMouseClickEvent(ToCefMouseEvent(eventArgs), ToCefButton(eventArgs.Button), mouseUp: true, clickCount: eventArgs.Clicks);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        BrowserHostProvider?.Invoke()?.SendMouseWheelEvent(ToCefMouseEvent(eventArgs), deltaX: 0, deltaY: eventArgs.Delta);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        BrowserHostProvider?.Invoke()?.SendFocusEvent(true);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        base.OnLostFocus(eventArgs);
        BrowserHostProvider?.Invoke()?.SendFocusEvent(false);
    }

    // Tab, arrow keys and Enter belong to the web page, not to WinForms focus navigation.
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void WndProc(ref Message message)
    {
        const int WmKeyDown = 0x0100;
        const int WmKeyUp = 0x0101;
        const int WmChar = 0x0102;
        const int WmSysKeyDown = 0x0104;
        const int WmSysKeyUp = 0x0105;
        const int WmSysChar = 0x0106;

        if (message.Msg is WmKeyDown or WmKeyUp or WmChar or WmSysKeyDown or WmSysKeyUp or WmSysChar)
        {
            // ToInt32 throws on 64-bit Windows for key-up messages, whose top bits are set.
            BrowserHostProvider?.Invoke()?.SendKeyEvent(message.Msg, unchecked((int)(long)message.WParam), unchecked((int)(long)message.LParam));
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (frameLock)
            {
                frame?.Dispose();
                frame = null;
            }
        }
        base.Dispose(disposing);
    }

    private CefSharp.MouseEvent ToCefMouseEvent(MouseEventArgs eventArgs)
    {
        var flags = CefEventFlags.None;
        if ((ModifierKeys & Keys.Control) != 0) flags |= CefEventFlags.ControlDown;
        if ((ModifierKeys & Keys.Shift) != 0) flags |= CefEventFlags.ShiftDown;
        if ((ModifierKeys & Keys.Alt) != 0) flags |= CefEventFlags.AltDown;
        if ((Control.MouseButtons & MouseButtons.Left) != 0) flags |= CefEventFlags.LeftMouseButton;
        if ((Control.MouseButtons & MouseButtons.Middle) != 0) flags |= CefEventFlags.MiddleMouseButton;
        if ((Control.MouseButtons & MouseButtons.Right) != 0) flags |= CefEventFlags.RightMouseButton;
        return new CefSharp.MouseEvent((int)(eventArgs.X / BrowserScale), (int)(eventArgs.Y / BrowserScale), flags);
    }

    private static MouseButtonType ToCefButton(MouseButtons button)
    {
        return button switch
        {
            MouseButtons.Right => MouseButtonType.Right,
            MouseButtons.Middle => MouseButtonType.Middle,
            _ => MouseButtonType.Left
        };
    }
}
