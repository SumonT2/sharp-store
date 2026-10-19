namespace Shop.Web.Infrastructure;

/// <summary>
/// Background presets for home page sections: the banner (Shop:Hero:Background), the promo tile (Shop:Promo:Background),
/// the pre-order tile (Shop:DealTileBackground) and the deals band (Shop:DealsBackground). The look of each lives in
/// site.css (.bg-{key}); artwork presets also draw shapes with _BgPattern.
/// </summary>
public static class BackgroundPresets
{
    /// <param name="OnColor">Strong or dark colour behind the text, so the text is white.</param>
    /// <param name="OnLight">Bright colour in both themes, so the text stays dark even in the dark theme.</param>
    public sealed record Preset(string Key, string Name, bool OnColor = false, bool OnLight = false)
    {
        /// <summary>Classes to add to the section.</summary>
        public string Classes => $"bg-preset bg-{Key}{(OnColor ? " on-color" : "")}{(OnLight ? " on-light" : "")}";
    }

    /// <summary>Banner only: the banner picture fills the whole background instead of a preset.</summary>
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
    ];

    /// <summary>The named preset, or null when the name is empty, "none" or unknown (the section keeps its plain look).</summary>
    public static Preset? Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
