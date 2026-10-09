using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace SubExtractor;

public partial class MainForm : Form
{
    private const int keySeekIntervalMs = 50;
    // how far each jump goes forward/backward (ms), set from the video's keyframe interval on drop
    private long keySeekStepMs = 1000;
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly VideoOverlayForm overlay;
    private string infoText = "hello\nworld";
    private int sourceVideoWidth;
    private int sourceVideoHeight;
    private bool isDragging;
    private long lastKeySeekTick;

    private void UpdateInfoText()
    {
        var boxLine = string.Empty;
        if (sourceVideoWidth > 0 && sourceVideoHeight > 0)
        {
            var box = overlay.Box;
            var x = (int)Math.Round(box.X * sourceVideoWidth);
            var y = (int)Math.Round(box.Y * sourceVideoHeight);
            var w = (int)Math.Round(box.Width * sourceVideoWidth);
            var h = (int)Math.Round(box.Height * sourceVideoHeight);
            boxLine = $"Box: [ {x},  {y},  {w},  {h} ]";
        }

        lblInfo.Text = infoText + Environment.NewLine + boxLine;
    }

    private static async Task<long> ReadKeyframeIntervalMsAsync(string path)
    {
        const long fallbackMs = 5000;
        const int keyframeCount = 20; // how many keyframes to sample
        const int scanStartSeconds = 30 * 60; // skip the first 30 minutes
        const int scanSeconds = 140; // how many seconds to scan after the start position
        var startInfo = new System.Diagnostics.ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                 "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey",
                 "-show_entries", "frame=pts_time", "-of", "csv=p=0",
                 "-read_intervals", $"{scanStartSeconds}%+{scanSeconds}", "-i", path
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
            var intervalMs = times.Count < 2
                ? 0
                : (long)Math.Ceiling((times[^1] - times[0]) / (times.Count - 1)) * 1000;
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
        overlay.BoxChanged += (_, _) => UpdateInfoText();


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

            const long testMarginMs = 1500; // try 0, 500, 1000, 2000
            var step = keyData == Keys.Right ? keySeekStepMs + testMarginMs : -keySeekStepMs;


            mediaPlayer.Time = Math.Clamp(mediaPlayer.Time + step, 0, mediaPlayer.Length);
            if (mediaPlayer.State == VLCState.Paused)
            {
                mediaPlayer.NextFrame();
            }
        }

        return true;
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
        lblInfo.Height = (lineHeight * 3) + lblInfo.Padding.Vertical;

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
            infoText = Path.GetFileName(files[0]) + Environment.NewLine + string.Join(
                "    ",
                $"Size: {FormatFileSize(fileInfo.Length)}",
                $"Duration: {FormatDuration(metadata.Duration)}",
                $"Resolution: {FormatResolution(metadata.Height)} ({metadata.Width} x {metadata.Height})",
                $"FPS: {metadata.FrameRate.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"Keyframe interval: {(keySeekStepMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)}s");
            sourceVideoWidth = metadata.Width;
            sourceVideoHeight = metadata.Height;
            UpdateInfoText();
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