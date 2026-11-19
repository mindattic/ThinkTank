using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MindAttic.Authentication.Data;
using MindAttic.Authentication.Entities;

namespace ThinkTank.Core.Data;

/// <summary>
/// The MindAttic.Authentication tables (schema <c>auth</c>) in ThinkTank's Azure SQL database (TT-T46). Accounts,
/// sessions, TOTP enrolment, recovery codes, sign-in back-off and the audit log live here; passwords only as
/// Argon2id hashes over the shared Vault pepper. Migrations are in <c>Data/Migrations</c>.
/// </summary>
public sealed class ThinkTankAuthDbContext(DbContextOptions<ThinkTankAuthDbContext> options)
    : DbContext(options), IAuthDataContext
{
    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();
    public DbSet<AuthUserMfa> AuthUserMfa => Set<AuthUserMfa>();
    public DbSet<AuthRecoveryCode> AuthRecoveryCodes => Set<AuthRecoveryCode>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthLoginThrottle> AuthLoginThrottles => Set<AuthLoginThrottle>();
    public DbSet<AuthAuditLog> AuthAuditLog => Set<AuthAuditLog>();
    public DbSet<AuthPasswordHistory> AuthPasswordHistory => Set<AuthPasswordHistory>();
    public DbSet<AuthPasswordResetToken> AuthPasswordResetTokens => Set<AuthPasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyMindAtticAuthConfiguration();   // all 8 tables in the 'auth' schema
    }

    /// <summary><c>UseSqlServer</c> with retries on transient Azure SQL failures, as every ThinkTank context opens.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure();
            sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth");
        });
}

/// <summary>Design-time factory so <c>dotnet ef migrations add ... --context ThinkTankAuthDbContext</c> works.</summary>
public sealed class ThinkTankAuthDbContextFactory : IDesignTimeDbContextFactory<ThinkTankAuthDbContext>
{
    public ThinkTankAuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = ThinkTankDatabase.Resolve(configuration: null, hosted: false)!;
        var options = new DbContextOptionsBuilder<ThinkTankAuthDbContext>();
        ThinkTankAuthDbContext.Configure(options, connectionString);
        return new ThinkTankAuthDbContext(options.Options);
    }
}
