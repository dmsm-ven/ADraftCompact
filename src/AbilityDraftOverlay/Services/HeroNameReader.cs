using ADraftCompact.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ADraftCompact.Services;

/// <summary>
/// Reads the hero name line of a player card with the OCR engine built into Windows 10/11
/// (offline, no extra packages). The line is first turned into black text on white: the name is the only
/// saturated bright text in the band, which removes the animated art behind it.
/// </summary>
public sealed class HeroNameReader
{
    private readonly ILogger<HeroNameReader> _logger;
    private readonly object _gate = new();
    private OcrEngine? _engine;
    private string? _engineLanguage;
    private bool _unavailableLogged;

    public HeroNameReader(ILogger<HeroNameReader> logger) => _logger = logger;

    /// <summary>True if an OCR engine for the language could be created. The result is cached per language.</summary>
    public bool IsAvailable(string languageTag) => GetEngine(languageTag) is not null;

    /// <summary>Returns the recognized lines of the band, or an empty list if OCR failed.</summary>
    public IReadOnlyList<string> ReadLines(Mat screenshot, PixelRect band, string languageTag)
    {
        var engine = GetEngine(languageTag);
        if (engine is null) return Array.Empty<string>();

        using var crop = new Mat(screenshot, new Rect(band.X, band.Y, band.Width, band.Height));
        using var binary = Binarize(crop);

        // 1) Only letter-sized shapes, cropped tightly around the text. Bright parts of the animated art next to
        //    the name (it was a black blob beside "BANE") otherwise make Windows OCR return nothing.
        IReadOnlyList<string> lines = Array.Empty<string>();
        using (var cleaned = BuildCleaned(binary, band.Height))
            if (cleaned is not null) lines = Recognize(engine, cleaned);

        // 2) Fallback: the whole band as is.
        if (lines.Count == 0)
        {
            using var whole = BuildWhole(binary);
            lines = Recognize(engine, whole);
        }

        // 3) Still nothing (e.g. fiery red art behind "LINA" looks as saturated as the text): the text is white,
        //    so keep only pixels that are bright in every channel.
        if (lines.Count == 0)
        {
            using var white = BinarizeWhite(crop);
            using (var cleaned = BuildCleaned(white, band.Height))
                if (cleaned is not null) lines = Recognize(engine, cleaned);
            if (lines.Count == 0)
            {
                using var whole = BuildWhole(white);
                lines = Recognize(engine, whole);
            }
        }

        if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            _logger.LogDebug("Hero name OCR @({X},{Y}): {Lines}", band.X, band.Y, string.Join(" | ", lines));
        return lines;
    }

    private const int Scale = 4;

