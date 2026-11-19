using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThinkTank.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.CreateTable(
                name: "AuthAuditLog",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserNameAttempted = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EventType = table.Column<byte>(type: "tinyint", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    ReasonCode = table.Column<byte>(type: "tinyint", nullable: false),
                    AccountKeyHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: true),
                    SourceIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CaptchaPresented = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthLoginThrottles",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<byte>(type: "tinyint", nullable: false),
                    KeyHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    FirstFailureUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastFailureUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAllowedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthLoginThrottles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthUsers",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NormalizedDisplayName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PasswordPepperKeyId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    LegacyHashScheme = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PasswordUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MfaEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    MustEnrollMfa = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthPasswordHistory",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PepperKeyId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthPasswordHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthPasswordHistory_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthPasswordResetTokens",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    RequestUserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthPasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthPasswordResetTokens_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthRecoveryCodes",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CodePepperKeyId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthRecoveryCodes_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AbsoluteExpiryUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_AuthUsers_AuthUserId",
                        column: x => x.AuthUserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthUserMfa",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SecretEncrypted = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    PendingSecretEncrypted = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    PendingExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTotpStepUsed = table.Column<long>(type: "bigint", nullable: false),
                    ActivatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserMfa", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AuthUserMfa_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AuthUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_AccountKeyHash",
                schema: "auth",
                table: "AuthAuditLog",
                column: "AccountKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_TimestampUtc",
                schema: "auth",
                table: "AuthAuditLog",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthLoginThrottles_Scope_KeyHash",
                schema: "auth",
                table: "AuthLoginThrottles",
                columns: new[] { "Scope", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthPasswordHistory_UserId",
                schema: "auth",
                table: "AuthPasswordHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthPasswordResetTokens_TokenHash",
                schema: "auth",
                table: "AuthPasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthPasswordResetTokens_UserId_ConsumedUtc",
                schema: "auth",
                table: "AuthPasswordResetTokens",
                columns: new[] { "UserId", "ConsumedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthRecoveryCodes_UserId",
                schema: "auth",
                table: "AuthRecoveryCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_AuthUserId",
                schema: "auth",
                table: "AuthSessions",
                column: "AuthUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_NormalizedDisplayName",
                schema: "auth",
                table: "AuthUsers",
                column: "NormalizedDisplayName",
                unique: true,
                filter: "[NormalizedDisplayName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_NormalizedEmail",
                schema: "auth",
                table: "AuthUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_NormalizedUserName",
                schema: "auth",
                table: "AuthUsers",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthAuditLog",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthLoginThrottles",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthPasswordHistory",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthPasswordResetTokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthRecoveryCodes",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthSessions",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthUserMfa",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuthUsers",
                schema: "auth");
        }
    }
}
