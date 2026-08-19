using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPenaltyAndOvertimeAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OvertimeAdjustment",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TaskPenaltyPoints",
                table: "MonthlyProductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeAdjustment",
                table: "MonthlyProductivity");

            migrationBuilder.DropColumn(
                name: "TaskPenaltyPoints",
                table: "MonthlyProductivity");
        }
    }
}