    /// <summary>BGR crop -> upscaled binary mask of near-white pixels (min channel is high).</summary>
    private static Mat BinarizeWhite(Mat bgr)
    {
        var channels = Cv2.Split(bgr);
        try
        {
            using var min = new Mat();
            Cv2.Min(channels[0], channels[1], min);
            Cv2.Min(min, channels[2], min);
            using var big = new Mat();
            Cv2.Resize(min, big, new OpenCvSharp.Size(min.Width * Scale, min.Height * Scale), 0, 0, InterpolationFlags.Cubic);
            Cv2.GaussianBlur(big, big, new OpenCvSharp.Size(3, 3), 0);
            var binary = new Mat();
            Cv2.Threshold(big, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            return binary;
        }
        finally
        {
            foreach (var channel in channels) channel.Dispose();
        }
    }

    /// <summary>BGR crop -> upscaled binary mask where the (saturated, bright) text is white.</summary>
    private static Mat Binarize(Mat bgr)
    {
        using var floats = new Mat();
        bgr.ConvertTo(floats, MatType.CV_32FC3);
        var channels = Cv2.Split(floats);
        try
        {
            using var max = new Mat();
            using var min = new Mat();
            Cv2.Max(channels[0], channels[1], max);
            Cv2.Max(max, channels[2], max);
            Cv2.Min(channels[0], channels[1], min);
            Cv2.Min(min, channels[2], min);

            // score = (max - min) + 0.6 * (max - 120): saturated and bright pixels are text.
            using var spread = new Mat();
            Cv2.Subtract(max, min, spread);
            using var score = new Mat();
            Cv2.AddWeighted(spread, 1.0, max, 0.6, -72.0, score);
            using var score8 = new Mat();
            score.ConvertTo(score8, MatType.CV_8UC1); // saturates to 0..255

            using var big = new Mat();
            Cv2.Resize(score8, big, new OpenCvSharp.Size(score8.Width * Scale, score8.Height * Scale), 0, 0, InterpolationFlags.Cubic);
            Cv2.GaussianBlur(big, big, new OpenCvSharp.Size(3, 3), 0);
            var binary = new Mat();
            Cv2.Threshold(big, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            return binary;
        }
        finally
        {
            foreach (var channel in channels) channel.Dispose();
        }
    }

    /// <summary>
    /// Keeps only letter-like components (height 25-80% of the band, not wider than a letter, on the common text row),
    /// crops to them and returns black text on white (BGRA). Null if nothing letter-like was found.
    /// </summary>
    private static Mat? BuildCleaned(Mat binary, int bandHeight)
    {
        var h = bandHeight * Scale;
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var letters = contours.Select(contour => Cv2.BoundingRect(contour))
            .Where(r => r.Height >= 0.25 * h && r.Height <= 0.8 * h && r.Width <= 1.6 * r.Height + 4 && r.Width >= 0.02 * h)
            .ToList();
        if (letters.Count == 0) return null;

        var centers = letters.Select(r => r.Y + r.Height / 2.0).OrderBy(v => v).ToArray();
        var median = centers[centers.Length / 2];
        letters = letters.Where(r => Math.Abs(r.Y + r.Height / 2.0 - median) <= 0.3 * h).ToList();
        if (letters.Count == 0) return null;

        using var clean = Mat.Zeros(binary.Size(), MatType.CV_8UC1).ToMat();
        foreach (var rect in letters)
        {
            using var source = new Mat(binary, rect);
            using var target = new Mat(clean, rect);
            source.CopyTo(target);
        }

        var x0 = letters.Min(r => r.X);
        var y0 = letters.Min(r => r.Y);
        var x1 = letters.Max(r => r.X + r.Width);
        var y1 = letters.Max(r => r.Y + r.Height);
        using var tight = new Mat(clean, new Rect(x0, y0, x1 - x0, y1 - y0));
        return ToBlackOnWhiteBgra(tight, 24);
    }

    private static Mat BuildWhole(Mat binary) => ToBlackOnWhiteBgra(binary, 12);

    private static Mat ToBlackOnWhiteBgra(Mat whiteText, int border)
    {
        using var inverted = new Mat();
        Cv2.BitwiseNot(whiteText, inverted);
        using var padded = new Mat();
        Cv2.CopyMakeBorder(inverted, padded, border, border, border, border, BorderTypes.Constant, Scalar.All(255));
        var bgra = new Mat();
        Cv2.CvtColor(padded, bgra, ColorConversionCodes.GRAY2BGRA);
        return bgra;
    }

    private IReadOnlyList<string> Recognize(OcrEngine engine, Mat bgra)
    {
        try
        {
            var size = (int)(bgra.Total() * bgra.ElemSize());
            var bytes = new byte[size];
            Marshal.Copy(bgra.Data, bytes, 0, size);

            var writer = new DataWriter();
            writer.WriteBytes(bytes);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, bgra.Width, bgra.Height);

            // Called from a worker thread without a synchronization context, so blocking here is safe.
            var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            return result.Lines.Select(line => line.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "OCR failed while reading hero name");
            return Array.Empty<string>();
        }
    }

    private OcrEngine? GetEngine(string languageTag)
    {
        lock (_gate)
        {
            if (_engine is not null && _engineLanguage == languageTag) return _engine;

            OcrEngine? engine = null;
            try
            {
                var language = new Language(languageTag);
                if (OcrEngine.IsLanguageSupported(language))
                    engine = OcrEngine.TryCreateFromLanguage(language);
            }
            catch (Exception ex) when (ex is ArgumentException or COMException)
            {
                _logger.LogWarning(ex, "Invalid OCR language: {Language}", languageTag);
            }

            if (engine is null && !_unavailableLogged)
            {
                _unavailableLogged = true;
                _logger.LogWarning("Windows OCR is unavailable for language {Language}. Install English (United States) in Windows language settings.", languageTag);
            }

            _engine = engine;
            _engineLanguage = languageTag;
            return engine;
        }
    }
}
