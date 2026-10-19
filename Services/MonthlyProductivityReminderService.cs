
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

        private DateTime? _lastReminderDate;

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
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Monthly productivity reminder error");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(30),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task CheckAndSendMonthEndReminder(
            CancellationToken stoppingToken)
        {
            // =========================================
            // INDIA TIME
            // =========================================

            TimeZoneInfo indiaTimeZone;

            try
            {
                // Linux VPS
                indiaTimeZone =
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows
                indiaTimeZone =
                    TimeZoneInfo.FindSystemTimeZoneById(
                        "India Standard Time");
            }

            var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                indiaTimeZone);

            // =========================================
            // CHECK DAY BEFORE MONTH END
            // =========================================

            var tomorrow = indiaNow.Date.AddDays(1);

            int lastDay = DateTime.DaysInMonth(
                tomorrow.Year,
                tomorrow.Month);

            if (tomorrow.Day != lastDay)
                return;

            // =========================================
            // SEND AT 12:00 PM IST
            // =========================================

            if (indiaNow.Hour != 12 || indiaNow.Minute != 0)
                return;

            // Prevent duplicate sends during this process run.
            if (_lastReminderDate == indiaNow.Date)
                return;

            // =========================================
            // GET SERVICES
            // =========================================

            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var firebase = scope.ServiceProvider
                .GetRequiredService<FirebaseNotificationService>();

            // =========================================
            // GET ROLES WITH POSITIONS 1, 2, AND 3
            // =========================================

            var roleNames = await context.Roles
                .Where(r =>
                    r.Position == 1 ||
                    r.Position == 2 ||
                    r.Position == 3)
                .Select(r => r.RoleName)
                .ToListAsync(stoppingToken);

            if (roleNames.Count == 0)
            {
                _logger.LogWarning(
                    "No roles found for positions 1, 2, and 3.");

                return;
            }

            // =========================================
            // GET USERS WITH FCM TOKENS
            // =========================================

            var users = await context.Users
                .Where(u =>
                    roleNames.Contains(u.Role) &&
                    !string.IsNullOrWhiteSpace(u.FcmToken))
                .ToListAsync(stoppingToken);

            _logger.LogInformation(
                "Found {Count} users with role positions 1, 2, and 3.",
                users.Count);

            // Set before sending to avoid duplicate attempts
            // if the loop runs again during the same date.
            _lastReminderDate = indiaNow.Date;

            // =========================================
            // SEND NOTIFICATION
            // =========================================

            foreach (var user in users)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(user.FcmToken))
                    continue;

                try
                {
                    await firebase.SendNotificationAsync(
                        user.FcmToken,

"Monthly Reminder",
"Tomorrow is the last day of the month. " +
"Please submit all completed task points, update " +
"attitude and behaviour scores, and ensure any " +
"applicable penalties are recorded. Please complete " +
"all pending updates so monthly performance and " +
"productivity can be calculated accurately."
);

                    _logger.LogInformation(
                        "Month-end reminder sent to {Name} ({UserId}), " +
                        "Role: {Role}",
                        user.Name,
                        user.UserId,
                        user.Role);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send reminder to user {UserId}",
                        user.UserId);
                }
            }

            _logger.LogInformation(
                "Monthly productivity reminders processed for {Date}.",
                indiaNow.ToString("yyyy-MM-dd"));
        }
    }
}
