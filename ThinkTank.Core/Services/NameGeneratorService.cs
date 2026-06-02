using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// Uses an LLM provider to generate realistic human first names for AI participants.
/// This gives each participant a humanized display name (e.g., "Alex (ChatGPT)")
/// rather than just the model name, making roundtable conversations feel more natural.
/// Falls back to "Alex" if the LLM returns an invalid or empty response.
/// </summary>
public class NameGeneratorService
{
    private readonly ThinkTankService thinkTank;
    private readonly ThinkTankSettingsService settings;

    /// <summary>
    /// Initializes a new instance with access to the LLM dispatch service and settings.
    /// </summary>
    public NameGeneratorService(ThinkTankService thinkTank, ThinkTankSettingsService settings)
    {
        this.thinkTank = thinkTank;
        this.settings = settings;
    }

    /// <summary>
    /// Asks the specified LLM provider to generate a single realistic human first name.
    /// The result is sanitized to contain only letters and capped at 32 characters.
    /// </summary>
    /// <param name="providerId">The LLM provider to use for generation.</param>
    /// <param name="authOverrideJson">Optional auth override, or <c>null</c> for provider defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A sanitized first name string, defaulting to "Alex" on failure.</returns>
    public async Task<string> GenerateFirstNameAsync(string providerId, string? authOverrideJson = null, CancellationToken cancellationToken = default)
    {
        var prompt = "Pick a single realistic first name for a human. Output ONLY the name, no punctuation, no quotes.";

        var personality = "You generate a single realistic human first name. Output only the name.";

        var history = new List<SharedTurn>();

        var name = await thinkTank.CallProvider(providerId, personality, authOverrideJson, prompt, history);

        // Take the first whitespace-delimited token so a chatty reply ("Sure, the name is Alex")
        // doesn't get its words run together, then strip non-letters and cap length. Order matters:
        // truncating the raw reply first could chop mid-sentence and concatenate garbage.
        name = (name ?? "").Trim();
        var firstToken = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        name = new string(firstToken.Where(char.IsLetter).ToArray());
        if (name.Length > 32)
            name = name[..32];
        if (string.IsNullOrWhiteSpace(name))
            name = "Alex";

        return name;
    }
}
