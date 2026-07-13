namespace VeloUploader.Editor;

/// <summary>
/// Source-monitor picture box with an interactive crop overlay: draw a region,
/// drag to move it, or grab a corner handle to resize. Crop coordinates are in
/// source-video pixels.
/// </summary>
internal sealed class EditorPreviewBox : PictureBox
{
    private enum CropDragMode
    {
        None,
        Draw,
        Move,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
    }

    private bool _dragging;
    private Point _dragStart;
    private Rectangle _dragOriginCropRect;
    private CropDragMode _dragMode;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Size VideoSize { get; set; } = Size.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Rectangle CropRect { get; private set; } = Rectangle.Empty;

    [System.ComponentModel.DefaultValue(true)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCropOverlay { get; set; } = true;

    public event Action<Rectangle>? CropChanged;

    public EditorPreviewBox()
    {
        BackColor = Color.FromArgb(8, 8, 10);
        BorderStyle = BorderStyle.FixedSingle;
        SizeMode = PictureBoxSizeMode.Zoom;
        Cursor = Cursors.Cross;
    }

    public void SetPreviewImage(Image image)
    {
        var old = Image;
        Image = image;
        old?.Dispose();
        Invalidate();
    }

    public Image? ClonePreviewImage() => Image is null ? null : new Bitmap(Image);

    public void ClearPreview()
    {
        var old = Image;
        Image = null;
        old?.Dispose();
        CropRect = Rectangle.Empty;
        Invalidate();
    }

    public void DisposePreviewImage()
    {
        var old = Image;
        Image = null;
        old?.Dispose();
    }

    public void SetCropRect(Rectangle rect)
    {
        CropRect = NormalizeToVideo(rect);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!ShowCropOverlay || e.Button != MouseButtons.Left || VideoSize.Width <= 0 || VideoSize.Height <= 0)
            return;

        var imgRect = GetImageBounds();
        if (!imgRect.Contains(e.Location))
            return;

        _dragging = true;
        _dragStart = e.Location;
        _dragOriginCropRect = CropRect;
        _dragMode = GetDragMode(e.Location, imgRect);

        if (_dragMode == CropDragMode.None)
            _dragMode = CropDragMode.Draw;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            UpdateCursor(e.Location);
            return;
        }

        var rect = BuildDragRect(e.Location);
        if (rect.Width > 1 && rect.Height > 1)
        {
            CropRect = rect;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging)
            return;

        _dragging = false;
        var rect = BuildDragRect(e.Location);
        if (rect.Width > 1 && rect.Height > 1)
        {
            CropRect = rect;
            CropChanged?.Invoke(CropRect);
        }

