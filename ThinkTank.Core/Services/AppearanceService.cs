using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// Controls the application's visual appearance including color theme, control sizing,
/// spacing, and border styling. Acts as the runtime facade over persisted appearance
/// settings, providing clamped values and change notification for reactive UI updates.
/// Supports 18 themes ranging from standard dark/light to creative themes like Matrix,
/// Neon, Dracula, Aurora, and Mono.
/// </summary>
public class AppearanceService
{
    private readonly ThinkTankSettingsService settings;

    /// <summary>The active color theme applied to all UI components.</summary>
    public AppearanceMode Mode { get; private set; } = AppearanceMode.Dark;

    /// <summary>Height in pixels for buttons, inputs, and other interactive controls (28-60).</summary>
    public int ControlHeight { get; private set; } = 40;

    /// <summary>Spacing in pixels between UI grid elements (0-30).</summary>
    public int Gutter { get; private set; } = 10;

    /// <summary>Corner rounding radius in pixels for cards, panels, and buttons (0-24).</summary>
    public int BorderRadius { get; private set; } = 10;

    /// <summary>
    /// Initializes appearance from persisted settings, applying defaults for any missing values.
    /// </summary>
    public AppearanceService(ThinkTankSettingsService settings)
    {
        this.settings = settings;
        Mode = ParseMode(this.settings.AppearanceTheme) ?? AppearanceMode.Dark;
        ControlHeight = this.settings.ControlHeight ?? 40;
        Gutter = this.settings.Gutter ?? 10;
        BorderRadius = this.settings.BorderRadius ?? 10;
    }

    /// <summary>Raised when any appearance property changes, triggering UI re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// Switches the active color theme and persists the change. No-op if already set.
    /// </summary>
    public void SetMode(AppearanceMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;
        settings.SetAppearanceTheme(ToThemeValue(mode));
        Changed?.Invoke();
    }

    /// <summary>Sets the control height, clamped to 28-60px. No-op if unchanged.</summary>
    public void SetControlHeight(int height)
    {
        height = Math.Clamp(height, 28, 60);
        if (ControlHeight == height)
            return;

        ControlHeight = height;
        settings.SetControlHeight(height);
        Changed?.Invoke();
    }

    /// <summary>Sets the gutter spacing, clamped to 0-30px. No-op if unchanged.</summary>
    public void SetGutter(int px)
    {
        px = Math.Clamp(px, 0, 30);
        if (Gutter == px) return;
        Gutter = px;
        settings.SetGutter(px);
        Changed?.Invoke();
    }

    /// <summary>Sets the border radius, clamped to 0-24px. No-op if unchanged.</summary>
    public void SetBorderRadius(int px)
    {
        px = Math.Clamp(px, 0, 24);
        if (BorderRadius == px) return;
        BorderRadius = px;
        settings.SetBorderRadius(px);
        Changed?.Invoke();
    }

    /// <summary>Parses a theme string to its enum value. Returns <c>null</c> for unrecognized themes.</summary>
    /// <remarks>
    /// Matches every <see cref="AppearanceMode"/> member by name (case-insensitive) so newly
    /// added themes (Aurora, Ember, Ocean, Forest, Mono, …) round-trip through persistence
    /// instead of silently falling back to <see cref="AppearanceMode.Dark"/> on reload.
    /// The numeric guard rejects raw enum integers ("3") that <c>Enum.TryParse</c> would
    /// otherwise accept, since persisted themes are always the lowercase name.
    /// </remarks>
    private static AppearanceMode? ParseMode(string? theme)
    {
        var name = (theme ?? "").Trim();
        if (name.Length == 0 || char.IsDigit(name[0]))
            return null;
        return Enum.TryParse<AppearanceMode>(name, ignoreCase: true, out var mode) ? mode : null;
    }

    /// <summary>Converts an <see cref="AppearanceMode"/> enum value to its lowercase string for persistence and CSS class mapping.</summary>
    public static string ToThemeValue(AppearanceMode mode)
        => mode.ToString().ToLowerInvariant();
}
