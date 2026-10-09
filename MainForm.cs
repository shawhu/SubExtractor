using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace SubExtractor;

public class MainForm : Form
{
    private const int keySeekIntervalMs = 50;
    // how far each jump goes forward/backward (ms), set from the video's keyframe interval on drop
    private long keySeekStepMs = 1000;
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly VideoOverlayForm overlay;
    private Label lblInfo = null!;
    private Panel pnlDropZone = null!;
    private Label lblDropPrompt = null!;
    private VideoView videoView = null!;
    private TrackBar trackPosition = null!;
    private Button btnPlayStop = null!;
    private Button btnPause = null!;
    private System.Windows.Forms.Timer playbackTimer = null!;
    private bool isDragging;
    private long lastKeySeekTick;
    private System.ComponentModel.IContainer? components;

    private static async Task<long> ReadKeyframeIntervalMsAsync(string path)
    {
        const long fallbackMs = 5000;
        const int keyframeCount = 4; // how many keyframes to sample
        const int scanSeconds = 30; // how many seconds from the start of the file to scan
        var startInfo = new System.Diagnostics.ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                 "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey",
                 "-show_entries", "frame=pts_time", "-of", "csv=p=0",
                 "-read_intervals", $"%+{scanSeconds}", "-i", path
             })
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var times = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => double.TryParse(line.TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : -1)
                .Where(t => t >= 0)
                .Take(keyframeCount)
                .ToList();
            var intervalMs = times.Count < 2 ? 0 : (long)((times[^1] - times[0]) / (times.Count - 1) * 1000);
            return intervalMs > 0 ? intervalMs : fallbackMs;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return fallbackMs;
        }
    }

    public MainForm()
    {
        Core.Initialize();
        libVlc = new LibVLC("--input-fast-seek", "--avcodec-hw=none");
        mediaPlayer = new MediaPlayer(libVlc);

        InitializeComponent();
        videoView.MediaPlayer = mediaPlayer;

        overlay = new VideoOverlayForm { Owner = this };
        overlay.DragEnter += DropZone_DragEnter;
        overlay.DragDrop += DropZone_DragDrop;

        var settings = AppSettings.Load();
        ClientSize = new Size(settings.ClientSize.Width, settings.ClientSize.Height);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(settings.ClientStartPosition.X, settings.ClientStartPosition.Y);
        lblInfo.TextChanged += (_, _) => LayoutTopControls();
        Resize += (_, _) => LayoutTopControls();
        Move += (_, _) => SyncOverlay();

        LayoutTopControls();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            videoView.MediaPlayer = null;
            mediaPlayer.Dispose();
            libVlc.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SyncOverlay();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is not (Keys.Left or Keys.Right))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var active = mediaPlayer.IsPlaying || mediaPlayer.State == VLCState.Paused;
        if (active && Environment.TickCount64 - lastKeySeekTick >= keySeekIntervalMs)
        {
            lastKeySeekTick = Environment.TickCount64;
            var step = keyData == Keys.Right ? keySeekStepMs : -keySeekStepMs;
            mediaPlayer.Time = Math.Clamp(mediaPlayer.Time + step, 0, mediaPlayer.Length);
            if (mediaPlayer.State == VLCState.Paused)
            {
                mediaPlayer.NextFrame();
            }
        }

        return true;
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblInfo = new Label();
        pnlDropZone = new Panel();
        lblDropPrompt = new Label();
        videoView = new VideoView();
        trackPosition = new TrackBar();
        btnPlayStop = new Button();
        btnPause = new Button();
        playbackTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        pnlDropZone.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)trackPosition).BeginInit();
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
        videoView.BackColor = Color.Black;
        videoView.Dock = DockStyle.Fill;
        videoView.Name = "videoView";
        videoView.TabIndex = 1;
        lblDropPrompt.Dock = DockStyle.Fill;
        lblDropPrompt.BackColor = Color.Gray;
        lblDropPrompt.Name = "lblDropPrompt";
        lblDropPrompt.Size = new Size(1, 1);
        lblDropPrompt.TabIndex = 0;
        lblDropPrompt.Text = "Drag and drop a video file here";
        lblDropPrompt.TextAlign = ContentAlignment.MiddleCenter;
        trackPosition.AutoSize = false;
        trackPosition.Height = 30;
        trackPosition.Enabled = false;
        trackPosition.Maximum = 1000;
        trackPosition.Name = "trackPosition";
        trackPosition.TabIndex = 2;
        trackPosition.TickStyle = TickStyle.None;
        trackPosition.MouseDown += (_, _) => isDragging = true;
        trackPosition.MouseUp += TrackPosition_MouseUp;
        btnPlayStop.Enabled = false;
        btnPlayStop.Name = "btnPlayStop";
        btnPlayStop.Size = new Size(90, 30);
        btnPlayStop.TabIndex = 3;
        btnPlayStop.Text = "Play";
        btnPlayStop.Click += BtnPlayStop_Click;
        btnPause.Enabled = false;
        btnPause.Name = "btnPause";
        btnPause.Size = new Size(90, 30);
        btnPause.TabIndex = 4;
        btnPause.Text = "Pause";
        btnPause.Click += BtnPause_Click;
        playbackTimer.Interval = 100;
        playbackTimer.Enabled = true;
        playbackTimer.Tick += PlaybackTimer_Tick;
        pnlDropZone.Controls.Add(lblDropPrompt);
        pnlDropZone.Controls.Add(videoView);
        pnlDropZone.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)trackPosition).EndInit();
        Controls.Add(lblInfo);
        Controls.Add(pnlDropZone);
        Controls.Add(trackPosition);
        Controls.Add(btnPlayStop);
        Controls.Add(btnPause);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "MainForm";
        ResumeLayout(false);
        PerformLayout();
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

        var controlsHeight = trackPosition.Height + btnPlayStop.Height + (dropZoneMargin * 2);
        var dropZoneTop = lblInfo.Bottom + dropZoneMargin;
        var availableWidth = Math.Max(0, ClientSize.Width - (dropZoneMargin * 2));
        var availableHeight = Math.Max(
            0,
            ClientSize.Height - dropZoneTop - dropZoneMargin - controlsHeight);
        var width = Math.Min(availableWidth, availableHeight * 16 / 9);
        var height = (int)Math.Round(width * 9.0 / 16);
        var left = Math.Max(dropZoneMargin, (ClientSize.Width - width) / 2);

        pnlDropZone.Visible = width > 0 && height > 0;
        pnlDropZone.Bounds = new Rectangle(left, dropZoneTop, width, height);
        trackPosition.Bounds = new Rectangle(
            left,
            pnlDropZone.Bottom + dropZoneMargin,
            width,
            trackPosition.Height);
        btnPlayStop.Location = new Point(left, trackPosition.Bottom + dropZoneMargin);
        btnPause.Location = new Point(btnPlayStop.Right + dropZoneMargin, btnPlayStop.Top);
        SyncOverlay();
    }

    private void SyncOverlay()
    {
        if (!Visible)
        {
            return;
        }

        overlay.Visible = pnlDropZone.Visible;
        if (overlay.Visible)
        {
            overlay.SyncTo(videoView.RectangleToScreen(videoView.ClientRectangle));
        }
    }

    private void PnlDropZone_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.DrawRectangle(Pens.Black, 0, 0, pnlDropZone.Width - 1, pnlDropZone.Height - 1);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        var state = mediaPlayer.State;
        var active = mediaPlayer.IsPlaying || state == VLCState.Paused;
        btnPlayStop.Text = active ? "Stop" : "Play";
        btnPause.Text = state == VLCState.Paused ? "Resume" : "Pause";
        btnPause.Enabled = active;

        if (!isDragging)
        {
            trackPosition.Value = active ? (int)(mediaPlayer.Position * 1000) : 0;
        }
    }

    private void TrackPosition_MouseUp(object? sender, MouseEventArgs e)
    {
        isDragging = false;
        mediaPlayer.Position = trackPosition.Value / 1000f;
        if (mediaPlayer.State == VLCState.Paused)
        {
            mediaPlayer.NextFrame();
        }
    }

    private void BtnPlayStop_Click(object? sender, EventArgs e)
    {
        if (mediaPlayer.IsPlaying || mediaPlayer.State == VLCState.Paused)
        {
            mediaPlayer.Stop();
        }
        else
        {
            mediaPlayer.Play();
        }
    }

    private void BtnPause_Click(object? sender, EventArgs e)
    {
        mediaPlayer.Pause();
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
            keySeekStepMs = await ReadKeyframeIntervalMsAsync(files[0]);
            lblInfo.Text = Path.GetFileName(files[0]) + Environment.NewLine + string.Join(
                "    ",
                $"Size: {FormatFileSize(fileInfo.Length)}",
                $"Duration: {FormatDuration(metadata.Duration)}",
                $"Resolution: {FormatResolution(metadata.Height)} ({metadata.Width} x {metadata.Height})",
                $"FPS: {metadata.FrameRate.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"Keyframe interval: {(keySeekStepMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)}s");
            lblDropPrompt.Visible = false;
            trackPosition.Enabled = true;
            btnPlayStop.Enabled = true;
            using var media = new Media(libVlc, files[0], FromType.FromPath);

            void PauseOnFirstPlay(object? s, EventArgs a)
            {
                mediaPlayer.Playing -= PauseOnFirstPlay;
                BeginInvoke(() => mediaPlayer.SetPause(true));
            }

            mediaPlayer.Playing += PauseOnFirstPlay;
            mediaPlayer.Play(media);
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