using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMonthlyProductivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveAdjustment",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "PermissionAdjustment",
                table: "MonthlyProductivity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LeaveAdjustment",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PermissionAdjustment",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
