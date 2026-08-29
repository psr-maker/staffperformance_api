using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffIdToTaskReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StaffId",
                table: "TaskReview",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaffId",
                table: "TaskReview");
        }
    }
}
