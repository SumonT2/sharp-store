namespace Shop.Web.Infrastructure;

/// <summary>
/// Accent colours visitors can pick in the display settings. Each one is a single mid-tone; the stylesheet mixes
/// the light fills, outlines and text shades from it, so they work in the light and the dark theme.
/// </summary>
public static class AccentColors
{
    public sealed record Accent(string Key, string Name, string Hex);

    public static readonly IReadOnlyList<Accent> All =
    [
        new("ocean", "Ocean blue", "#2f7fd6"),
        new("emerald", "Emerald", "#1f9d6a"),
        new("teal", "Teal", "#139a97"),
        new("violet", "Violet", "#7b5ce0"),
        new("amber", "Amber", "#d9930f"),
        new("raspberry", "Raspberry", "#d6457a"),
        new("coral", "Coral", "#e2643f"),
    ];

    /// <summary>The configured default (Shop:Accent), or the first colour when it isn't one of the list.</summary>
    public static Accent Default(string? key) =>
        All.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
