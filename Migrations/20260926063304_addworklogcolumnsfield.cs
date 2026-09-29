using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class addworklogcolumnsfield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutImageUrl",
                table: "WorkLog",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "OutLatitude",
                table: "WorkLog",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutLocationName",
                table: "WorkLog",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "OutLongitude",
                table: "WorkLog",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutTime",
                table: "WorkLog",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutImageUrl",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "OutLatitude",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "OutLocationName",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "OutLongitude",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "OutTime",
                table: "WorkLog");
        }
    }
}
