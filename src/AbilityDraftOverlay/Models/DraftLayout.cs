using System.IO;

namespace AbilityDraftOverlay.Models;

/// <summary>Rectangle relative to the origin of a player's cell (reference resolution pixels).</summary>
public sealed class SlotRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

/// <summary>One team column of the draft screen. Slots may be overridden if the column is mirrored.</summary>
public sealed class TeamColumn
{
    public int Team { get; set; }
    /// <summary>Top-left corner of the first row's cell, in reference pixels.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public SlotRect? Hero { get; set; }
    /// <summary>Line with the hero name, relative to the cell origin.</summary>
    public SlotRect? HeroName { get; set; }
    public List<SlotRect>? Abilities { get; set; }
}

public sealed class MatchingOptions
{
    /// <summary>Minimal cosine similarity of the best candidate (0..1).</summary>
    public double MinScore { get; set; } = 0.55;
    /// <summary>Minimal gap between the best and the best *different* candidate.</summary>
    public double MinMargin { get; set; } = 0.10;
    /// <summary>Pixels trimmed from every side of the slot before the emptiness test (skips frame and rounded corners).</summary>
    public int EmptyInset { get; set; } = 6;
    /// <summary>An ability slot is empty when its center is almost black: mean brightness (0..255) below this.</summary>
    public double AbilityEmptyMaxMean { get; set; } = 35.0;
    /// <summary>The hero area is empty ("NO HERO" silhouette) when its per-channel std-dev is below this.</summary>
    public double HeroEmptyMaxStdDev { get; set; } = 40.0;
    /// <summary>Pixels trimmed from every side of a slot before matching (frames, borders, glow).</summary>
    public int Inset { get; set; } = 3;

    /// <summary>
    /// How heroes are recognized. "ocr": read the name line (Windows OCR) - the card art is animated, so it cannot be matched
    /// against static images; "image": compare the art with downloaded portraits (unreliable); "auto": ocr when available, else image.
    /// </summary>
    public string HeroSource { get; set; } = "auto";
    /// <summary>OCR language tag. The hero names are matched against English names, so the game must run in English.</summary>
    public string OcrLanguage { get; set; } = "en-US";
    /// <summary>Minimal similarity (0..1) between the OCR text and a hero name.</summary>
    public double HeroNameMinScore { get; set; } = 0.70;
    /// <summary>Minimal gap between the best and the second best hero name.</summary>
    public double HeroNameMinMargin { get; set; } = 0.12;
    /// <summary>The frame is the draft board if at least this many of the 10 cards show a hero name or "NO HERO".</summary>
    public int BoardMinCards { get; set; } = 8;
    /// <summary>The slot is also tried shifted by +-ShiftPx to tolerate small layout inaccuracies.</summary>
    public int ShiftPx { get; set; } = 2;
}

/// <summary>
/// Geometry of the Ability Draft screen. Player numbers follow the pick order:
/// row 0 col 0 = 1, row 0 col 1 = 2, row 1 col 0 = 3, ...
/// </summary>
public sealed class DraftLayout
{
    public int ReferenceWidth { get; set; } = 1920;
    public int ReferenceHeight { get; set; } = 1080;
    /// <summary>False while coordinates are only approximate; the UI warns about it.</summary>
    public bool Calibrated { get; set; }
    public int Rows { get; set; } = 5;
    public int RowPitch { get; set; }
    public List<TeamColumn> Columns { get; set; } = new();
    public SlotRect Hero { get; set; } = new();
    public SlotRect? HeroName { get; set; }
    public List<SlotRect> Abilities { get; set; } = new();
    public MatchingOptions Matching { get; set; } = new();

    public void Validate()
    {
        if (ReferenceWidth <= 0 || ReferenceHeight <= 0) throw new InvalidDataException("Layout: referenceWidth/referenceHeight must be > 0.");
        if (Rows <= 0) throw new InvalidDataException("Layout: rows must be > 0.");
        if (Columns.Count == 0) throw new InvalidDataException("Layout: at least one team column (columns) is required.");
        if (Hero.W <= 0 || Hero.H <= 0) throw new InvalidDataException("Layout: hero.w/hero.h must be > 0.");
        if (Abilities.Count == 0 && Columns.All(c => c.Abilities is null or { Count: 0 }))
            throw new InvalidDataException("Layout: no ability slots (abilities) are defined.");
    }

    /// <summary>Expands the compact description into concrete slots, scaled to the real screenshot size.</summary>
    public IReadOnlyList<SlotPlacement> Expand(int imageWidth, int imageHeight)
    {
        var sx = imageWidth / (double)ReferenceWidth;
        var sy = imageHeight / (double)ReferenceHeight;
        var result = new List<SlotPlacement>();
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                var column = Columns[col];
                var player = row * Columns.Count + col + 1;
                var originX = column.X;
                var originY = column.Y + row * RowPitch;

                var nameRect = column.HeroName ?? HeroName;
                result.Add(new SlotPlacement(player, column.Team, SlotType.Hero, 0,
                    Scale(column.Hero ?? Hero, originX, originY, sx, sy),
                    nameRect is null ? null : Scale(nameRect, originX, originY, sx, sy)));

                var abilities = column.Abilities ?? Abilities;
                for (var i = 0; i < abilities.Count; i++)
                    result.Add(new SlotPlacement(player, column.Team, SlotType.Ability, i,
                        Scale(abilities[i], originX, originY, sx, sy)));
            }
        }
        return result;
    }

    private static PixelRect Scale(SlotRect rect, int originX, int originY, double sx, double sy) => new(
        (int)Math.Round((originX + rect.X) * sx),
        (int)Math.Round((originY + rect.Y) * sy),
        Math.Max(1, (int)Math.Round(rect.W * sx)),
        Math.Max(1, (int)Math.Round(rect.H * sy)));
}
