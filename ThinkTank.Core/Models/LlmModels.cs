namespace LLMThinkTank.Core.Models;

/// <summary>
/// Defines a supported LLM provider with its default identity and personality.
/// Each model entry serves as a blueprint for roundtable participants, providing
/// the provider-specific personality prompt, display avatar, and API key registration URL.
/// </summary>
public class LlmModel
{
    /// <summary>Provider identifier used for API dispatch routing (e.g., "openai", "claude").</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-friendly display name shown in the UI (e.g., "ChatGPT", "Gemini").</summary>
    public string Name { get; init; } = "";

    /// <summary>Unicode character or emoji used as the model's visual avatar in the chat UI.</summary>
    public string Avatar { get; init; } = "";

    /// <summary>Default system prompt that establishes the model's roundtable persona and response style.</summary>
    public string Personality { get; init; } = "";

    /// <summary>URL where users can obtain or manage their API key for this provider.</summary>
    public string ApiKeyUrl { get; init; } = "";
}

/// <summary>
/// Represents a single turn in the shared conversation history passed to each LLM provider.
/// All participants see the same sequence of <see cref="SharedTurn"/> objects, allowing
/// each model to read and respond to what other AI systems have said.
/// </summary>
public class SharedTurn
{
    /// <summary>Provider identifier of the model that produced this turn.</summary>
    public string ModelId { get; set; } = "";

    /// <summary>Display name of the model (e.g., "Claude"), used as a speaker label in prompts.</summary>
    public string ModelName { get; set; } = "";

    /// <summary>The text content of the model's response.</summary>
    public string Text { get; set; } = "";

    /// <summary>Zero-based discussion round number this turn belongs to.</summary>
    public int Round { get; set; }
}

/// <summary>
/// Represents a message displayed in the conversation UI, including both successful
/// responses and error messages from failed API calls.
/// </summary>
public class ConversationMessage
{
    /// <summary>Provider identifier of the model that generated this message.</summary>
    public string ModelId { get; set; } = "";

    /// <summary>The message text content, or error description if <see cref="IsError"/> is true.</summary>
    public string Text { get; set; } = "";

    /// <summary>Zero-based discussion round this message belongs to.</summary>
    public int Round { get; set; }

    /// <summary>Indicates whether this message represents a failed API call.</summary>
    public bool IsError { get; set; }
}
