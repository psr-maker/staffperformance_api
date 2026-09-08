using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class updateovertimefield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isApprov",
                table: "OverTime");

            migrationBuilder.AddColumn<string>(
                name: "ManagerResponseReason",
                table: "OverTime",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ManagerStatus",
                table: "OverTime",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "RequestedBy",
                table: "OverTime",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffResponseReason",
                table: "OverTime",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StaffStatus",
                table: "OverTime",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerResponseReason",
                table: "OverTime");

            migrationBuilder.DropColumn(
                name: "ManagerStatus",
                table: "OverTime");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "OverTime");

            migrationBuilder.DropColumn(
                name: "StaffResponseReason",
                table: "OverTime");

            migrationBuilder.DropColumn(
                name: "StaffStatus",
                table: "OverTime");

            migrationBuilder.AddColumn<bool>(
                name: "isApprov",
                table: "OverTime",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
