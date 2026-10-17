using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using AbilityDraftOverlay.Models;

namespace AbilityDraftOverlay.Services;

public readonly record struct SearchHit(string? Id, float Score, string? RunnerUpId, float RunnerUpScore);

/// <summary>Descriptors of all icons of one kind. Several rows may share an id (modern/legacy variants).</summary>
public sealed class IconBank
{
    public static IconBank Empty { get; } = new(Array.Empty<string>(), Array.Empty<float>());

    public IconBank(string[] ids, float[] data)
    {
        Ids = ids;
        Data = data;
    }

    public string[] Ids { get; }
    /// <summary>Row-major matrix Count x IconIndex.Dim of L2-normalized, zero-mean descriptors.</summary>
    public float[] Data { get; }
    public int Count => Ids.Length;

    /// <summary>Brute-force cosine search. ~2k rows x 1.7k dims is well under a millisecond with SIMD.</summary>
    public SearchHit Search(ReadOnlySpan<float> query)
    {
        if (Count == 0) return new SearchHit(null, 0f, null, -1f);
        var scores = new float[Count];
        var best = 0;
        for (var i = 0; i < Count; i++)
        {
            scores[i] = Dot(Data.AsSpan(i * IconIndex.Dim, IconIndex.Dim), query);
            if (scores[i] > scores[best]) best = i;
        }

        // Runner-up must be a *different* icon, otherwise variants of the same icon would look ambiguous.
        var bestId = Ids[best];
        var runner = -1;
        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(Ids[i], bestId, StringComparison.Ordinal)) continue;
            if (runner < 0 || scores[i] > scores[runner]) runner = i;
        }
        return new SearchHit(bestId, scores[best], runner >= 0 ? Ids[runner] : null, runner >= 0 ? scores[runner] : -1f);
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var width = Vector<float>.Count;
        var acc = Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - width; i += width)
            acc += new Vector<float>(a.Slice(i, width)) * new Vector<float>(b.Slice(i, width));
        var sum = Vector.Sum(acc);
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}

/// <summary>
/// Precomputed index of hero and ability icons. Every icon is reduced once to a tiny normalized color
/// descriptor; recognition then only compares a few dozen small crops against this index instead of
/// sliding thousands of templates over the whole 1920x1080 frame.
/// The index is cached on disk and rebuilt only when the downloaded assets change.
/// </summary>
public sealed class IconIndex
{
    public const int Side = 24;
    public const int Dim = Side * Side * 3;

    private const int CacheVersion = 1;
    private static readonly string[] ExcludedAbilityPrefixes = { "special_bonus", "generic_hidden", "attribute_bonus" };

    private readonly ILogger<IconIndex> _logger;
    private readonly object _gate = new();
    private Dictionary<string, string> _heroNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _abilityNames = new(StringComparer.OrdinalIgnoreCase);

    public IconIndex(ILogger<IconIndex> logger) => _logger = logger;

    public IconBank Heroes { get; private set; } = IconBank.Empty;
    public IconBank Abilities { get; private set; } = IconBank.Empty;
    public bool IsLoaded { get; private set; }

    /// <summary>Forces a reload on next use (call after new assets were downloaded).</summary>
    public void Invalidate() { lock (_gate) IsLoaded = false; }

    public string GetDisplayName(SlotType type, string id)
    {
        var names = type == SlotType.Hero ? _heroNames : _abilityNames;
        return names.TryGetValue(id, out var name) ? name : id;
    }

    public void EnsureLoaded()
    {
        lock (_gate)
        {
            if (IsLoaded) return;
            if (!Directory.Exists(AppPaths.Assets))
                throw new DirectoryNotFoundException($"Каталог ресурсов не найден: {AppPaths.Assets}. Сначала загрузите ресурсы.");

            var heroFiles = EnumerateHeroFiles().ToArray();
            var abilityFiles = EnumerateAbilityFiles().ToArray();
            if (heroFiles.Length == 0 && abilityFiles.Length == 0)
                throw new InvalidOperationException("В каталоге ресурсов нет PNG-иконок героев или способностей.");

            var fingerprint = Fingerprint(heroFiles.Concat(abilityFiles));
            var cachePath = Path.Combine(AppPaths.Cache, "icons.index");
            if (!TryLoadCache(cachePath, fingerprint))
            {
                _logger.LogInformation("Построение индекса иконок: героев {Heroes}, способностей {Abilities} файлов", heroFiles.Length, abilityFiles.Length);
                var started = System.Diagnostics.Stopwatch.StartNew();
                Heroes = BuildBank(heroFiles);
                Abilities = BuildBank(abilityFiles);
                _logger.LogInformation("Индекс построен за {Elapsed} мс: героев {H}, способностей {A}", started.ElapsedMilliseconds, Heroes.Count, Abilities.Count);
                TrySaveCache(cachePath, fingerprint);
            }

            LoadNames();
            IsLoaded = true;
        }
    }

