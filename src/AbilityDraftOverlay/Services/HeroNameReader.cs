using System.Runtime.InteropServices;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AbilityDraftOverlay.Services;

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
        using var prepared = Prepare(crop);
        var lines = Recognize(engine, prepared);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("OCR имени героя @({X},{Y}): {Lines}", band.X, band.Y, string.Join(" | ", lines));
        return lines;
    }

    /// <summary>BGR crop -> upscaled, binarized, inverted (black text on white) BGRA image.</summary>
    private static Mat Prepare(Mat bgr)
    {
        const int scale = 4;
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
            Cv2.Resize(score8, big, new OpenCvSharp.Size(score8.Width * scale, score8.Height * scale), 0, 0, InterpolationFlags.Cubic);
            Cv2.GaussianBlur(big, big, new OpenCvSharp.Size(3, 3), 0);
            using var binary = new Mat();
            Cv2.Threshold(big, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.BitwiseNot(binary, binary);

            using var padded = new Mat();
            Cv2.CopyMakeBorder(binary, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.All(255));
            var bgra = new Mat();
            Cv2.CvtColor(padded, bgra, ColorConversionCodes.GRAY2BGRA);
            return bgra;
        }
        finally
        {
            foreach (var channel in channels) channel.Dispose();
        }
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
            _logger.LogWarning(ex, "Сбой OCR при чтении имени героя");
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
                _logger.LogWarning(ex, "Некорректный язык OCR: {Language}", languageTag);
            }

            if (engine is null && !_unavailableLogged)
            {
                _unavailableLogged = true;
                _logger.LogWarning("Windows OCR для языка {Language} недоступен. Установите язык: Параметры → Время и язык → Язык и регион → Добавить язык → English (United States).", languageTag);
            }

            _engine = engine;
            _engineLanguage = languageTag;
            return engine;
        }
    }
}
