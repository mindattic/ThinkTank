namespace LLMThinkTank.Core.Models;

/// <summary>
/// Stores the authentication and configuration JSON blob for a single LLM provider.
/// The <see cref="Json"/> payload typically contains <c>type</c>, <c>apiKey</c>,
/// <c>model</c>, and <c>maxTokens</c> fields that are parsed at call time
/// to configure HTTP requests to the provider's API.
/// </summary>
/// <param name="ProviderId">Provider identifier (e.g., "openai", "claude", "gemini").</param>
/// <param name="Json">Raw JSON string containing auth credentials and model configuration.</param>
public record ProviderAuthConfig(string ProviderId, string Json);
