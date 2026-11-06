using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PilotTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKillSwitchReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KillSwitchChangedBy",
                table: "ExecutionStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KillSwitchChangedUtc",
                table: "ExecutionStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KillSwitchReason",
                table: "ExecutionStates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KillSwitchChangedBy",
                table: "ExecutionStates");

            migrationBuilder.DropColumn(
                name: "KillSwitchChangedUtc",
                table: "ExecutionStates");

            migrationBuilder.DropColumn(
                name: "KillSwitchReason",
                table: "ExecutionStates");
        }
    }
}