        _dragMode = CropDragMode.None;
        UpdateCursor(e.Location);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);
        if (!ShowCropOverlay || Image == null || VideoSize.Width <= 0 || VideoSize.Height <= 0 || CropRect.Width <= 0 || CropRect.Height <= 0)
            return;

        var imgRect = GetImageBounds();
        var overlayRect = ToDisplayRect(CropRect, imgRect);
        using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        using var pen = new Pen(EditorTheme.Accent, 2);
        using var gridPen = new Pen(Color.FromArgb(180, 200, 200, 230), 1)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };
        using var handleBrush = new SolidBrush(Color.FromArgb(245, 245, 250));
        using var handleBorder = new Pen(Color.FromArgb(60, 60, 70), 1);
        using var labelBrush = new SolidBrush(Color.FromArgb(190, 12, 12, 15));

        pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, imgRect.Top, imgRect.Width, Math.Max(0, overlayRect.Top - imgRect.Top)));
        pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, overlayRect.Bottom, imgRect.Width, Math.Max(0, imgRect.Bottom - overlayRect.Bottom)));
        pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, overlayRect.Top, Math.Max(0, overlayRect.Left - imgRect.Left), overlayRect.Height));
        pe.Graphics.FillRectangle(shade, new Rectangle(overlayRect.Right, overlayRect.Top, Math.Max(0, imgRect.Right - overlayRect.Right), overlayRect.Height));
        pe.Graphics.DrawRectangle(pen, overlayRect);

        var thirdWidth = overlayRect.Width / 3f;
        var thirdHeight = overlayRect.Height / 3f;
        pe.Graphics.DrawLine(gridPen, overlayRect.Left + thirdWidth, overlayRect.Top, overlayRect.Left + thirdWidth, overlayRect.Bottom);
        pe.Graphics.DrawLine(gridPen, overlayRect.Left + (thirdWidth * 2), overlayRect.Top, overlayRect.Left + (thirdWidth * 2), overlayRect.Bottom);
        pe.Graphics.DrawLine(gridPen, overlayRect.Left, overlayRect.Top + thirdHeight, overlayRect.Right, overlayRect.Top + thirdHeight);
        pe.Graphics.DrawLine(gridPen, overlayRect.Left, overlayRect.Top + (thirdHeight * 2), overlayRect.Right, overlayRect.Top + (thirdHeight * 2));

        var labelRect = new Rectangle(overlayRect.Left + 8, Math.Max(imgRect.Top + 6, overlayRect.Top + 8), 120, 20);
        pe.Graphics.FillRectangle(labelBrush, labelRect);
        TextRenderer.DrawText(pe.Graphics, $"{CropRect.Width}×{CropRect.Height}", Font, labelRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        foreach (var handle in GetHandleRects(overlayRect))
        {
            pe.Graphics.FillRectangle(handleBrush, handle);
            pe.Graphics.DrawRectangle(handleBorder, handle);
        }
    }

    private Rectangle BuildDragRect(Point currentPoint)
    {
        return _dragMode switch
        {
            CropDragMode.Move => MoveCrop(currentPoint),
            CropDragMode.ResizeTopLeft => ResizeCrop(currentPoint, resizeLeft: true, resizeTop: true, resizeRight: false, resizeBottom: false),
            CropDragMode.ResizeTopRight => ResizeCrop(currentPoint, resizeLeft: false, resizeTop: true, resizeRight: true, resizeBottom: false),
            CropDragMode.ResizeBottomLeft => ResizeCrop(currentPoint, resizeLeft: true, resizeTop: false, resizeRight: false, resizeBottom: true),
            CropDragMode.ResizeBottomRight => ResizeCrop(currentPoint, resizeLeft: false, resizeTop: false, resizeRight: true, resizeBottom: true),
            _ => BuildCropRectFromPoints(_dragStart, currentPoint),
        };
    }

    private Rectangle MoveCrop(Point currentPoint)
    {
        var start = ClientToVideoPoint(_dragStart) ?? new Point(_dragOriginCropRect.Left, _dragOriginCropRect.Top);
        var current = ClientToVideoPoint(currentPoint) ?? start;
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;

        var left = Math.Clamp(_dragOriginCropRect.Left + dx, 0, Math.Max(0, VideoSize.Width - _dragOriginCropRect.Width));
        var top = Math.Clamp(_dragOriginCropRect.Top + dy, 0, Math.Max(0, VideoSize.Height - _dragOriginCropRect.Height));
        return new Rectangle(left, top, _dragOriginCropRect.Width, _dragOriginCropRect.Height);
    }

    private Rectangle ResizeCrop(Point currentPoint, bool resizeLeft, bool resizeTop, bool resizeRight, bool resizeBottom)
    {
        var anchor = ClientToVideoPoint(currentPoint) ?? new Point(_dragOriginCropRect.Right, _dragOriginCropRect.Bottom);
        var left = resizeLeft ? anchor.X : _dragOriginCropRect.Left;
        var top = resizeTop ? anchor.Y : _dragOriginCropRect.Top;
        var right = resizeRight ? anchor.X : _dragOriginCropRect.Right;
        var bottom = resizeBottom ? anchor.Y : _dragOriginCropRect.Bottom;
        return NormalizeToVideo(Rectangle.FromLTRB(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom)));
    }

    private void UpdateCursor(Point location)
    {
        if (!ShowCropOverlay || Image == null)
        {
            Cursor = Cursors.Default;
            return;
        }

        var imgRect = GetImageBounds();
        if (!imgRect.Contains(location))
        {
            Cursor = Cursors.Default;
            return;
        }

        Cursor = GetDragMode(location, imgRect) switch
        {
            CropDragMode.Move => Cursors.SizeAll,
            CropDragMode.ResizeTopLeft or CropDragMode.ResizeBottomRight => Cursors.SizeNWSE,
            CropDragMode.ResizeTopRight or CropDragMode.ResizeBottomLeft => Cursors.SizeNESW,
            _ => Cursors.Cross,
        };
    }

    private CropDragMode GetDragMode(Point location, Rectangle imageRect)
    {
        if (CropRect.Width <= 0 || CropRect.Height <= 0)
            return CropDragMode.Draw;

        var overlayRect = ToDisplayRect(CropRect, imageRect);
        var handles = GetHandleRects(overlayRect).ToArray();
        if (handles[0].Contains(location)) return CropDragMode.ResizeTopLeft;
        if (handles[1].Contains(location)) return CropDragMode.ResizeTopRight;
        if (handles[2].Contains(location)) return CropDragMode.ResizeBottomLeft;
        if (handles[3].Contains(location)) return CropDragMode.ResizeBottomRight;
        if (overlayRect.Contains(location)) return CropDragMode.Move;
        return CropDragMode.Draw;
    }

    private static IEnumerable<Rectangle> GetHandleRects(Rectangle overlayRect)
    {
        const int size = 8;
        yield return new Rectangle(overlayRect.Left - (size / 2), overlayRect.Top - (size / 2), size, size);
        yield return new Rectangle(overlayRect.Right - (size / 2), overlayRect.Top - (size / 2), size, size);
        yield return new Rectangle(overlayRect.Left - (size / 2), overlayRect.Bottom - (size / 2), size, size);
        yield return new Rectangle(overlayRect.Right - (size / 2), overlayRect.Bottom - (size / 2), size, size);
    }

    private Rectangle BuildCropRectFromPoints(Point start, Point end)
    {
        var first = ClientToVideoPoint(start);
        var second = ClientToVideoPoint(end);
        if (first == null || second == null)
            return CropRect;

        var left = Math.Min(first.Value.X, second.Value.X);
        var top = Math.Min(first.Value.Y, second.Value.Y);
        var right = Math.Max(first.Value.X, second.Value.X);
        var bottom = Math.Max(first.Value.Y, second.Value.Y);
        return NormalizeToVideo(Rectangle.FromLTRB(left, top, right, bottom));
    }

    private Rectangle NormalizeToVideo(Rectangle rect)
    {
        if (VideoSize.Width <= 0 || VideoSize.Height <= 0)
            return Rectangle.Empty;

        var left = Math.Clamp(rect.Left, 0, VideoSize.Width - 1);
        var top = Math.Clamp(rect.Top, 0, VideoSize.Height - 1);
        var right = Math.Clamp(rect.Right, left + 1, VideoSize.Width);
        var bottom = Math.Clamp(rect.Bottom, top + 1, VideoSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private Point? ClientToVideoPoint(Point point)
    {
        var rect = GetImageBounds();
        if (VideoSize.Width <= 0 || VideoSize.Height <= 0)
            return null;

        var px = Math.Clamp(point.X, rect.Left, rect.Right);
        var py = Math.Clamp(point.Y, rect.Top, rect.Bottom);
        var x = (px - rect.Left) * VideoSize.Width / Math.Max(1, rect.Width);
        var y = (py - rect.Top) * VideoSize.Height / Math.Max(1, rect.Height);
        return new Point(Math.Clamp(x, 0, VideoSize.Width - 1), Math.Clamp(y, 0, VideoSize.Height - 1));
    }

    private Rectangle ToDisplayRect(Rectangle cropRect, Rectangle imageRect)
    {
        var x = imageRect.Left + (int)Math.Round(cropRect.X * imageRect.Width / (double)Math.Max(1, VideoSize.Width));
        var y = imageRect.Top + (int)Math.Round(cropRect.Y * imageRect.Height / (double)Math.Max(1, VideoSize.Height));
        var width = (int)Math.Round(cropRect.Width * imageRect.Width / (double)Math.Max(1, VideoSize.Width));
        var height = (int)Math.Round(cropRect.Height * imageRect.Height / (double)Math.Max(1, VideoSize.Height));
        return new Rectangle(x, y, Math.Max(2, width), Math.Max(2, height));
    }

    private Rectangle GetImageBounds()
    {
        if (Image == null)
            return ClientRectangle;

        var imageRatio = Image.Width / (double)Math.Max(1, Image.Height);
        var boxRatio = Width / (double)Math.Max(1, Height);
        if (imageRatio > boxRatio)
        {
            var drawHeight = (int)Math.Round(Width / imageRatio);
            var y = (Height - drawHeight) / 2;
            return new Rectangle(0, y, Width, drawHeight);
        }

        var drawWidth = (int)Math.Round(Height * imageRatio);
        var x = (Width - drawWidth) / 2;
        return new Rectangle(x, 0, drawWidth, Height);
    }
}
