using System.Globalization;
using LibVLCSharp.WinForms;

namespace SubExtractor;

public class MainForm : Form
{
    private Label lblInfo = null!;
    private Panel pnlDropZone = null!;
    private Label lblDropPrompt = null!;
    private VideoView videoView = null!;
    private System.ComponentModel.IContainer? components;

    public MainForm()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        ClientSize = new Size(settings.ClientSize.Width, settings.ClientSize.Height);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(settings.ClientStartPosition.X, settings.ClientStartPosition.Y);
        lblInfo.TextChanged += (_, _) => LayoutTopControls();
        Resize += (_, _) => LayoutTopControls();

        pnlDropZone.AllowDrop = true;
        lblDropPrompt.AllowDrop = true;
        pnlDropZone.DragEnter += DropZone_DragEnter;
        lblDropPrompt.DragEnter += DropZone_DragEnter;
        pnlDropZone.DragDrop += DropZone_DragDrop;
        lblDropPrompt.DragDrop += DropZone_DragDrop;
        LayoutTopControls();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblInfo = new Label();
        pnlDropZone = new Panel();
        lblDropPrompt = new Label();
        videoView = new VideoView();
        SuspendLayout();
        pnlDropZone.SuspendLayout();
        lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblInfo.AutoSize = false;
        lblInfo.AutoEllipsis = true;
        lblInfo.BackColor = Color.Black;
        lblInfo.ForeColor = Color.White;
        lblInfo.Location = new Point(0, 0);
        lblInfo.Padding = new Padding(10);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(100, 1);
        lblInfo.TabIndex = 0;
        lblInfo.Text = "hello\nworld";
        pnlDropZone.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        pnlDropZone.BackColor = Color.Gray;
        pnlDropZone.Location = new Point(10, 10);
        pnlDropZone.Name = "pnlDropZone";
        pnlDropZone.Padding = new Padding(1);
        pnlDropZone.Size = new Size(1, 1);
        pnlDropZone.TabIndex = 1;
        pnlDropZone.Paint += PnlDropZone_Paint;
        videoView.BackColor = Color.Blue;
        videoView.Location = new Point(1, 1);
        videoView.Name = "videoView";
        videoView.Size = new Size(100, 50);
        videoView.TabIndex = 1;
        lblDropPrompt.Dock = DockStyle.Fill;
        lblDropPrompt.BackColor = Color.Gray;
        lblDropPrompt.Name = "lblDropPrompt";
        lblDropPrompt.Size = new Size(1, 1);
        lblDropPrompt.TabIndex = 0;
        lblDropPrompt.Text = "Drag and drop a video file here";
        lblDropPrompt.TextAlign = ContentAlignment.MiddleCenter;
        pnlDropZone.Controls.Add(videoView);
        pnlDropZone.Controls.Add(lblDropPrompt);
        pnlDropZone.ResumeLayout(false);
        Controls.Add(lblInfo);
        Controls.Add(pnlDropZone);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "MainForm";
        ResumeLayout(false);
    }

    private void LayoutTopControls()
    {
        const int dropZoneMargin = 10;
        lblInfo.Location = Point.Empty;
        lblInfo.Width = ClientSize.Width;
        var lineHeight = TextRenderer.MeasureText(
            "Ag",
            lblInfo.Font,
            Size.Empty,
            TextFormatFlags.NoPadding).Height;
        lblInfo.Height = (lineHeight * 2) + lblInfo.Padding.Vertical;

        var dropZoneTop = lblInfo.Bottom + dropZoneMargin;
        var availableWidth = Math.Max(0, ClientSize.Width - (dropZoneMargin * 2));
        var availableHeight = Math.Max(
            0,
            ClientSize.Height - dropZoneTop - dropZoneMargin);
        var width = Math.Min(availableWidth, availableHeight * 16 / 9);
        var height = (int)Math.Round(width * 9.0 / 16);
        var left = Math.Max(dropZoneMargin, (ClientSize.Width - width) / 2);

        pnlDropZone.Visible = width > 0 && height > 0;
        pnlDropZone.Bounds = new Rectangle(left, dropZoneTop, width, height);
    }

    private void PnlDropZone_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.DrawRectangle(Pens.Black, 0, 0, pnlDropZone.Width - 1, pnlDropZone.Height - 1);
    }

    private static void DropZone_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 })
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private async void DropZone_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files ||
            !File.Exists(files[0]))
        {
            MessageBox.Show(
                this,
                "Drop one video file at a time.",
                "Invalid file drop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        UseWaitCursor = true;
        try
        {
            var metadata = await VideoMetadataReader.ReadAsync(files[0]);
            var fileInfo = new FileInfo(files[0]);
            lblInfo.Text = Path.GetFileName(files[0]) + Environment.NewLine + string.Join(
                "    ",
                $"Size: {FormatFileSize(fileInfo.Length)}",
                $"Duration: {FormatDuration(metadata.Duration)}",
                $"Resolution: {FormatResolution(metadata.Height)} ({metadata.Width} x {metadata.Height})",
                $"FPS: {metadata.FrameRate.ToString("0.##", CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or InvalidDataException
                                   or IOException
                                   or UnauthorizedAccessException
                                   or TimeoutException
                                   or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Could not read video information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const double bytesPerMegabyte = 1024 * 1024;
        const double bytesPerGigabyte = 1024 * bytesPerMegabyte;

        if (bytes > bytesPerGigabyte)
        {
            return $"{bytes / bytesPerGigabyte:0.###} GB";
        }

        return $"{Math.Max(1, bytes / bytesPerMegabyte):0.##} MB";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        return $"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatResolution(int height)
    {
        return height switch
        {
            >= 4320 => "8K",
            >= 2160 => "4K",
            _ => $"{height}p"
        };
    }
}