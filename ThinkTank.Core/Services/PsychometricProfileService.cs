using System.Collections.Concurrent;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

/// <summary>
/// Bridges ThinkTank to MindAttic.Legion's psychometric layer. Resolves the latest
/// <see cref="PsychometricProfile"/> for a persona from Legion's file-backed
/// <see cref="PersonaStore"/> (one JSON file per persona under
/// <c>%APPDATA%/MindAttic/Legion/personas/</c>, populated by
/// <c>legion.exe psychometrics score</c>) and renders it into a behavioral brief via
/// <see cref="PsychometricNarrator"/>.
///
/// <para>Profiles are immutable for the life of a session, so both the resolved profile
/// and its rendered prose are cached per persona id (including negative results — a
/// persona with no scored profile caches an empty brief, so we never re-hit disk for it).</para>
///
/// <para>All lookups degrade gracefully: an unknown persona id, an unscored persona, a
/// missing store directory, or a malformed file all yield <c>null</c>/empty rather than
/// throwing — the participant simply runs with its plain personality prompt.</para>
/// </summary>
public class PsychometricProfileService
{
    private readonly PersonaStore store;
    private readonly ConcurrentDictionary<string, string> briefCache = new(StringComparer.Ordinal);
    // Caches the resolved profile per persona id, including negative results (a null value
    // is a cached "no profile" — ConcurrentDictionary permits null values), so unscored
    // personas are never re-read from disk on subsequent votes / HasProfile checks.
    private readonly ConcurrentDictionary<string, PsychometricProfile?> profileCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the service over a <see cref="PersonaStore"/>. Pass <paramref name="storeDir"/>
    /// to point at a specific store (used by tests); leave null to use Legion's default
    /// resolution (the <c>MINDATTIC_LEGION_STORE</c> env var, then the roaming MindAttic bucket).
    /// </summary>
    public PsychometricProfileService(string? storeDir = null)
        : this(new PersonaStore(storeDir)) { }

    /// <summary>Creates the service over a caller-supplied store (used by tests).</summary>
    public PsychometricProfileService(PersonaStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// The most recent psychometric profile for <paramref name="personaId"/>, or <c>null</c>
    /// when the id is blank, the persona isn't in the store, or it has no assessments.
    /// </summary>
    public PsychometricProfile? GetProfile(string? personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId)) return null;
        return profileCache.GetOrAdd(personaId, id =>
        {
            try
            {
                return store.LatestProfile(id);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ThinkTank.Psychometrics] failed to read profile for '{id}': {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// The behavioral brief to append to this persona's system prompt — the prose rendering
    /// of its profile — or an empty string when no profile is available. Cached per persona id.
    /// </summary>
    public string DescribeForPrompt(string? personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId)) return "";
        return briefCache.GetOrAdd(personaId, id => PsychometricNarrator.Describe(GetProfile(id)));
    }

    /// <summary>True when <paramref name="personaId"/> has a scored profile in the store.</summary>
    public bool HasProfile(string? personaId) => GetProfile(personaId) is not null;
}
