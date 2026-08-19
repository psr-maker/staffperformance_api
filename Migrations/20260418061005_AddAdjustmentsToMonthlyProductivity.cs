using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StaffWork_Track.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustmentsToMonthlyProductivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkLog",
                table: "WorkLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Warnings",
                table: "Warnings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfile",
                table: "UserProfile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tasks",
                table: "Tasks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TaskReview",
                table: "TaskReview");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TaskMembers",
                table: "TaskMembers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_QualityMetrics",
                table: "QualityMetrics");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PermissionForm",
                table: "PermissionForm");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MonthlyProductivity",
                table: "MonthlyProductivity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LeaveForm",
                table: "LeaveForm");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Goal",
                table: "Goal");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FiveSPoints",
                table: "FiveSPoints");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Departments",
                table: "Departments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Auditlog",
                table: "Auditlog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Announcements",
                table: "Announcements");

            migrationBuilder.RenameTable(
                name: "WorkLog",
                newName: "worklog");

            migrationBuilder.RenameTable(
                name: "Warnings",
                newName: "warnings");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "UserProfile",
                newName: "userprofile");

            migrationBuilder.RenameTable(
                name: "Tasks",
                newName: "tasks");

            migrationBuilder.RenameTable(
                name: "TaskReview",
                newName: "taskreview");

            migrationBuilder.RenameTable(
                name: "TaskMembers",
                newName: "taskmembers");

            migrationBuilder.RenameTable(
                name: "QualityMetrics",
                newName: "qualitymetrics");

            migrationBuilder.RenameTable(
                name: "PermissionForm",
                newName: "permissionform");

            migrationBuilder.RenameTable(
                name: "Notifications",
                newName: "notifications");

            migrationBuilder.RenameTable(
                name: "MonthlyProductivity",
                newName: "monthlyproductivity");

            migrationBuilder.RenameTable(
                name: "LeaveForm",
                newName: "leaveform");

            migrationBuilder.RenameTable(
                name: "Goal",
                newName: "goal");

            migrationBuilder.RenameTable(
                name: "FiveSPoints",
                newName: "fivespoints");

            migrationBuilder.RenameTable(
                name: "Departments",
                newName: "departments");

            migrationBuilder.RenameTable(
                name: "Auditlog",
                newName: "auditlog");

            migrationBuilder.RenameTable(
                name: "Announcements",
                newName: "announcements");

            migrationBuilder.AddColumn<int>(
                name: "LeaveAdjustment",
                table: "monthlyproductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionAdjustment",
                table: "monthlyproductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Productivity",
                table: "monthlyproductivity",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_worklog",
                table: "worklog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_warnings",
                table: "warnings",
                column: "WarningId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_userprofile",
                table: "userprofile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_tasks",
                table: "tasks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_taskreview",
                table: "taskreview",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_taskmembers",
                table: "taskmembers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_qualitymetrics",
                table: "qualitymetrics",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_permissionform",
                table: "permissionform",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notifications",
                table: "notifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_monthlyproductivity",
                table: "monthlyproductivity",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_leaveform",
                table: "leaveform",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_goal",
                table: "goal",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fivespoints",
                table: "fivespoints",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_departments",
                table: "departments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auditlog",
                table: "auditlog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_announcements",
                table: "announcements",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_worklog",
                table: "worklog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_warnings",
                table: "warnings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_userprofile",
                table: "userprofile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_tasks",
                table: "tasks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_taskreview",
                table: "taskreview");

            migrationBuilder.DropPrimaryKey(
                name: "PK_taskmembers",
                table: "taskmembers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_qualitymetrics",
                table: "qualitymetrics");

            migrationBuilder.DropPrimaryKey(
                name: "PK_permissionform",
                table: "permissionform");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notifications",
                table: "notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_monthlyproductivity",
                table: "monthlyproductivity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_leaveform",
                table: "leaveform");

            migrationBuilder.DropPrimaryKey(
                name: "PK_goal",
                table: "goal");

            migrationBuilder.DropPrimaryKey(
                name: "PK_fivespoints",
                table: "fivespoints");

            migrationBuilder.DropPrimaryKey(
                name: "PK_departments",
                table: "departments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auditlog",
                table: "auditlog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_announcements",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "LeaveAdjustment",
                table: "monthlyproductivity");

            migrationBuilder.DropColumn(
                name: "PermissionAdjustment",
                table: "monthlyproductivity");

            migrationBuilder.DropColumn(
                name: "Productivity",
                table: "monthlyproductivity");

            migrationBuilder.RenameTable(
                name: "worklog",
                newName: "WorkLog");

            migrationBuilder.RenameTable(
                name: "warnings",
                newName: "Warnings");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "userprofile",
                newName: "UserProfile");

            migrationBuilder.RenameTable(
                name: "tasks",
                newName: "Tasks");

            migrationBuilder.RenameTable(
                name: "taskreview",
                newName: "TaskReview");

            migrationBuilder.RenameTable(
                name: "taskmembers",
                newName: "TaskMembers");

            migrationBuilder.RenameTable(
                name: "qualitymetrics",
                newName: "QualityMetrics");

            migrationBuilder.RenameTable(
                name: "permissionform",
                newName: "PermissionForm");

            migrationBuilder.RenameTable(
                name: "notifications",
                newName: "Notifications");

            migrationBuilder.RenameTable(
                name: "monthlyproductivity",
                newName: "MonthlyProductivity");

            migrationBuilder.RenameTable(
                name: "leaveform",
                newName: "LeaveForm");

            migrationBuilder.RenameTable(
                name: "goal",
                newName: "Goal");

            migrationBuilder.RenameTable(
                name: "fivespoints",
                newName: "FiveSPoints");

            migrationBuilder.RenameTable(
                name: "departments",
                newName: "Departments");

            migrationBuilder.RenameTable(
                name: "auditlog",
                newName: "Auditlog");

            migrationBuilder.RenameTable(
                name: "announcements",
                newName: "Announcements");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkLog",
                table: "WorkLog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Warnings",
                table: "Warnings",
                column: "WarningId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfile",
                table: "UserProfile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tasks",
                table: "Tasks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TaskReview",
                table: "TaskReview",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TaskMembers",
                table: "TaskMembers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_QualityMetrics",
                table: "QualityMetrics",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PermissionForm",
                table: "PermissionForm",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MonthlyProductivity",
                table: "MonthlyProductivity",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LeaveForm",
                table: "LeaveForm",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Goal",
                table: "Goal",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FiveSPoints",
                table: "FiveSPoints",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Departments",
                table: "Departments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Auditlog",
                table: "Auditlog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Announcements",
                table: "Announcements",
                column: "Id");
        }
    }
}
