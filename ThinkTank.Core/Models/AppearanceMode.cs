namespace LLMThinkTank.Core.Models;

/// <summary>
/// Defines the available UI color themes for the application.
/// Each mode maps to a corresponding CSS theme class that controls all visual styling
/// including backgrounds, text colors, borders, and accent highlights.
/// </summary>
public enum AppearanceMode
{
    /// <summary>Default dark theme with neutral gray tones.</summary>
    Dark,
    /// <summary>Light theme with white backgrounds and dark text.</summary>
    Light,
    /// <summary>Pastel greens and soft pinks inspired by spring foliage.</summary>
    Spring,
    /// <summary>Warm yellows, oranges, and sky blues evoking summer sunlight.</summary>
    Summer,
    /// <summary>Rich ambers, burnt oranges, and deep reds for an autumnal palette.</summary>
    Autumn,
    /// <summary>Cool blues, silvers, and icy whites reminiscent of winter frost.</summary>
    Winter,
    /// <summary>Green-on-black terminal aesthetic inspired by The Matrix.</summary>
    Matrix,
    /// <summary>Crisp whites and pale cyan accents for a frozen, minimal look.</summary>
    Ice,
    /// <summary>Warm gradient from deep purple through orange to golden yellow.</summary>
    Sunset,
    /// <summary>Vibrant electric pinks, cyans, and purples on a dark background.</summary>
    Neon,
    /// <summary>Popular dark theme featuring purples, pinks, and soft greens.</summary>
    Dracula,
    /// <summary>Ethan Schoonover's precision color scheme with balanced contrast.</summary>
    Solarized,
    /// <summary>Deep navy and indigo tones for a late-night coding aesthetic.</summary>
    Midnight,
    /// <summary>Northern lights-inspired palette with teals, greens, and violet accents.</summary>
    Aurora,
    /// <summary>Warm dark theme with glowing orange and red ember tones.</summary>
    Ember,
    /// <summary>Deep sea blues and aqua greens for a calming underwater feel.</summary>
    Ocean,
    /// <summary>Natural greens and earthy browns evoking a woodland environment.</summary>
    Forest,
    /// <summary>Monochrome grayscale theme for distraction-free reading.</summary>
    Mono
}
