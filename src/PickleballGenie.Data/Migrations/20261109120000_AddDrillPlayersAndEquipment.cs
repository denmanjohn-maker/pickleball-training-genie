using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballGenie.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDrillPlayersAndEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Equipment",
                table: "Drills",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPlayers",
                table: "Drills",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Equipment",
                table: "Drills");

            migrationBuilder.DropColumn(
                name: "MinPlayers",
                table: "Drills");
        }
    }
}
