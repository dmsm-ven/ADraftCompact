using System.Reflection;

namespace ADraftCompact.Services;

/// <summary>Version of the running build (&lt;Version&gt; in the csproj).</summary>
public static class AppVersion
{
    public static Version Current { get; } = Parse(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0, 0);

    /// <summary>"0.1.0"</summary>
    public static string Text => Format(Current);

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary>Accepts "v0.1.0", "0.1.0+abcdef" and "0.1.0-beta"; null when unparseable.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('v', 'V');
        var end = s.IndexOfAny(['+', '-', ' ']);
        if (end >= 0) s = s[..end];
        return Version.TryParse(s, out var v) ? v : null;
    }
}
