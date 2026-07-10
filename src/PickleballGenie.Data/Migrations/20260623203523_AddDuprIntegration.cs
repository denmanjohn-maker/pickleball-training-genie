using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballGenie.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDuprIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentDUPR",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<decimal>(
                name: "DoublesDUPR",
                table: "AspNetUsers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuprAccountId",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDuprLinked",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SinglesDUPR",
                table: "AspNetUsers",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoublesDUPR",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DuprAccountId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsDuprLinked",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SinglesDUPR",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentDUPR",
                table: "AspNetUsers",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
