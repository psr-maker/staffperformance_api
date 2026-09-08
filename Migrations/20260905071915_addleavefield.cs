using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class addleavefield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WorkedDate",
                table: "ExtraWork",
                newName: "WorkDate");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "ExtraWork",
                newName: "StaffId");

            migrationBuilder.RenameColumn(
                name: "TotalHours",
                table: "ExtraWork",
                newName: "ExpectedHours");

            migrationBuilder.RenameColumn(
                name: "ApprovedBy",
                table: "ExtraWork",
                newName: "VerifiedBy");

            migrationBuilder.AddColumn<int>(
                name: "ManagerId",
                table: "ExtraWork",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ManagerRemarks",
                table: "ExtraWork",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StaffRemarks",
                table: "ExtraWork",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TaskId",
                table: "ExtraWork",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "ExtraWork");

            migrationBuilder.DropColumn(
                name: "ManagerRemarks",
                table: "ExtraWork");

            migrationBuilder.DropColumn(
                name: "StaffRemarks",
                table: "ExtraWork");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "ExtraWork");

            migrationBuilder.RenameColumn(
                name: "WorkDate",
                table: "ExtraWork",
                newName: "WorkedDate");

            migrationBuilder.RenameColumn(
                name: "VerifiedBy",
                table: "ExtraWork",
                newName: "ApprovedBy");

            migrationBuilder.RenameColumn(
                name: "StaffId",
                table: "ExtraWork",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "ExpectedHours",
                table: "ExtraWork",
                newName: "TotalHours");
        }
    }
}
