using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using staff_work_tracking.Data;
using staff.Services;

namespace StaffWork_Track.Services
{
    public class MonthlyProductivityReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MonthlyProductivityReminderService> _logger;

        public MonthlyProductivityReminderService(
            IServiceScopeFactory scopeFactory,
            ILogger<MonthlyProductivityReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Monthly Productivity Reminder Service Started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndSendMonthEndReminder(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Monthly productivity reminder error");
                }

                // Check every minute
                await Task.Delay(
                    TimeSpan.FromMinutes(1),
                    stoppingToken);
            }
        }

        private async Task CheckAndSendMonthEndReminder(
            CancellationToken stoppingToken)
        {
            // =====================================================
            // INDIA TIME
            // =====================================================

            var indiaTimeZone =
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

            var indiaNow =
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    indiaTimeZone);

            // =====================================================
            // GET TOMORROW
            // =====================================================

            var tomorrow = indiaNow.Date.AddDays(1);

            // =====================================================
            // CHECK WHETHER TOMORROW IS LAST DAY OF MONTH
            // =====================================================

            int tomorrowLastDay = DateTime.DaysInMonth(
                tomorrow.Year,
                tomorrow.Month);

            if (tomorrow.Day != tomorrowLastDay)
            {
                return;
            }

            // =====================================================
            // SEND AT 12:00 PM
            // =====================================================

            if (indiaNow.Hour != 12)
            {
                return;
            }

            // =====================================================
            // GET DATABASE + FIREBASE SERVICES
            // =====================================================

            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var firebase = scope.ServiceProvider
                .GetRequiredService<FirebaseNotificationService>();

            // =====================================================
            // GET MANAGER ROLE
            //
            // Position 2 = Manager
            // =====================================================

            var managerRole = await context.Roles
                .FirstOrDefaultAsync(r =>
                    r.Position == 2,
                    stoppingToken);

            if (managerRole == null)
            {
                _logger.LogWarning(
                    "Manager role not found.");

                return;
            }

            // =====================================================
            // GET ALL MANAGERS WITH FCM TOKEN
            // =====================================================

            var managers = await context.Users
                .Where(u =>
                    u.Role == managerRole.RoleName &&
                    !string.IsNullOrWhiteSpace(u.FcmToken))
                .ToListAsync(stoppingToken);

            _logger.LogInformation(
                "Found {Count} managers for month-end reminder.",
                managers.Count);

            // =====================================================
            // SEND NOTIFICATION TO ALL MANAGERS
            // =====================================================

            foreach (var manager in managers)
            {
                if (string.IsNullOrWhiteSpace(manager.FcmToken))
                    continue;

                try
                {
                    await firebase.SendNotificationAsync(
                        manager.FcmToken!,
                        "Monthly Productivity Reminder",
                        "Tomorrow is the last day of the month. " +
                        "Please update and complete all staff data, " +
                        "tasks, goals, attendance, leave, permissions " +
                        "and reviews so monthly productivity can be " +
                        "calculated correctly."
                    );

                    _logger.LogInformation(
                        "Month-end reminder sent to Manager: " +
                        "{Name} ({UserId})",
                        manager.Name,
                        manager.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "FCM failed for manager {UserId}",
                        manager.UserId);
                }
            }
        }
    }
}