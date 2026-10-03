using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Tasks_TaskCode",
                table: "Tasks",
                column: "TaskCode");

            migrationBuilder.CreateIndex(
                name: "IX_TaskQuantitySplit_TaskCode",
                table: "TaskQuantitySplit",
                column: "TaskCode");

            migrationBuilder.CreateIndex(
                name: "IX_TaskMembers_TaskCode",
                table: "TaskMembers",
                column: "TaskCode");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskMembers_Tasks_TaskCode",
                table: "TaskMembers",
                column: "TaskCode",
                principalTable: "Tasks",
                principalColumn: "TaskCode",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskQuantitySplit_Tasks_TaskCode",
                table: "TaskQuantitySplit",
                column: "TaskCode",
                principalTable: "Tasks",
                principalColumn: "TaskCode",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskMembers_Tasks_TaskCode",
                table: "TaskMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskQuantitySplit_Tasks_TaskCode",
                table: "TaskQuantitySplit");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tasks_TaskCode",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskQuantitySplit_TaskCode",
                table: "TaskQuantitySplit");

            migrationBuilder.DropIndex(
                name: "IX_TaskMembers_TaskCode",
                table: "TaskMembers");
        }
    }
}
