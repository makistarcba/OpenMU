using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestPeriodAndResetGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaximumResets",
                schema: "config",
                table: "QuestDefinition",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumResets",
                schema: "config",
                table: "QuestDefinition",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                schema: "config",
                table: "QuestDefinition",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFinishedUtc",
                schema: "data",
                table: "CharacterQuestState",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaximumResets",
                schema: "config",
                table: "QuestDefinition");

            migrationBuilder.DropColumn(
                name: "MinimumResets",
                schema: "config",
                table: "QuestDefinition");

            migrationBuilder.DropColumn(
                name: "Period",
                schema: "config",
                table: "QuestDefinition");

            migrationBuilder.DropColumn(
                name: "LastFinishedUtc",
                schema: "data",
                table: "CharacterQuestState");
        }
    }
}
