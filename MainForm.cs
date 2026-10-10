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
    private string? currentVideoPath;
    private TimeSpan currentVideoDuration;
    private string? extractionProgress;
    private bool isExtractingFrames;
    private bool isDragging;
    private long lastKeySeekTick;

    private void UpdateInfoText()
    {
        var boxLine = string.Empty;
        if (sourceVideoWidth > 0 && sourceVideoHeight > 0)
        {
            if (TryGetFrameCrop(out var crop))
            {
                boxLine = $"Box: [ {crop.X},  {crop.Y},  {crop.Width},  {crop.Height} ]";
            }
        }

        if (!string.IsNullOrEmpty(extractionProgress))
        {
            boxLine = string.IsNullOrEmpty(boxLine)
                ? extractionProgress
                : $"{boxLine}    {extractionProgress}";
        }

        lblInfo.Text = infoText + Environment.NewLine + boxLine;
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
            Console.WriteLine($"keySeekStepMs:{keySeekStepMs}");

            var testMarginMs = keySeekStepMs / 10;
            //var testMarginMs = 0;
            Console.WriteLine($"testMarginMs:{testMarginMs}");
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
        btnCalibrateRedBox.Location = new Point(btnPause.Right + dropZoneMargin, btnPause.Top);
        SyncOverlay();
    }

    private bool TryGetFrameCrop(out Rectangle crop)
    {
        crop = Rectangle.Empty;
        var viewSize = overlay.ViewSize;
        if (viewSize.Width <= 0 || viewSize.Height <= 0 ||
            sourceVideoWidth <= 0 || sourceVideoHeight <= 0)
        {
            return false;
        }

        var videoAspectRatio = (double)sourceVideoWidth / sourceVideoHeight;
        var viewAspectRatio = (double)viewSize.Width / viewSize.Height;
        double videoLeft;
        double videoTop;
        double videoWidth;
        double videoHeight;
        if (videoAspectRatio <= viewAspectRatio)
        {
            videoHeight = viewSize.Height;
            videoWidth = videoHeight * videoAspectRatio;
            videoLeft = (viewSize.Width - videoWidth) / 2;
            videoTop = 0;
        }
        else
        {
            videoWidth = viewSize.Width;
            videoHeight = videoWidth / videoAspectRatio;
            videoLeft = 0;
            videoTop = (viewSize.Height - videoHeight) / 2;
        }

        var box = overlay.Box;
        var boxLeft = Math.Clamp((double)box.Left * viewSize.Width, 0, viewSize.Width);
        var boxTop = Math.Clamp((double)box.Top * viewSize.Height, 0, viewSize.Height);
        var boxRight = Math.Clamp((double)box.Right * viewSize.Width, 0, viewSize.Width);
        var boxBottom = Math.Clamp((double)box.Bottom * viewSize.Height, 0, viewSize.Height);
        var left = Math.Max(boxLeft, videoLeft);
        var top = Math.Max(boxTop, videoTop);
        var right = Math.Min(boxRight, videoLeft + videoWidth);
        var bottom = Math.Min(boxBottom, videoTop + videoHeight);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        var frameLeft = Math.Clamp(
            (int)Math.Floor((left - videoLeft) * sourceVideoWidth / videoWidth),
            0,
            sourceVideoWidth - 1);
        var frameTop = Math.Clamp(
            (int)Math.Floor((top - videoTop) * sourceVideoHeight / videoHeight),
            0,
            sourceVideoHeight - 1);
        var frameRight = Math.Clamp(
            (int)Math.Ceiling((right - videoLeft) * sourceVideoWidth / videoWidth),
            frameLeft + 1,
            sourceVideoWidth);
        var frameBottom = Math.Clamp(
            (int)Math.Ceiling((bottom - videoTop) * sourceVideoHeight / videoHeight),
            frameTop + 1,
            sourceVideoHeight);
        crop = Rectangle.FromLTRB(frameLeft, frameTop, frameRight, frameBottom);
        return true;
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
            keySeekStepMs = await KeyframeIntervalReader.ReadAsync(files[0]);
            infoText = Path.GetFileName(files[0]) + Environment.NewLine + string.Join(
                "    ",
                $"Size: {Common.FormatFileSize(fileInfo.Length)}",
                $"Duration: {Common.FormatDuration(metadata.Duration)}",
                $"Resolution: {Common.FormatResolution(metadata.Height)} ({metadata.Width} x {metadata.Height})",
                $"FPS: {metadata.FrameRate.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"Keyframe interval: {(keySeekStepMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)}s");
            sourceVideoWidth = metadata.Width;
            sourceVideoHeight = metadata.Height;
            currentVideoPath = files[0];
            currentVideoDuration = metadata.Duration;
            UpdateInfoText();
            lblDropPrompt.Visible = false;
            trackPosition.Enabled = true;
            btnPlayStop.Enabled = true;
            btnCalibrateRedBox.Enabled = !isExtractingFrames;
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

    private async void BtnCalibrateRedBox_Click(object? sender, EventArgs e)
    {
        if (currentVideoPath is null || isExtractingFrames)
        {
            return;
        }

        var videoPath = currentVideoPath;
        if (!TryGetFrameCrop(out var crop))
        {
            MessageBox.Show(
                this,
                "The red box does not overlap the visible video image.",
                "Nothing to extract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var frameCount = Math.Max(1, (long)Math.Ceiling(currentVideoDuration.TotalSeconds));
        var outputDirectory = Path.Combine(
            Path.GetDirectoryName(videoPath)!,
            Path.GetFileNameWithoutExtension(videoPath) + "_frames");

        isExtractingFrames = true;
        btnCalibrateRedBox.Enabled = false;
        extractionProgress = $"0/{frameCount}";
        UpdateInfoText();
        UseWaitCursor = true;

        try
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            Directory.CreateDirectory(outputDirectory);
            await ExtractFramesAsync(videoPath, outputDirectory, frameCount, crop);
            extractionProgress = null;
            UpdateInfoText();
            MessageBox.Show(
                this,
                $"Frame extraction completed. Images are in:{Environment.NewLine}{outputDirectory}",
                "Extraction complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or InvalidDataException
                                   or IOException
                                   or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Frame extraction failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            isExtractingFrames = false;
            extractionProgress = null;
            UpdateInfoText();
            btnCalibrateRedBox.Enabled = currentVideoPath is not null;
            UseWaitCursor = false;
        }
    }

    private async Task ExtractFramesAsync(
        string videoPath,
        string outputDirectory,
        long totalFrames,
        Rectangle crop)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-nostats",
                     "-i", videoPath, "-map", "0:v:0",
                     "-vf", $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y},fps=1",
                     "-q:v", "2",
                     "-start_number", "1", "-progress", "pipe:1", "-f", "image2",
                     Path.Combine(outputDirectory, "frame_%06d.jpg")
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start ffmpeg.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Could not start ffmpeg. Install FFmpeg and make sure ffmpeg is available on PATH.",
                ex);
        }

        var errorTask = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("frame=", StringComparison.Ordinal) &&
                long.TryParse(
                    line.AsSpan("frame=".Length).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var extractedFrames))
            {
                extractionProgress = $"{Math.Min(extractedFrames, totalFrames)}/{totalFrames}";
                UpdateInfoText();
            }
        }

        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(error) ? "ffmpeg could not extract frames." : error.Trim());
        }
    }

}