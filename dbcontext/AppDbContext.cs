using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using staff;


namespace staff_work_tracking.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<SendOTP> otp { get; set; }
        public DbSet<TaskTable> Tasks { get; set; }
        public DbSet<Goal> Goal { get; set; }
        public DbSet<TaskMember> TaskMembers { get; set; }
        public DbSet<Auditlog> Auditlog { get; set; }
        public DbSet<TaskReview> TaskReview { get; set; }
        public DbSet<Announcement> Announcements { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<WorkLog> WorkLog { get; set; }
        public DbSet<Warning> Warnings { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<MonthlyProductivity> MonthlyProductivity { get; set; }
        public DbSet<FiveSPoints> FiveSPoints { get; set; }
        public DbSet<QualityMetrics> QualityMetrics { get; set; }
        public DbSet<LeaveForm> LeaveForm { get; set; }
        public DbSet<PermissionForm> PermissionForm { get; set; }
        public DbSet<UserProfile> UserProfile { get; set; }
        public DbSet<OverTime> OverTime { get; set; }
        public DbSet<TaskMemberRemoval> TaskMemberRemoval { get; set; }
        public DbSet<Roles> Roles { get; set; }
        public DbSet<ExtraWork> ExtraWork { get; set; }
        public DbSet<PunchCorrection> PunchCorrection { get; set; }
        public DbSet<AttitudeBehaviourScore> AttitudeBehaviourScore { get; set; }

    }
}
