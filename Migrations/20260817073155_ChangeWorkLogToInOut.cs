using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class ChangeWorkLogToInOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "WorkLog");

            migrationBuilder.DropColumn(
                name: "TotalHours",
                table: "WorkLog");

            migrationBuilder.RenameColumn(
                name: "SubDepartmentName",
                table: "WorkLog",
                newName: "WorkType");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "WorkLog",
                newName: "Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WorkType",
                table: "WorkLog",
                newName: "SubDepartmentName");

            migrationBuilder.RenameColumn(
                name: "Time",
                table: "WorkLog",
                newName: "StartTime");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "WorkLog",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "EndTime",
                table: "WorkLog",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "TotalHours",
                table: "WorkLog",
                type: "double",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
