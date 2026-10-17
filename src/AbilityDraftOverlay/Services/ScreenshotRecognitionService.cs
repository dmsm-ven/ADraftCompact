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
    private readonly ILogger<ScreenshotRecognitionService> _logger;

    public ScreenshotRecognitionService(IconIndex index, DraftLayoutProvider layouts, ILogger<ScreenshotRecognitionService> logger)
    {
        _index = index;
        _layouts = layouts;
        _logger = logger;
    }

    public Task<DraftSnapshot> RecognizeAsync(string screenshotPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(screenshotPath, cancellationToken), cancellationToken);

    public DraftSnapshot Recognize(string screenshotPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(screenshotPath)) throw new FileNotFoundException("Скриншот не найден.", screenshotPath);

        var total = Stopwatch.StartNew();
        _index.EnsureLoaded();
        var indexTime = total.Elapsed;

        var layout = _layouts.Load();
        using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        if (screenshot.Empty()) throw new InvalidDataException("Не удалось прочитать скриншот как изображение.");
        if (screenshot.Width * 9 != screenshot.Height * 16)
            _logger.LogWarning("Скриншот {W}x{H} не 16:9; координаты раскладки будут промасштабированы неточно", screenshot.Width, screenshot.Height);

        var placements = layout.Expand(screenshot.Width, screenshot.Height);
        var results = new SlotResult[placements.Count];
        Parallel.For(0, placements.Count, new ParallelOptions { CancellationToken = cancellationToken },
            i => results[i] = RecognizeSlot(screenshot, placements[i], layout.Matching));

        var debugPath = TrySaveDebugImage(screenshot, results);
        total.Stop();

        _logger.LogInformation("Кадр {Path} ({W}x{H}): слотов {Slots}, распознано {Ok}, неуверенно {Unsure}, пусто {Empty}; всего {Total} мс (загрузка индекса {Index} мс)",
            screenshotPath, screenshot.Width, screenshot.Height, results.Length,
            results.Count(r => r.State == SlotState.Recognized), results.Count(r => r.State == SlotState.Uncertain),
            results.Count(r => r.State == SlotState.Empty), (long)total.Elapsed.TotalMilliseconds, (long)indexTime.TotalMilliseconds);

        return new DraftSnapshot(results, total.Elapsed, indexTime, layout.Calibrated, debugPath);
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

        using (var center = new Mat(screenshot, new CvRect(baseX, baseY, width, height)))
        {
            Cv2.MeanStdDev(center, out _, out var std);
            if (Math.Max(std.Val0, Math.Max(std.Val1, std.Val2)) < options.EmptyStdDev)
                return new SlotResult(slot, SlotState.Empty, null, null, 0, 0, null);
        }

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
                var label = r.State is SlotState.Recognized or SlotState.Uncertain
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
