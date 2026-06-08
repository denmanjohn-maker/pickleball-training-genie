using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballGenie.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstimatedDurationMinutes",
                table: "Drills",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "PreferredSessionDurationMinutes",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedDurationMinutes",
                table: "Drills");

            migrationBuilder.DropColumn(
                name: "PreferredSessionDurationMinutes",
                table: "AspNetUsers");
        }
    }
}
