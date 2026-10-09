using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SubExtractor;

internal sealed class VideoOverlayForm : Form
{
    private const int borderThickness = 2;
    private const int fillAlpha = 3;
    private const int grabDistance = 8;
    private const int minBoxSize = 10;
    private const int initialBoxWidth = 1200;
    private const int initialBoxHeight = 100;
    private const int initialBoxBottom = 20;
    private const int wsExLayered = 0x80000;
    private const int wsExNoActivate = 0x08000000;
    private const int ulwAlpha = 2;
    private const byte acSrcAlpha = 1;
    private static readonly Color boxColor = Color.Red;

    [Flags]
    private enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    private RectangleF box;
    private Rectangle screenBounds;
    private Edges dragEdges;
    private bool isMoving;
    private int moveGrabOffsetY;

    public VideoOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AllowDrop = true;
    }

    // x, y, width, height as fractions (0 to 1) of the overlay's width and height
    public RectangleF Box => box;

    public event EventHandler? BoxChanged;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExLayered | wsExNoActivate;
            return createParams;
        }
    }

    public void SyncTo(Rectangle bounds)
    {
        screenBounds = bounds;
        if (box.IsEmpty)
        {
            var width = Math.Min(1f, (float)initialBoxWidth / bounds.Width);
            var height = Math.Min(1f, (float)initialBoxHeight / bounds.Height);
            box = new RectangleF((1f - width) / 2, 1f - height - ((float)initialBoxBottom / bounds.Height), width, height);
        }

        Render();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var pixels = ToPixels();
        dragEdges = HitTest(e.Location, pixels);
        if (dragEdges == Edges.None && pixels.Contains(e.Location))
        {
            isMoving = true;
            moveGrabOffsetY = e.Y - pixels.Top;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragEdges = Edges.None;
        isMoving = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pixels = ToPixels();
        if (isMoving)
        {
            var top = Math.Clamp(e.Y - moveGrabOffsetY, 0, screenBounds.Height - pixels.Height);
            box = new RectangleF(box.X, (float)top / screenBounds.Height, box.Width, box.Height);
            Render();
            BoxChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (dragEdges == Edges.None)
        {
            var edges = HitTest(e.Location, pixels);
            Cursor = edges == Edges.None && pixels.Contains(e.Location) ? Cursors.SizeAll : CursorFor(edges);
            return;
        }

        var left = pixels.Left;
        var top2 = pixels.Top;
        var right = pixels.Right;
        var bottom = pixels.Bottom;
        var keepHorizontalSymmetry = (ModifierKeys & Keys.Shift) == Keys.Shift;
        if (keepHorizontalSymmetry && dragEdges.HasFlag(Edges.Left))
        {
            var centerTwice = screenBounds.Width;
            var minLeft = Math.Max(0, centerTwice - screenBounds.Width);
            var maxLeft = Math.Min(screenBounds.Width, (centerTwice - minBoxSize) / 2);
            left = Math.Clamp(e.X, minLeft, maxLeft);
            right = centerTwice - left;
        }
        else if (keepHorizontalSymmetry && dragEdges.HasFlag(Edges.Right))
        {
            var centerTwice = screenBounds.Width;
            var minRight = Math.Max(
                centerTwice - screenBounds.Width,
                (centerTwice + minBoxSize + 1) / 2);
            var maxRight = Math.Min(screenBounds.Width, centerTwice);
            right = Math.Clamp(e.X, minRight, maxRight);
            left = centerTwice - right;
        }
        else if (dragEdges.HasFlag(Edges.Left))
        {
            left = Math.Clamp(e.X, 0, right - minBoxSize);
        }
        else if (dragEdges.HasFlag(Edges.Right))
        {
            right = Math.Clamp(e.X, left + minBoxSize, screenBounds.Width);
        }

        if (dragEdges.HasFlag(Edges.Top))
        {
            top2 = Math.Clamp(e.Y, 0, bottom - minBoxSize);
        }

        if (dragEdges.HasFlag(Edges.Bottom))
        {
            bottom = Math.Clamp(e.Y, top2 + minBoxSize, screenBounds.Height);
        }

        box = new RectangleF(
            (float)left / screenBounds.Width,
            (float)top2 / screenBounds.Height,
            (float)(right - left) / screenBounds.Width,
            (float)(bottom - top2) / screenBounds.Height);
        Render();
        BoxChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle ToPixels()
    {
        return Rectangle.Round(new RectangleF(
            box.X * screenBounds.Width,
            box.Y * screenBounds.Height,
            box.Width * screenBounds.Width,
            box.Height * screenBounds.Height));
    }

    private static Edges HitTest(Point point, Rectangle pixels)
    {
        var edges = Edges.None;
        var withinX = point.X >= pixels.Left - grabDistance && point.X <= pixels.Right + grabDistance;
        var withinY = point.Y >= pixels.Top - grabDistance && point.Y <= pixels.Bottom + grabDistance;
        if (withinY && Math.Abs(point.X - pixels.Left) <= grabDistance)
        {
            edges |= Edges.Left;
        }
        else if (withinY && Math.Abs(point.X - pixels.Right) <= grabDistance)
        {
            edges |= Edges.Right;
        }

        if (withinX && Math.Abs(point.Y - pixels.Top) <= grabDistance)
        {
            edges |= Edges.Top;
        }
        else if (withinX && Math.Abs(point.Y - pixels.Bottom) <= grabDistance)
        {
            edges |= Edges.Bottom;
        }

        return edges;
    }

    private static Cursor CursorFor(Edges edges)
    {
        return edges switch
        {
            Edges.Left or Edges.Right => Cursors.SizeWE,
            Edges.Top or Edges.Bottom => Cursors.SizeNS,
            Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
            Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
            _ => Cursors.Default
        };
    }

    private void Render()
    {
        var pixels = ToPixels();
        using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(boxColor, borderThickness))
        {
            graphics.Clear(Color.FromArgb(fillAlpha, 0, 0, 0));
            graphics.DrawRectangle(
                pen,
                pixels.X + (borderThickness / 2),
                pixels.Y + (borderThickness / 2),
                pixels.Width - borderThickness,
                pixels.Height - borderThickness);
        }

        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var memoryDc = CreateCompatibleDC(IntPtr.Zero);
        var oldBitmap = SelectObject(memoryDc, hBitmap);
        var position = screenBounds.Location;
        var size = screenBounds.Size;
        var source = Point.Empty;
        var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = acSrcAlpha };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size, memoryDc, ref source, 0, ref blend, ulwAlpha);
        SelectObject(memoryDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memoryDc);
    }
}