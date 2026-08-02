using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballGenie.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDrillSelfRatingsAndSessionNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WorkoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelfRating",
                table: "WorkoutSessionDrills",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WorkoutSessions");

            migrationBuilder.DropColumn(
                name: "SelfRating",
                table: "WorkoutSessionDrills");
        }
    }
}
