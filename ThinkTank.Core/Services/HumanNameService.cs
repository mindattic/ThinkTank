namespace LLMThinkTank.Core.Services;

/// <summary>
/// Provides offline random human name generation from a curated pool of 30 gender-neutral
/// first names. Used as a fast, no-API-call alternative to <see cref="NameGeneratorService"/>
/// for assigning humanized display names to AI roundtable participants.
/// </summary>
public class HumanNameService
{
    /// <summary>Pool of 30 gender-neutral first names for random selection.</summary>
    private static readonly string[] FirstNames =
    [
        "Avery", "Blake", "Casey", "Drew", "Elliot", "Emerson", "Finley", "Harper", "Hayden", "Jordan",
        "Kai", "Logan", "Morgan", "Parker", "Quinn", "Reese", "Riley", "Rowan", "Sawyer", "Skyler",
        "Sydney", "Taylor", "Teagan", "Dakota", "Cameron", "Jules", "Sasha", "Robin", "Jamie", "Alex"
    ];

    private readonly Random rng = new();

    /// <summary>Returns a random first name from the pool.</summary>
    public string NextFirstName() => FirstNames[rng.Next(FirstNames.Length)];

    /// <summary>
    /// Returns a display name combining a random first name with the LLM name,
    /// e.g., "Jordan (ChatGPT)" or "Quinn (Claude)".
    /// </summary>
    /// <param name="llmName">The LLM model's display name to append in parentheses.</param>
    public string NextDisplayName(string llmName) => $"{NextFirstName()} ({llmName})";
}
