using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ThinkTank.Core.Data;

/// <summary>
/// One LLM call ThinkTank made (TT-T47): who, for which conversation, which provider and model, when, how long,
/// and how it ended. The row is written when the call starts (so it counts against the daily budget at once) and
/// completed when it returns. Legion 27 does not report token counts, so <see cref="TokensIn"/> and
/// <see cref="TokensOut"/> stay null until it does; the character counts are kept for a cost estimate meanwhile.
/// </summary>
public sealed class LlmUsage
{
    public long Id { get; set; }

    /// <summary>The signed-in account the call was made for.</summary>
    public string UserName { get; set; } = "";

    /// <summary>The conversation (chat id), when the call belongs to one.</summary>
    public string? ConversationId { get; set; }

    /// <summary>What the call was for: turn, title, topic, vote, persona, test.</summary>
    public string Kind { get; set; } = "";

    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public int? DurationMs { get; set; }
    public int PromptChars { get; set; }
    public int? ResponseChars { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }

    /// <summary>Null on success; otherwise the user-safe description and log reference (never the provider's raw text).</summary>
    public string? Error { get; set; }

    /// <summary>True when the caller stopped the call (Pause, Close, circuit ended) before it returned.</summary>
    public bool Cancelled { get; set; }
}

/// <summary>
/// ThinkTank's own tables in its Azure SQL database (schema <c>app</c>), apart from the auth tables (TT-T47).
/// Today: <see cref="LlmUsage"/>. Conversations and personas move here in plan item 1.1.
/// </summary>
public sealed class ThinkTankDbContext(DbContextOptions<ThinkTankDbContext> options) : DbContext(options)
{
    public const string Schema = "app";

    public DbSet<LlmUsage> LlmUsage => Set<LlmUsage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
        b.Entity<LlmUsage>(e =>
        {
            e.ToTable("LlmUsage");
            e.HasKey(u => u.Id);
            e.Property(u => u.UserName).HasMaxLength(256).IsRequired();
            e.Property(u => u.ConversationId).HasMaxLength(64);
            e.Property(u => u.Kind).HasMaxLength(32).IsRequired();
            e.Property(u => u.Provider).HasMaxLength(64).IsRequired();
            e.Property(u => u.Model).HasMaxLength(128).IsRequired();
            e.Property(u => u.Error).HasMaxLength(500);
            e.HasIndex(u => new { u.UserName, u.StartedUtc });
        });
    }

    /// <summary><c>UseSqlServer</c> with retries on transient Azure SQL failures; migration history in the app schema.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure();
            sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
        });
}

/// <summary>Design-time factory so <c>dotnet ef migrations add ... --context ThinkTankDbContext</c> works.</summary>
public sealed class ThinkTankDbContextFactory : IDesignTimeDbContextFactory<ThinkTankDbContext>
{
    public ThinkTankDbContext CreateDbContext(string[] args)
    {
        var connectionString = ThinkTankDatabase.Resolve(configuration: null, hosted: false)!;
        var options = new DbContextOptionsBuilder<ThinkTankDbContext>();
        ThinkTankDbContext.Configure(options, connectionString);
        return new ThinkTankDbContext(options.Options);
    }
}
