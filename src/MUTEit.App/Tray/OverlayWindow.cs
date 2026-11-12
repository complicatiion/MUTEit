using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace MUTEit.App.Tray;

/// <summary>
/// Desktop overlay: a small borderless, always-on-top, per-pixel transparent window (layered window). It never takes
/// the focus (WS_EX_NOACTIVATE), so clicking it does not interrupt a game or a call window.
/// <list type="bullet">
/// <item>Click: switch the microphone (if enabled in the settings).</item>
/// <item>Drag: move it to any position on any monitor; the position is saved when the mouse is released.</item>
/// </list>
/// Note: Windows draws nothing above games in exclusive full screen; borderless / windowed games are fine.
/// </summary>
internal sealed class OverlayWindow : Form
{
    private Bitmap? _image;
    private Point _pressPoint;
    private Point _pressLocation;
    private bool _pressed;
    private bool _dragging;
    private byte _opacity = 230;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "MUTEit Overlay";
        Cursor = Cursors.Hand;
    }

    /// <summary>Click without dragging.</summary>
    public event Action? Clicked;

    /// <summary>Dragging finished at this position (physical screen pixels).</summary>
    public event Action<Point>? Moved;

    /// <summary>Monitor DPI changed (dragged to another monitor): the owner renders a new image.</summary>
    public event Action? DpiChangedTo;

    /// <summary>Click switches the microphone (runtime only, never serialised by the designer).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ClickEnabled { get; set; } = true;

    /// <summary>The left mouse button is held on the overlay (click or drag in progress).</summary>
    public bool IsDragging => _pressed;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeApp.WS_EX_LAYERED | NativeApp.WS_EX_TOOLWINDOW | NativeApp.WS_EX_NOACTIVATE | NativeApp.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>Shows <paramref name="image"/> (takes ownership) with <paramref name="opacityPercent"/>.</summary>
    public void SetImage(Bitmap image, int opacityPercent)
    {
        _image?.Dispose();
        _image = image;
        _opacity = (byte)Math.Clamp((int)Math.Round(opacityPercent * 2.55), 25, 255);
        Size = image.Size;
        if (IsHandleCreated)
        {
            Push();
        }
    }

    /// <summary>Shows the overlay without activating it and keeps it above other topmost windows.</summary>
    public void ShowOverlay()
    {
        if (!Visible)
        {
            // ShowWithoutActivation: shown without taking the focus; the image is pushed when the handle is created.
            Show();
        }

        Push();

        NativeApp.SetWindowPos(Handle, NativeApp.HWND_TOPMOST, 0, 0, 0, 0, NativeApp.SWP_NOMOVE | NativeApp.SWP_NOSIZE | NativeApp.SWP_NOACTIVATE);
    }

    public void HideOverlay()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Push();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeApp.WM_MOUSEACTIVATE)
        {
            m.Result = NativeApp.MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            _dragging = false;
            _pressPoint = Cursor.Position;
            _pressLocation = Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressed)
        {
            return;
        }

        var now = Cursor.Position;
        var dx = now.X - _pressPoint.X;
        var dy = now.Y - _pressPoint.Y;
        var threshold = SystemInformation.DragSize;
        if (!_dragging && (Math.Abs(dx) >= threshold.Width || Math.Abs(dy) >= threshold.Height))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            Location = new Point(_pressLocation.X + dx, _pressLocation.Y + dy);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_pressed)
        {
            return;
        }

        _pressed = false;
        Capture = false;
        if (_dragging)
        {
            _dragging = false;
            Moved?.Invoke(Location);
        }
        else if (ClickEnabled)
        {
            Clicked?.Invoke();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // The size is controlled by the owner (rendered image); do not apply the suggested bounds.
        e.Cancel = true;
        DpiChangedTo?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Copies the image with its alpha channel to the layered window.</summary>
    private void Push()
    {
        if (_image is null || !IsHandleCreated)
        {
            return;
        }

        var screenDc = NativeApp.GetDC(IntPtr.Zero);
        var memoryDc = NativeApp.CreateCompatibleDC(screenDc);
        var bitmap = IntPtr.Zero;
        var old = IntPtr.Zero;
        try
        {
            bitmap = _image.GetHbitmap(Color.FromArgb(0));
            old = NativeApp.SelectObject(memoryDc, bitmap);
            var size = new NativeApp.SIZE(_image.Width, _image.Height);
            var source = new NativeApp.POINT(0, 0);
            var target = new NativeApp.POINT(Left, Top);
            var blend = new NativeApp.BLENDFUNCTION
            {
                BlendOp = NativeApp.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = _opacity,
                AlphaFormat = NativeApp.AC_SRC_ALPHA,
            };
            NativeApp.UpdateLayeredWindow(Handle, screenDc, ref target, ref size, memoryDc, ref source, 0, ref blend, NativeApp.ULW_ALPHA);
        }
        finally
        {
            NativeApp.ReleaseDC(IntPtr.Zero, screenDc);
            if (bitmap != IntPtr.Zero)
            {
                NativeApp.SelectObject(memoryDc, old);
                NativeApp.DeleteObject(bitmap);
            }

            NativeApp.DeleteDC(memoryDc);
        }
    }
}
