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
    public static SubtitleBoundsScanResult Scan(
        string framesDirectory,
        IProgress<int>? progress = null)
    {
        var framePaths = Directory
            .GetFiles(framesDirectory, "frame_*.jpg")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (framePaths.Length == 0)
        {
            throw new InvalidDataException("No extracted JPG frames were found to scan.");
        }

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        var regionsDetected = 0;
        var imageWidth = 0;
        var imageHeight = 0;
        var lastProgressUpdate = System.Diagnostics.Stopwatch.StartNew();
        Common.WriteDiagnostic(
            "PaddleOCR backend: CUDA GPU 0 (RTX 30-series runtime), detection-only ChineseV5 model (shared by PaddleSharp's Chinese and English V5 model definitions)");
        using var detector = new PaddleOcrDetector(
            LocalDetectionModel.ChineseV5,
            PaddleDevice.Gpu(deviceId: 0));

        for (var i = 0; i < framePaths.Length; i++)
        {
            using var image = Cv2.ImRead(framePaths[i], ImreadModes.Color);
            if (image.Empty())
            {
                throw new InvalidDataException($"Could not read extracted frame: {framePaths[i]}");
            }

            imageWidth = image.Width;
            imageHeight = image.Height;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var detectedRegions = detector.Run(image);
            Common.WriteDiagnostic($"detect {sw.ElapsedMilliseconds} ms");


            for (var regionIndex = 0; regionIndex < detectedRegions.Length; regionIndex++)
            {
                var regionLeft = float.PositiveInfinity;
                var regionTop = float.PositiveInfinity;
                var regionRight = float.NegativeInfinity;
                var regionBottom = float.NegativeInfinity;
                foreach (var point in detectedRegions[regionIndex].Points())
                {
                    regionLeft = Math.Min(regionLeft, point.X);
                    regionTop = Math.Min(regionTop, point.Y);
                    regionRight = Math.Max(regionRight, point.X);
                    regionBottom = Math.Max(regionBottom, point.Y);
                    left = Math.Min(left, (int)Math.Floor(point.X));
                    top = Math.Min(top, (int)Math.Floor(point.Y));
                    right = Math.Max(right, (int)Math.Ceiling(point.X));
                    bottom = Math.Max(bottom, (int)Math.Ceiling(point.Y));
                }

                var regionX = (int)Math.Floor(regionLeft);
                var regionY = (int)Math.Floor(regionTop);
                var regionWidth = (int)Math.Ceiling(regionRight) - regionX;
                var regionHeight = (int)Math.Ceiling(regionBottom) - regionY;
                // Common.WriteDiagnostic(
                //     $"  detected box {regionIndex + 1}: {{{regionX},{regionY},{regionWidth},{regionHeight}}}");
                // regionsDetected++;
            }

            if (progress is not null &&
                (i == framePaths.Length - 1 ||
                 lastProgressUpdate.Elapsed >= TimeSpan.FromMilliseconds(500)))
            {
                progress.Report(i + 1);
                lastProgressUpdate.Restart();
            }
        }

        Rectangle bounds;
        if (regionsDetected == 0)
        {
            bounds = Rectangle.Empty;
        }
        else
        {
            var boundedLeft = Math.Clamp(left, 0, imageWidth - 1);
            var boundedTop = Math.Clamp(top, 0, imageHeight - 1);
            bounds = Rectangle.FromLTRB(
                boundedLeft,
                boundedTop,
                Math.Clamp(Math.Max(left + 1, right), boundedLeft + 1, imageWidth),
                Math.Clamp(Math.Max(top + 1, bottom), boundedTop + 1, imageHeight));
        }

        Common.WriteDiagnostic(
            $"PaddleOCR union across {framePaths.Length} frames: {{{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}}}; {regionsDetected} region(s)");
        return new SubtitleBoundsScanResult(bounds, framePaths.Length, regionsDetected);
    }
}
