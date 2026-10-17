using System.IO;
using OpenCvSharp;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

public sealed record IconMatch(string Name, string Category, double Confidence, int X, int Y, int Width, int Height);

/// <summary>Recognizes icon images by template matching; no OCR, ML or network access.</summary>
public sealed class ScreenshotRecognitionService
{
    private readonly ILogger<ScreenshotRecognitionService> _logger;
    private static string AssetsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbilityDraftOverlay", "Assets");

    public ScreenshotRecognitionService(ILogger<ScreenshotRecognitionService> logger) => _logger = logger;

    public Task<IReadOnlyList<IconMatch>> RecognizeAsync(string screenshotPath, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<IconMatch>>(() => Recognize(screenshotPath, cancellationToken), cancellationToken);

    private IReadOnlyList<IconMatch> Recognize(string screenshotPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(screenshotPath)) throw new FileNotFoundException("Скриншот не найден.", screenshotPath);
        if (!Directory.Exists(AssetsRoot)) throw new DirectoryNotFoundException($"Каталог ресурсов не найден: {AssetsRoot}");

        using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        if (screenshot.Empty()) throw new InvalidDataException("Не удалось прочитать скриншот как изображение.");
        using var screenshotGray = new Mat();
        Cv2.CvtColor(screenshot, screenshotGray, ColorConversionCodes.BGR2GRAY);

        var templates = Directory.EnumerateFiles(AssetsRoot, "*.png", SearchOption.AllDirectories)
            .Where(path => path.Contains(Path.DirectorySeparatorChar + "abilities" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || path.Contains(Path.DirectorySeparatorChar + "heroes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "legacy" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _logger.LogInformation("Распознавание скриншота {Path}; изображение {Width}x{Height}; шаблонов {Count}", screenshotPath, screenshot.Width, screenshot.Height, templates.Length);
        if (templates.Length == 0) throw new InvalidOperationException("В каталоге ресурсов нет PNG-иконок героев или способностей.");

        var matches = new List<IconMatch>();
        var processed = 0;
        foreach (var path in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var source = Cv2.ImRead(path, ImreadModes.Grayscale);
                if (source.Empty() || source.Width < 12 || source.Height < 12) continue;
                var category = path.Contains(Path.DirectorySeparatorChar + "heroes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "Герой" : "Способность";
                var name = Path.GetFileNameWithoutExtension(path);
                foreach (var scale in new[] { 0.5, 0.75, 1.0 })
                {
                    var width = (int)Math.Round(source.Width * scale);
                    var height = (int)Math.Round(source.Height * scale);
                    if (width < 12 || height < 12 || width >= screenshotGray.Width || height >= screenshotGray.Height) continue;
                    using var template = new Mat();
                    Cv2.Resize(source, template, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Area);
                    using var result = new Mat();
                    Cv2.MatchTemplate(screenshotGray, template, result, TemplateMatchModes.CCoeffNormed);
                    Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);
                    if (maxValue >= 0.88)
                    {
                        matches.Add(new IconMatch(name, category, maxValue, maxLocation.X, maxLocation.Y, width, height));
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is OpenCVException or IOException)
            {
                _logger.LogDebug(ex, "Шаблон пропущен: {Path}", path);
            }
            processed++;
            if (processed % 100 == 0) _logger.LogDebug("Обработано шаблонов: {Processed}/{Total}", processed, templates.Length);
        }

        // Keep only the strongest hit for each icon name and suppress overlapping duplicates.
        var final = new List<IconMatch>();
        foreach (var match in matches.OrderByDescending(m => m.Confidence))
        {
            if (final.Any(existing => existing.Name == match.Name && existing.Category == match.Category)) continue;
            if (final.Any(existing => Math.Abs(existing.X - match.X) < 18 && Math.Abs(existing.Y - match.Y) < 18)) continue;
            final.Add(match);
        }
        _logger.LogInformation("Распознавание завершено: найдено {Count} совпадений из {Templates} шаблонов", final.Count, templates.Length);
        return final;
    }
}
