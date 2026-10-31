using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// The application's visual appearance (color theme, control sizing, spacing, border styling) as read
/// from ThinkTank's settings, clamped to their ranges. Edited only in MindAttic.Vault.Desktop (LAMP,
/// TT-T27); each read reflects the saved values, and the layout applies them on load.
/// </summary>
public class AppearanceService
{
    private readonly ThinkTankSettingsService settings;

    public AppearanceService(ThinkTankSettingsService settings)
    {
        this.settings = settings;
    }

    /// <summary>The active color theme (unrecognized names fall back to <see cref="AppearanceMode.Dark"/>).</summary>
    public AppearanceMode Mode => ParseMode(settings.AppearanceTheme) ?? AppearanceMode.Dark;

    /// <summary>Height in pixels for buttons, inputs, and other interactive controls (28-60).</summary>
    public int ControlHeight => Math.Clamp(settings.ControlHeight ?? 40, 28, 60);

    /// <summary>Spacing in pixels between UI grid elements (0-30).</summary>
    public int Gutter => Math.Clamp(settings.Gutter ?? 10, 0, 30);

    /// <summary>Corner rounding radius in pixels for cards, panels, and buttons (0-24).</summary>
    public int BorderRadius => Math.Clamp(settings.BorderRadius ?? 10, 0, 24);

    /// <summary>Parses a theme string to its enum value. Returns <c>null</c> for unrecognized themes.</summary>
    /// <remarks>
    /// Matches every <see cref="AppearanceMode"/> member by name (case-insensitive). The numeric guard
    /// rejects raw enum integers ("3") that <c>Enum.TryParse</c> would otherwise accept, since persisted
    /// themes are always the lowercase name.
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
