using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMonthlyProductivityToDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiveSPoints",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "OvertimeAdjustment",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "WarrantyPoints",
                table: "MonthlyProductivity");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaskPoints",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaskPenaltyPoints",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "Productivity",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "PermissionAdjustment",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "LeaveAdjustment",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "GoalPoints",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<decimal>(
                name: "AttitudeScore",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalScore",
                table: "MonthlyProductivity",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttitudeScore",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "MonthlyProductivity");

            migrationBuilder.AlterColumn<int>(
                name: "TaskPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AlterColumn<int>(
                name: "TaskPenaltyPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AlterColumn<int>(
                name: "Productivity",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AlterColumn<int>(
                name: "PermissionAdjustment",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AlterColumn<int>(
                name: "LeaveAdjustment",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AlterColumn<int>(
                name: "GoalPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(65,30)");

            migrationBuilder.AddColumn<int>(
                name: "FiveSPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeAdjustment",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Progress",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WarrantyPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
