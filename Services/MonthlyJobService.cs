using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using staff_work_tracking.Data;

namespace StaffWork_Track.Services
{
    public class MonthlyJobService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public MonthlyJobService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var context = scope.ServiceProvider
                        .GetRequiredService<AppDbContext>();

                    var service = scope.ServiceProvider
                        .GetRequiredService<ProductivityService>();

                    var now = DateTime.Now;

                    if (now.Day == 1)
                    {
                        int month = now.Month - 1;
                        int year = now.Year;

                        if (month == 0)
                        {
                            month = 12;
                            year--;
                        }

                        var staffList = await context.Users.ToListAsync();

                        foreach (var staff in staffList)
                        {
                            await service.CalculateMonthly(
                                staff.UserId,
                                month,
                                year
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"MonthlyJobService Error: {ex}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }
}