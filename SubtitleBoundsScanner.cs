using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace SubExtractor;

internal readonly record struct SubtitleBoundsScanResult(
    Rectangle Bounds,
    int FramesScanned,
    int RegionsDetected);

internal static class SubtitleBoundsScanner
{
    private const int Gap = 32;

    public static SubtitleBoundsScanResult Scan(
        string framesDirectory,
        IProgress<int>? progress = null)
    {
        var framePaths = Directory
            .GetFiles(framesDirectory, "frame_*.jpg")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (framePaths.Length == 0)
            throw new InvalidDataException("No extracted JPG frames were found to scan.");

        int frameWidth, frameHeight;
        using (var first = Cv2.ImRead(framePaths[0], ImreadModes.Color))
        {
            if (first.Empty())
                throw new InvalidDataException($"Could not read extracted frame: {framePaths[0]}");
            frameWidth = first.Width;
            frameHeight = first.Height;
        }

        var cellWidth = frameWidth + Gap;
        var cellHeight = frameHeight + Gap;
        var columns = Math.Max(1, (int)Math.Round(Math.Sqrt((double)cellHeight / cellWidth)));
        var rows = Math.Max(1, (int)Math.Round((double)columns * cellWidth / cellHeight));
        var batchSize = columns * rows;

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        var regionsDetected = 0;
        Common.WriteDiagnostic(
            $"PaddleOCR backend: CUDA GPU 0 (RTX 30-series runtime), detection-only ChineseV5 model; stitching {batchSize} frame(s) per call ({columns}x{rows} grid)");
        using var detector = new PaddleOcrDetector(
            LocalDetectionModel.ChineseV5,
            PaddleDevice.Gpu(deviceId: 0));
        Common.WriteDiagnostic($"shawhu Detector MaxSize = {detector.MaxSize}");
        using var canvas = new Mat(rows * cellHeight, columns * cellWidth, MatType.CV_8UC3);

        for (var start = 0; start < framePaths.Length; start += batchSize)
        {
            var count = Math.Min(batchSize, framePaths.Length - start);
            canvas.SetTo(Scalar.Black);
            for (var slot = 0; slot < count; slot++)
            {
                using var image = Cv2.ImRead(framePaths[start + slot], ImreadModes.Color);
                if (image.Empty() || image.Width != frameWidth || image.Height != frameHeight)
                    throw new InvalidDataException(
                        $"Frame is unreadable or differs in size from the first frame: {framePaths[start + slot]}");

                using var target = canvas[new Rect(
                    slot % columns * cellWidth,
                    slot / columns * cellHeight,
                    frameWidth,
                    frameHeight)];
                image.CopyTo(target);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var detectedRegions = detector.Run(canvas);
            Common.WriteDiagnostic($"detect {count} frame(s) in {sw.ElapsedMilliseconds} ms");

            foreach (var region in detectedRegions)
            {
                var originX = (int)(region.Center.X / cellWidth) * cellWidth;
                var originY = (int)(region.Center.Y / cellHeight) * cellHeight;
                foreach (var point in region.Points())
                {
                    var x = Math.Clamp(point.X - originX, 0, frameWidth);
                    var y = Math.Clamp(point.Y - originY, 0, frameHeight);
                    left = Math.Min(left, (int)Math.Floor(x));
                    top = Math.Min(top, (int)Math.Floor(y));
                    right = Math.Max(right, (int)Math.Ceiling(x));
                    bottom = Math.Max(bottom, (int)Math.Ceiling(y));
                }

                regionsDetected++;
            }

            progress?.Report(start + count);
        }

        Rectangle bounds;
        if (regionsDetected == 0)
            bounds = Rectangle.Empty;
        else
        {
            var boundedLeft = Math.Clamp(left, 0, frameWidth - 1);
            var boundedTop = Math.Clamp(top, 0, frameHeight - 1);
            bounds = Rectangle.FromLTRB(
                boundedLeft,
                boundedTop,
                Math.Clamp(Math.Max(left + 1, right), boundedLeft + 1, frameWidth),
                Math.Clamp(Math.Max(top + 1, bottom), boundedTop + 1, frameHeight));
        }

        Common.WriteDiagnostic(
            $"PaddleOCR union across {framePaths.Length} frames: {{{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}}}; {regionsDetected} region(s)");
        return new SubtitleBoundsScanResult(bounds, framePaths.Length, regionsDetected);
    }
}