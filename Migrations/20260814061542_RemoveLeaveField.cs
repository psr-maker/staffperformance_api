using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLeaveField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversionReason",
                table: "LeaveForm");

            migrationBuilder.DropColumn(
                name: "CompensationLeaveId",
                table: "ExtraWork");

            migrationBuilder.AddColumn<int>(
                name: "CompensationExtraWorkId",
                table: "LeaveForm",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompensationExtraWorkId",
                table: "LeaveForm");

            migrationBuilder.AddColumn<string>(
                name: "ConversionReason",
                table: "LeaveForm",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CompensationLeaveId",
                table: "ExtraWork",
                type: "int",
                nullable: true);
        }
    }
}
