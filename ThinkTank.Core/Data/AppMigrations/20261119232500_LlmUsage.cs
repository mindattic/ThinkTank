using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThinkTank.Core.Data.AppMigrations
{
    /// <inheritdoc />
    public partial class LlmUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "LlmUsage",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    PromptChars = table.Column<int>(type: "int", nullable: false),
                    ResponseChars = table.Column<int>(type: "int", nullable: true),
                    TokensIn = table.Column<int>(type: "int", nullable: true),
                    TokensOut = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Cancelled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmUsage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsage_UserName_StartedUtc",
                schema: "app",
                table: "LlmUsage",
                columns: new[] { "UserName", "StartedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmUsage",
                schema: "app");
        }
    }
}
