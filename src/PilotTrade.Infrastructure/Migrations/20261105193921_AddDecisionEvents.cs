using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PilotTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionEvents",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    SelectionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotAsOfUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ClaudeStatus = table.Column<string>(type: "TEXT", nullable: false),
                    JevStatus = table.Column<string>(type: "TEXT", nullable: false),
                    ClaudeJson = table.Column<string>(type: "TEXT", nullable: false),
                    JevJson = table.Column<string>(type: "TEXT", nullable: false),
                    PromptSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    PromptChars = table.Column<int>(type: "INTEGER", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    NoTradeReason = table.Column<string>(type: "TEXT", nullable: true),
                    RiskVeto = table.Column<string>(type: "TEXT", nullable: true),
                    StrategyId = table.Column<string>(type: "TEXT", nullable: false),
                    StrategyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    AuditEventId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionEvents", x => x.DecisionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionEvents_Environment_AccountId_RecordedTicks",
                table: "DecisionEvents",
                columns: new[] { "Environment", "AccountId", "RecordedTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionEvents");
        }
    }
}
