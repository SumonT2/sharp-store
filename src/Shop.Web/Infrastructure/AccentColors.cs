namespace Shop.Web.Infrastructure;

/// <summary>
/// Muted accent colours visitors can pick in the display settings. Each one is a single mid-tone; the stylesheet mixes
/// the light fills, outlines and text shades from it, so they work in the light and the dark theme.
/// </summary>
public static class AccentColors
{
    public sealed record Accent(string Key, string Name, string Hex);

    public static readonly IReadOnlyList<Accent> All =
    [
        new("terracotta", "Terracotta", "#c0714a"),
        new("sage", "Sage", "#6f8f6b"),
        new("slate", "Slate blue", "#5e7ea6"),
        new("teal", "Teal", "#4c8a88"),
        new("plum", "Plum", "#8b6f9e"),
        new("ochre", "Ochre", "#ad8a44"),
        new("rose", "Rose", "#b86b7a"),
    ];

    /// <summary>The configured default (Shop:Accent), or the first colour when it isn't one of the list.</summary>
    public static Accent Default(string? key) =>
        All.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
