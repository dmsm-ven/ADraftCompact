using System.Diagnostics;
using System.IO;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvScalar = OpenCvSharp.Scalar;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Recognizes the Ability Draft screen without any OCR/ML:
/// 1. the draft UI is fixed, so the layout JSON says exactly where every hero/ability slot is;
/// 2. every slot (~60 small crops) is reduced to a tiny descriptor and compared with the precomputed icon index.
/// This replaces sliding thousands of templates over the entire frame.
/// </summary>
public sealed class ScreenshotRecognitionService
{
    private readonly IconIndex _index;
    private readonly DraftLayoutProvider _layouts;
    private readonly HeroNameReader _heroNames;
    private readonly ILogger<ScreenshotRecognitionService> _logger;

    public ScreenshotRecognitionService(IconIndex index, DraftLayoutProvider layouts, HeroNameReader heroNames,
        ILogger<ScreenshotRecognitionService> logger)
    {
        _index = index;
        _layouts = layouts;
        _heroNames = heroNames;
        _logger = logger;
    }

    public Task<DraftSnapshot> RecognizeAsync(string screenshotPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(screenshotPath, cancellationToken), cancellationToken);

    public DraftSnapshot Recognize(string screenshotPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(screenshotPath)) throw new FileNotFoundException("Скриншот не найден.", screenshotPath);
        using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        if (screenshot.Empty()) throw new InvalidDataException("Не удалось прочитать скриншот как изображение.");
        return Recognize(screenshot, screenshotPath, cancellationToken);
    }

    /// <summary>Recognizes an already captured BGR frame. The caller keeps ownership of the Mat.</summary>
    public DraftSnapshot Recognize(Mat screenshot, string sourceLabel, CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        _index.EnsureLoaded();
        var indexTime = total.Elapsed;

        var layout = _layouts.Load();
        if (screenshot.Width * 9 != screenshot.Height * 16)
            _logger.LogWarning("Кадр {W}x{H} не 16:9; координаты раскладки будут промасштабированы неточно", screenshot.Width, screenshot.Height);

        var placements = layout.Expand(screenshot.Width, screenshot.Height);
        var results = new SlotResult?[placements.Count];

        // The hero card art is animated, so heroes are read by their name line (OCR); images remain a fallback.
        var heroSource = ResolveHeroSource(layout.Matching);
        var useOcr = heroSource == "ocr";
        var boardDetected = true;

        if (useOcr)
        {
            // Names first: they also tell whether this frame is the draft board at all.
            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].Type != SlotType.Hero || placements[i].NameRoi is null) continue;
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = RecognizeHeroByName(screenshot, placements[i], layout.Matching);
            }

            var cards = results.Count(r => r is not null && r.Score >= layout.Matching.HeroNameMinScore);
            boardDetected = cards >= layout.Matching.BoardMinCards;
            if (!boardDetected)
            {
                _logger.LogInformation("Кадр {Source}: доска Ability Draft не найдена (карточек с именем героя или NO HERO: {Cards} из {Min})",
                    sourceLabel, cards, layout.Matching.BoardMinCards);
                total.Stop();
                return new DraftSnapshot(results.Where(r => r is not null).Select(r => r!).ToArray(), total.Elapsed, indexTime,
                    layout.Calibrated, null, heroSource, BoardDetected: false);
            }
        }

        Parallel.For(0, placements.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            if (results[i] is not null) return; // heroes already read by name
            results[i] = RecognizeSlot(screenshot, placements[i], layout.Matching);
        });

        var final = results.Select(r => r!).ToArray();
        var debugPath = TrySaveDebugImage(screenshot, final);
        total.Stop();

        _logger.LogInformation("Кадр {Source} ({W}x{H}): слотов {Slots}, распознано {Ok}, неуверенно {Unsure}, пусто {Empty}; всего {Total} мс (загрузка индекса {Index} мс)",
            sourceLabel, screenshot.Width, screenshot.Height, final.Length,
            final.Count(r => r.State == SlotState.Recognized), final.Count(r => r.State == SlotState.Uncertain),
            final.Count(r => r.State == SlotState.Empty), (long)total.Elapsed.TotalMilliseconds, (long)indexTime.TotalMilliseconds);

        return new DraftSnapshot(final, total.Elapsed, indexTime, layout.Calibrated, debugPath, heroSource, boardDetected);
    }

    private string ResolveHeroSource(MatchingOptions options)
    {
        var wanted = options.HeroSource.Trim().ToLowerInvariant();
        if (wanted == "image") return "image";
        if (_heroNames.IsAvailable(options.OcrLanguage)) return "ocr";
        if (wanted == "ocr")
            throw new InvalidOperationException($"OCR для языка {options.OcrLanguage} недоступен. Установите язык в Windows (Параметры → Время и язык → Язык и регион → Добавить язык → English (United States)) или поставьте heroSource: \"image\" в раскладке.");
        _logger.LogWarning("OCR недоступен, герои определяются по картинкам (ненадёжно: арт героя анимирован)");
        return "image";
    }

    private SlotResult RecognizeHeroByName(Mat screenshot, SlotPlacement slot, MatchingOptions options)
    {
        var band = slot.NameRoi!.Value;
        var fits = band.X >= 0 && band.Y >= 0 && band.Right <= screenshot.Width && band.Bottom <= screenshot.Height
                   && band.Width >= 8 && band.Height >= 8;
        if (!fits) return new SlotResult(slot, SlotState.OutOfBounds, null, null, 0, 0, null);

        var lines = _heroNames.ReadLines(screenshot, band, options.OcrLanguage);
        var raw = string.Join(" | ", lines);
        var match = HeroNameMatcher.Resolve(lines, _index.HeroCatalog, options.HeroNameMinScore);

        if (match.IsNoHero) return new SlotResult(slot, SlotState.Empty, null, null, match.Score, match.Margin, null, raw);
        if (match.Hero is not { } hero) return new SlotResult(slot, SlotState.Empty, null, null, 0, 0, null, raw);

        var state = match.Score >= options.HeroNameMinScore && match.Margin >= options.HeroNameMinMargin
            ? SlotState.Recognized : SlotState.Uncertain;
        return new SlotResult(slot, state, hero.Id, hero.Name, match.Score, match.Margin, match.RunnerUp?.Id, raw);
    }

    private SlotResult RecognizeSlot(Mat screenshot, SlotPlacement slot, MatchingOptions options)
    {
        var roi = slot.Roi;
        var inset = Math.Max(0, options.Inset);
        var shift = Math.Max(0, options.ShiftPx);

        // Every shifted crop lies inside the slot inflated by `shift`, so one bounds check is enough.
        var fits = roi.X - shift >= 0 && roi.Y - shift >= 0
                   && roi.Right + shift <= screenshot.Width && roi.Bottom + shift <= screenshot.Height
                   && roi.Width - 2 * inset >= 8 && roi.Height - 2 * inset >= 8;
        if (!fits) return new SlotResult(slot, SlotState.OutOfBounds, null, null, 0, 0, null);

        var width = roi.Width - 2 * inset;
        var height = roi.Height - 2 * inset;
        var baseX = roi.X + inset;
        var baseY = roi.Y + inset;

        if (IsEmpty(screenshot, roi, slot.Type, options))
            return new SlotResult(slot, SlotState.Empty, null, null, 0, 0, null);

        var bank = slot.Type == SlotType.Hero ? _index.Heroes : _index.Abilities;
        var best = new SearchHit(null, float.MinValue, null, -1f);
        foreach (var (dx, dy) in ShiftOffsets(shift))
        {
            using var crop = new Mat(screenshot, new CvRect(baseX + dx, baseY + dy, width, height));
            var descriptor = IconIndex.ComputeDescriptor(crop);
            if (descriptor is null) continue;
            var hit = bank.Search(descriptor);
            if (hit.Id is not null && hit.Score > best.Score) best = hit;
        }

        if (best.Id is null) return new SlotResult(slot, SlotState.Empty, null, null, 0, 0, null);

        var runnerUp = best.RunnerUpId is null ? 0.0 : Math.Max(0.0, best.RunnerUpScore);
        var margin = best.Score - runnerUp;
        var state = best.Score >= options.MinScore && margin >= options.MinMargin ? SlotState.Recognized : SlotState.Uncertain;
        return new SlotResult(slot, state, best.Id, _index.GetDisplayName(slot.Type, best.Id), best.Score, margin, best.RunnerUpId);
    }

    /// <summary>
    /// Emptiness is judged on the slot center only. An empty ability slot is an almost black square
    /// (mean brightness ~0-15 vs ~65+ for any icon on the reference screenshot); the "NO HERO" silhouette is
    /// a flat low-contrast picture (std-dev ~15-30 vs ~50+ for real hero art).
    /// </summary>
    private static bool IsEmpty(Mat screenshot, PixelRect roi, SlotType type, MatchingOptions options)
    {
        var trim = Math.Max(0, options.EmptyInset);
        var width = roi.Width - 2 * trim;
        var height = roi.Height - 2 * trim;
        if (width < 4 || height < 4) return false;

        using var center = new Mat(screenshot, new CvRect(roi.X + trim, roi.Y + trim, width, height));
        if (type == SlotType.Hero)
        {
            Cv2.MeanStdDev(center, out _, out var std);
            return Math.Max(std.Val0, Math.Max(std.Val1, std.Val2)) < options.HeroEmptyMaxStdDev;
        }

        var mean = Cv2.Mean(center);
        return (mean.Val0 + mean.Val1 + mean.Val2) / 3.0 < options.AbilityEmptyMaxMean;
    }

    private static IEnumerable<(int Dx, int Dy)> ShiftOffsets(int shift)
    {
        yield return (0, 0);
        if (shift == 0) yield break;
        yield return (shift, 0);
        yield return (-shift, 0);
        yield return (0, shift);
        yield return (0, -shift);
    }

    /// <summary>Draws every slot rectangle and its verdict on a copy of the frame - the main tool for calibrating the layout.</summary>
    private string? TrySaveDebugImage(Mat screenshot, IReadOnlyList<SlotResult> results)
    {
        try
        {
            using var canvas = screenshot.Clone();
            foreach (var r in results)
            {
                var roi = r.Slot.Roi;
                var color = r.State switch
                {
                    SlotState.Recognized => new CvScalar(80, 220, 80),
                    SlotState.Uncertain => new CvScalar(0, 215, 255),
                    SlotState.Empty => new CvScalar(160, 160, 160),
                    _ => new CvScalar(60, 60, 255)
                };
                Cv2.Rectangle(canvas, new CvRect(roi.X, roi.Y, roi.Width, roi.Height), color, 1);
                if (r.Slot.NameRoi is { } name)
                    Cv2.Rectangle(canvas, new CvRect(name.X, name.Y, name.Width, name.Height), color, 1);
                var label = r.Slot.Type == SlotType.Hero && r.RawText is not null
                    ? $"P{r.Slot.Player}H '{r.RawText}' -> {r.EntityId ?? r.State.ToString()} {r.Score:0.00}"
                    : r.State is SlotState.Recognized or SlotState.Uncertain
                    ? $"P{r.Slot.Player}{(r.Slot.Type == SlotType.Hero ? "H" : "A" + r.Slot.Index)} {r.EntityId} {r.Score:0.00}"
                    : $"P{r.Slot.Player}{(r.Slot.Type == SlotType.Hero ? "H" : "A" + r.Slot.Index)} {r.State}";
                Cv2.PutText(canvas, label, new CvPoint(roi.X, Math.Max(10, roi.Y - 2)), HersheyFonts.HersheySimplex, 0.3, color, 1);
            }

            Directory.CreateDirectory(AppPaths.Debug);
            var path = Path.Combine(AppPaths.Debug, "last_recognition.png");
            Cv2.ImWrite(path, canvas);
            return path;
        }
        catch (Exception ex) when (ex is IOException or OpenCVException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить отладочное изображение");
            return null;
        }
    }
}