    /// <summary>Turns a BGR image into a zero-mean, unit-length vector; null for flat images.</summary>
    public static float[]? ComputeDescriptor(Mat bgr)
    {
        if (bgr.Channels() != 3) throw new ArgumentException("Ожидается 3-канальное BGR-изображение.", nameof(bgr));
        using var small = new Mat();
        Cv2.Resize(bgr, small, new OpenCvSharp.Size(Side, Side), 0, 0, InterpolationFlags.Area);
        using var floats = new Mat();
        small.ConvertTo(floats, MatType.CV_32FC3);

        var vector = new float[Dim];
        Marshal.Copy(floats.Data, vector, 0, Dim);

        var mean = 0f;
        foreach (var v in vector) mean += v;
        mean /= Dim;

        var norm = 0f;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] -= mean;
            norm += vector[i] * vector[i];
        }
        norm = MathF.Sqrt(norm);
        if (norm < 5f) return null;

        for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        return vector;
    }

    private static IEnumerable<(string Id, string Path)> EnumerateHeroFiles()
    {
        // Both portraits and small icons are used as variants of the same hero.
        foreach (var folder in new[] { "portraits", "icons" })
        {
            var dir = Path.Combine(AppPaths.Assets, "heroes", folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
                yield return (Path.GetFileNameWithoutExtension(file), file);
        }
    }

    private static IEnumerable<(string Id, string Path)> EnumerateAbilityFiles()
    {
        var modern = Path.Combine(AppPaths.Assets, "abilities", "modern");
        if (Directory.Exists(modern))
            foreach (var file in Directory.EnumerateFiles(modern, "*.png"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!IsExcludedAbility(id)) yield return (id, file);
            }

        // The in-game draft may still render the older artwork, so keep it as a second variant of the same id.
        var legacy = Path.Combine(AppPaths.Assets, "abilities", "legacy");
        if (Directory.Exists(legacy))
            foreach (var file in Directory.EnumerateFiles(legacy, "*_lg.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var id = name[..^"_lg".Length];
                if (!IsExcludedAbility(id)) yield return (id, file);
            }
    }

    private static bool IsExcludedAbility(string id) =>
        ExcludedAbilityPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IconBank BuildBank((string Id, string Path)[] files)
    {
        var rows = new float[]?[files.Length];
        Parallel.For(0, files.Length, i =>
        {
            try
            {
                using var image = Cv2.ImRead(files[i].Path, ImreadModes.Color);
                if (image.Empty() || image.Width < 12 || image.Height < 12) return;
                rows[i] = ComputeDescriptor(image);
            }
            catch (OpenCVException)
            {
                // Unreadable file: skip it, the rest of the index is still usable.
            }
        });

        var count = rows.Count(r => r is not null);
        var ids = new string[count];
        var data = new float[count * Dim];
        var next = 0;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i] is not { } row) continue;
            ids[next] = files[i].Id;
            Array.Copy(row, 0, data, next * Dim, Dim);
            next++;
        }
        return new IconBank(ids, data);
    }

    private static string Fingerprint(IEnumerable<(string Id, string Path)> files)
    {
        long count = 0, bytes = 0, newest = 0;
        foreach (var (_, path) in files)
        {
            var info = new FileInfo(path);
            count++;
            bytes += info.Length;
            newest = Math.Max(newest, info.LastWriteTimeUtc.Ticks);
        }
        return $"{count}:{bytes}:{newest}";
    }

    private bool TryLoadCache(string path, string fingerprint)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadInt32() != CacheVersion || reader.ReadInt32() != Side) return false;
            if (reader.ReadString() != fingerprint) return false;
            var heroes = ReadBank(reader);
            var abilities = ReadBank(reader);
            Heroes = heroes;
            Abilities = abilities;
            _logger.LogInformation("Индекс иконок загружен из кэша: героев {H}, способностей {A}", heroes.Count, abilities.Count);
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Кэш индекса повреждён, будет перестроен");
            return false;
        }
    }

    private void TrySaveCache(string path, string fingerprint)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp)))
            {
                writer.Write(CacheVersion);
                writer.Write(Side);
                writer.Write(fingerprint);
                WriteBank(writer, Heroes);
                WriteBank(writer, Abilities);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить кэш индекса: {Path}", path);
        }
    }

    private static void WriteBank(BinaryWriter writer, IconBank bank)
    {
        writer.Write(bank.Count);
        foreach (var id in bank.Ids) writer.Write(id);
        writer.Write(MemoryMarshal.AsBytes(bank.Data.AsSpan()));
    }

    private static IconBank ReadBank(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("Некорректный размер банка иконок.");
        var ids = new string[count];
        for (var i = 0; i < count; i++) ids[i] = reader.ReadString();
        var bytes = reader.ReadBytes(count * Dim * sizeof(float));
        if (bytes.Length != count * Dim * sizeof(float)) throw new EndOfStreamException();
        var data = new float[count * Dim];
        Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        return new IconBank(ids, data);
    }

    private void LoadNames()
    {
        var heroNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var abilityNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var heroesPath = Path.Combine(AppPaths.Assets, "data", "heroes.json");
            if (File.Exists(heroesPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(heroesPath));
                foreach (var hero in doc.RootElement.EnumerateObject())
                {
                    if (!hero.Value.TryGetProperty("name", out var internalName)) continue;
                    var shortName = internalName.GetString()?.Replace("npc_dota_hero_", "", StringComparison.Ordinal);
                    if (string.IsNullOrEmpty(shortName)) continue;
                    if (hero.Value.TryGetProperty("localized_name", out var localized) && localized.GetString() is { } text)
                        heroNames[shortName] = text;
                }
            }

            var abilitiesPath = Path.Combine(AppPaths.Assets, "data", "abilities.json");
            if (File.Exists(abilitiesPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(abilitiesPath));
                foreach (var ability in doc.RootElement.EnumerateObject())
                    if (ability.Value.ValueKind == JsonValueKind.Object
                        && ability.Value.TryGetProperty("dname", out var dname) && dname.GetString() is { Length: > 0 } text)
                        abilityNames[ability.Name] = text;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Не удалось прочитать названия из справочников, будут показаны идентификаторы");
        }
        _heroNames = heroNames;
        _abilityNames = abilityNames;
    }
}
