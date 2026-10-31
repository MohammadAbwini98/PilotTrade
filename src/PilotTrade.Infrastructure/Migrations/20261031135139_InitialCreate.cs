using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PilotTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: true),
                    InstrumentEpic = table.Column<string>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SensitiveRedacted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrokerEnvironments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    ApiBaseUri = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: true),
                    LiveExecutionEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerEnvironments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClaudeAnalyses",
                columns: table => new
                {
                    AnalysisId = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    SelectionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Signal = table.Column<string>(type: "TEXT", nullable: false),
                    MarketRegime = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProducedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaudeAnalyses", x => x.AnalysisId);
                });

            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    SelectionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StrategyId = table.Column<string>(type: "TEXT", nullable: false),
                    StrategyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Candidate = table.Column<string>(type: "TEXT", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", nullable: false),
                    ChecksJson = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClaudeAnalysisId = table.Column<string>(type: "TEXT", nullable: true),
                    JevEvaluationId = table.Column<string>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.DecisionId);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    KillSwitchEngaged = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    LiveArmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LiveArmedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SuspensionReason = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    AssetClass = table.Column<string>(type: "TEXT", nullable: false),
                    QuoteCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    PricePrecision = table.Column<int>(type: "INTEGER", nullable: false),
                    TickSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinDealSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDealSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    DealSizeStep = table.Column<decimal>(type: "TEXT", nullable: false),
                    RetrievedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => new { x.Epic, x.Environment, x.AccountId });
                });

            migrationBuilder.CreateTable(
                name: "InstrumentSelections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    SelectionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SelectedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstrumentSelections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JevEvaluations",
                columns: table => new
                {
                    EvaluationId = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", nullable: false),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProducedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JevEvaluations", x => x.EvaluationId);
                });

            migrationBuilder.CreateTable(
                name: "OrderIntents",
                columns: table => new
                {
                    IntentId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<decimal>(type: "TEXT", nullable: false),
                    LimitLevel = table.Column<decimal>(type: "TEXT", nullable: true),
                    StopLevel = table.Column<decimal>(type: "TEXT", nullable: true),
                    TakeProfitLevel = table.Column<decimal>(type: "TEXT", nullable: true),
                    ClientReference = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SelectionVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    StrategyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    BrokerDealReference = table.Column<string>(type: "TEXT", nullable: true),
                    BrokerDealId = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderIntents", x => x.IntentId);
                });

            migrationBuilder.CreateTable(
                name: "OrderJournals",
                columns: table => new
                {
                    CommandId = table.Column<string>(type: "TEXT", nullable: false),
                    IntentId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Epic = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    RequestDigest = table.Column<string>(type: "TEXT", nullable: false),
                    DecisionSnapshotId = table.Column<string>(type: "TEXT", nullable: true),
                    RiskAuthorizationJson = table.Column<string>(type: "TEXT", nullable: true),
                    BrokerDealReference = table.Column<string>(type: "TEXT", nullable: true),
                    BrokerDealId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConfirmedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderJournals", x => x.CommandId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BrokerEnvironments");

            migrationBuilder.DropTable(
                name: "ClaudeAnalyses");

            migrationBuilder.DropTable(
                name: "Decisions");

            migrationBuilder.DropTable(
                name: "ExecutionStates");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "InstrumentSelections");

            migrationBuilder.DropTable(
                name: "JevEvaluations");

            migrationBuilder.DropTable(
                name: "OrderIntents");

            migrationBuilder.DropTable(
                name: "OrderJournals");
        }
    }
}
