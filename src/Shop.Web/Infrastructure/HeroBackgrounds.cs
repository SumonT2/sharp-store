namespace Shop.Web.Infrastructure;

/// <summary>
/// Background presets for the home page banner (Shop:Hero:Background). The look of each lives in site.css
/// (.hero-bg-{key}); "photo" uses the banner picture as the whole background instead.
/// </summary>
public static class HeroBackgrounds
{
    /// <param name="OnColor">Strong or dark colour behind the text, so the text is white.</param>
    /// <param name="OnLight">Bright colour in both themes, so the text stays dark even in the dark theme.</param>
    public sealed record Preset(string Key, string Name, bool OnColor = false, bool OnLight = false);

    public const string Photo = "photo";

    public static readonly IReadOnlyList<Preset> All =
    [
        new("accent", "Accent glow (follows the visitor's colour)"),
        new("waves", "Waves (follows the visitor's colour)"),
        new("geometric", "Geometric diamonds (follows the visitor's colour)"),
        new("swoosh", "Corner swoosh (follows the visitor's colour)"),
        new("watercolor", "Watercolour splash"),
        new("halftone", "Sunny halftone", OnLight: true),
        new("sky", "Sky"),
        new("mint", "Mint"),
        new("peach", "Peach"),
        new("lavender", "Lavender"),
        new("aurora", "Aurora (blue to teal)", OnColor: true),
        new("sunset", "Sunset (orange to violet)", OnColor: true),
        new("streaks", "Neon streaks", OnColor: true),
        new("network", "Hexagon network", OnColor: true),
        new("midnight", "Midnight", OnColor: true),
        new(Photo, "Photo (the banner picture fills the background)"),
    ];

    /// <summary>The named preset, or the first one when the name is empty or unknown.</summary>
    public static Preset Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
