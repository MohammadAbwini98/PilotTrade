using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PilotTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Adapter",
                table: "OrderJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "OrderJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Size",
                table: "OrderJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderStateTransitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandId = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    FromState = table.Column<string>(type: "TEXT", nullable: true),
                    ToState = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Adapter = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStateTransitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderJournals_Environment_AccountId",
                table: "OrderJournals",
                columns: new[] { "Environment", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStateTransitions_CommandId",
                table: "OrderStateTransitions",
                column: "CommandId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderStateTransitions");

            migrationBuilder.DropIndex(
                name: "IX_OrderJournals_Environment_AccountId",
                table: "OrderJournals");

            migrationBuilder.DropColumn(
                name: "Adapter",
                table: "OrderJournals");

            migrationBuilder.DropColumn(
                name: "Side",
                table: "OrderJournals");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "OrderJournals");
        }
    }
}
