using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff_work_tracking.Data;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace staff.Controllers
{
    [Route("api/Dashboard")]
    [ApiController]
    public class DashboardReportsController : ControllerBase


    {

        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public DashboardReportsController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }


        //[Authorize]
        //[HttpGet("dashboard-summary")]
        //public async Task<IActionResult> GetCompanyPerformance(
        //    [FromQuery] int year)
        //{
        //    if (year < 2000 || year > DateTime.Today.Year)
        //        return BadRequest("Invalid year.");

        //    var today = DateTime.Today;
        //    var currentMonth = year == today.Year ? today.Month : 12;

        //    // Company totals
        //    var totalUsers = await _context.Users.CountAsync();
        //    var totalDepartments = await _context.Departments.CountAsync();

        //    // Year-wise tasks
        //    var tasks = await _context.Tasks
        //        .Where(t => t.Created_At.Year == year)
        //        .ToListAsync();

        //    bool IsCompleted(string? status) =>
        //        string.Equals(status, "Completed",
        //            StringComparison.OrdinalIgnoreCase);

        //    var completedTasks = tasks.Where(t => IsCompleted(t.Status)).ToList();
        //    var pendingTasks = tasks.Where(t => !IsCompleted(t.Status)).ToList();

        //    var overdueTasks = pendingTasks
        //        .Count(t => t.Due_Date.Date < today);

        //    var taskOnTime = completedTasks.Count(t =>
        //        t.Completed_Date != default &&
        //        t.Completed_Date.Date <= t.Due_Date.Date);

        //    var taskDelayed = completedTasks.Count(t =>
        //        t.Completed_Date != default &&
        //        t.Completed_Date.Date > t.Due_Date.Date);

        //    var taskCompletedWithDate = completedTasks.Count(t =>
        //        t.Completed_Date != default);

        //    // Year-wise goals
        //    var goals = await _context.Goal
        //        .Where(g => g.StartDate.Year == year)
        //        .ToListAsync();

        //    var completedGoals = goals.Where(g => IsCompleted(g.Status)).ToList();
        //    var pendingGoals = goals.Where(g => !IsCompleted(g.Status)).ToList();

        //    var overdueGoals = pendingGoals
        //        .Count(g => g.DueDate.Date < today);

        //    var goalsWithCompletionDate = completedGoals
        //        .Where(g => g.Completed_Date.HasValue)
        //        .ToList();

        //    var goalOnTime = goalsWithCompletionDate.Count(g =>
        //        g.Completed_Date!.Value.Date <= g.DueDate.Date);

        //    var goalDelayed = goalsWithCompletionDate.Count(g =>
        //        g.Completed_Date!.Value.Date > g.DueDate.Date);

        //    double Percent(int count, int total) =>
        //        total == 0 ? 0 : Math.Round(count * 100.0 / total, 2);

        //    // Department list
        //    var departments = await _context.Departments
        //        .Select(d => d.DepartmentName)
        //        .ToListAsync();

        //    var departmentResults = new List<object>();
        //    var monthlyCompany = new List<object>();

        //    // =========================================================
        //    // COMPANY PRODUCTIVITY AND PERFORMANCE
        //    // Staff productivity is out of 85.
        //    // Staff performance (TotalScore) is out of 100.
        //    // =========================================================

        //    var productivityRecords = await _context.MonthlyProductivity
        //        .Where(p =>
        //            p.Year == year &&
        //            p.Month >= 1 &&
        //            p.Month <= currentMonth)
        //        .ToListAsync();

        //    // Convert staff productivity from 85 to 100.
        //    double StaffProductivityPercentage(double productivity)
        //    {
        //        return Math.Round(
        //            (productivity / 85.0) * 100.0, 2);
        //    }

        //    // Monthly company scores
        //    var monthlyCompany = new List<object>();

        //    for (int month = 1; month <= currentMonth; month++)
        //    {
        //        var monthRecords = productivityRecords
        //            .Where(p => p.Month == month)
        //            .ToList();

        //        monthlyCompany.Add(new
        //        {
        //            month,

        //            employeeCount = monthRecords
        //                .Select(p => p.StaffId)
        //                .Distinct()
        //                .Count(),

        //            performance = monthRecords.Count == 0
        //                ? (double?)null
        //                : Math.Round(
        //                    monthRecords.Average(p => (double)p.TotalScore), 2),

        //            productivity = monthRecords.Count == 0
        //                ? (double?)null
        //                : Math.Round(
        //                    monthRecords.Average(
        //                        p => StaffProductivityPercentage(
        //                            (double)p.Productivity)), 2)
        //        });
        //    }

        //    // =========================================================
        //    // YEARLY OVERALL COMPANY SCORES
        //    // Average all recorded employee-month scores in the year.
        //    // =========================================================

        //    var yearlyOverallCompanyScore = productivityRecords.Count == 0
        //        ? (double?)null
        //        : Math.Round(
        //            productivityRecords.Average(p => (double)p.TotalScore), 2);

        //    var yearlyCompanyProductivity = productivityRecords.Count == 0
        //        ? (double?)null
        //        : Math.Round(
        //            productivityRecords.Average(
        //                p => StaffProductivityPercentage(
        //                    (double)p.Productivity)), 2);
        //    foreach (var departmentName in departments)
        //    {
        //        var userIds = await _context.Users
        //            .Where(u => u.Department == departmentName)
        //            .Select(u => u.UserId)
        //            .ToListAsync();

        //        // Tasks assigned to this department
        //        var members = await _context.TaskMembers
        //            .Where(m => m.Assign_To != null && m.Assign_To != "")
        //            .ToListAsync();

        //        var taskCodes = members
        //            .Where(m =>
        //            {
        //                var value = m.Assign_To!.Split('-')[0].Trim();
        //                return int.TryParse(value, out int id) &&
        //                       userIds.Contains(id);
        //            })
        //            .Select(m => m.TaskCode)
        //            .Distinct()
        //            .ToHashSet();

        //        var deptTasks = tasks
        //            .Where(t => taskCodes.Contains(t.TaskCode))
        //            .ToList();

        //        // Goals assigned to this department
        //        var goalIds = await _context.GoalAssignment
        //            .Where(a => userIds.Contains(a.UserId))
        //            .Select(a => a.GoalId)
        //            .Distinct()
        //            .ToListAsync();

        //        var deptGoals = goals
        //            .Where(g => goalIds.Contains(g.Id))
        //            .ToList();

        //        // Include parent goals without duplicates
        //        var parentIds = deptGoals
        //            .Where(g => g.ParentGoalId.HasValue)
        //            .Select(g => g.ParentGoalId!.Value)
        //            .Distinct()
        //            .ToList();

        //        var parents = await _context.Goal
        //            .Where(g => parentIds.Contains(g.Id) &&
        //                        g.StartDate.Year == year)
        //            .ToListAsync();

        //        deptGoals.AddRange(parents);
        //        deptGoals = deptGoals
        //            .GroupBy(g => g.Id)
        //            .Select(g => g.First())
        //            .ToList();

        //        var deptCompletedTasks = deptTasks
        //            .Where(t => IsCompleted(t.Status)).ToList();

        //        var deptPendingTasks = deptTasks
        //            .Where(t => !IsCompleted(t.Status)).ToList();

        //        var deptCompletedGoals = deptGoals
        //            .Where(g => IsCompleted(g.Status)).ToList();

        //        var deptPendingGoals = deptGoals
        //            .Where(g => !IsCompleted(g.Status)).ToList();

        //        var deptScores = productivityRecords
        //            .Where(p => userIds.Contains(p.StaffId))
        //            .ToList();

        //        var monthlyDepartment = new List<object>();

        //        for (int month = 1; month <= currentMonth; month++)
        //        {
        //            var records = deptScores
        //                .Where(p => p.Month == month)
        //                .ToList();

        //            monthlyDepartment.Add(new
        //            {
        //                month,
        //                performance = records.Count == 0
        //                    ? 0
        //                    : Math.Round(
        //                        records.Average(p => p.TotalScore), 2),
        //                productivity = records.Count == 0
        //                    ? 0
        //                    : Math.Round(
        //                        records.Average(p => p.Productivity), 2)
        //            });
        //        }

        //        var deptGoalsWithDates = deptCompletedGoals
        //            .Where(g => g.Completed_Date.HasValue)
        //            .ToList();

        //        var deptTaskOnTime = deptCompletedTasks.Count(t =>
        //            t.Completed_Date != default &&
        //            t.Completed_Date.Date <= t.Due_Date.Date);

        //        var deptTaskDelayed = deptCompletedTasks.Count(t =>
        //            t.Completed_Date != default &&
        //            t.Completed_Date.Date > t.Due_Date.Date);

        //        var deptGoalOnTime = deptGoalsWithDates.Count(g =>
        //            g.Completed_Date!.Value.Date <= g.DueDate.Date);

        //        var deptGoalDelayed = deptGoalsWithDates.Count(g =>
        //            g.Completed_Date!.Value.Date > g.DueDate.Date);

        //        departmentResults.Add(new
        //        {
        //            department = departmentName,
        //            totalUsers = userIds.Count,

        //            tasks = new
        //            {
        //                total = deptTasks.Count,
        //                completed = deptCompletedTasks.Count,
        //                pending = deptPendingTasks.Count,
        //                overdue = deptPendingTasks.Count(t =>
        //                    t.Due_Date.Date < today),
        //                completionPercentage = Percent(
        //                    deptCompletedTasks.Count, deptTasks.Count),
        //                onTimePercentage = Percent(
        //                    deptTaskOnTime, deptTaskOnTime + deptTaskDelayed),
        //                delayedPercentage = Percent(
        //                    deptTaskDelayed, deptTaskOnTime + deptTaskDelayed)
        //            },

        //            goals = new
        //            {
        //                total = deptGoals.Count,
        //                completed = deptCompletedGoals.Count,
        //                pending = deptPendingGoals.Count,
        //                overdue = deptPendingGoals.Count(g =>
        //                    g.DueDate.Date < today),
        //                completionPercentage = Percent(
        //                    deptCompletedGoals.Count, deptGoals.Count),
        //                onTimePercentage = Percent(
        //                    deptGoalOnTime,
        //                    deptGoalOnTime + deptGoalDelayed),
        //                delayedPercentage = Percent(
        //                    deptGoalDelayed,
        //                    deptGoalOnTime + deptGoalDelayed)
        //            },

        //            monthlyPerformance = monthlyDepartment
        //        });
        //    }

        //    return Ok(new
        //    {
        //        year,
        //        availableMonths = currentMonth,

        //        company = new
        //        {
        //            totalUsers,
        //            totalDepartments,

        //            tasks = new
        //            {
        //                total = tasks.Count,
        //                completed = completedTasks.Count,
        //                pending = pendingTasks.Count,
        //                overdue = overdueTasks,
        //                completionPercentage = Percent(
        //                    completedTasks.Count, tasks.Count),
        //                onTimePercentage = Percent(
        //                    taskOnTime, taskOnTime + taskDelayed),
        //                delayedPercentage = Percent(
        //                    taskDelayed, taskOnTime + taskDelayed)
        //            },

        //            goals = new
        //            {
        //                total = goals.Count,
        //                completed = completedGoals.Count,
        //                pending = pendingGoals.Count,
        //                overdue = overdueGoals,
        //                completionPercentage = Percent(
        //                    completedGoals.Count, goals.Count),
        //                onTimePercentage = Percent(
        //                    goalOnTime, goalOnTime + goalDelayed),
        //                delayedPercentage = Percent(
        //                    goalDelayed, goalOnTime + goalDelayed)
        //            }
        //        },
        //        yearlyOverallCompanyScore,
        //        yearlyCompanyProductivity,
        //        monthlyCompanyPerformance = monthlyCompany,
        //        departments = departmentResults
        //    });
        //}




        [Authorize]
        [HttpGet("dashboard-summary")]
        public async Task<IActionResult> GetCompanyPerformance([FromQuery] int year)
        {
            if (year < 2000 || year > DateTime.Today.Year)
                return BadRequest("Invalid year.");

            var today = DateTime.Today;
            var currentMonth = year == today.Year ? today.Month : 12;

            bool IsCompleted(string? status) =>
                string.Equals(status, "Completed",
                    StringComparison.OrdinalIgnoreCase);

            double Percent(int count, int total) =>
                total == 0 ? 0 : Math.Round(count * 100.0 / total, 2);

            // =====================================================
            // COMPANY TOTALS
            // =====================================================

            var totalUsers = await _context.Users.CountAsync();
            var totalDepartments = await _context.Departments.CountAsync();

            // =====================================================
            // YEAR-WISE TASKS
            // =====================================================

            var tasks = await _context.Tasks
                .Where(t => t.Created_At.Year == year)
                .ToListAsync();

            var completedTasks = tasks
                .Where(t => IsCompleted(t.Status))
                .ToList();

            var pendingTasks = tasks
                .Where(t => !IsCompleted(t.Status))
                .ToList();

            var overdueTasks = pendingTasks
                .Count(t => t.Due_Date.Date < today);

            var completedTasksWithDates = completedTasks
      .Where(t => t.Completed_Date.Year > 1)
      .ToList();

            var taskOnTime = completedTasksWithDates.Count(t =>
                t.Completed_Date.Date <= t.Due_Date.Date);

            var taskDelayed = completedTasksWithDates.Count(t =>
                t.Completed_Date.Date > t.Due_Date.Date);

            // =====================================================
            // YEAR-WISE GOALS
            // =====================================================

            var goals = await _context.Goal
                .Where(g => g.StartDate.Year == year)
                .ToListAsync();

            var completedGoals = goals
                .Where(g => IsCompleted(g.Status))
                .ToList();

            var pendingGoals = goals
                .Where(g => !IsCompleted(g.Status))
                .ToList();

            var overdueGoals = pendingGoals
                .Count(g => g.DueDate.Date < today);

            var goalsWithCompletionDate = completedGoals
     .Where(g =>
         g.Completed_Date.HasValue &&
         g.Completed_Date.Value.Year > 1)
     .ToList();

            var goalOnTime = goalsWithCompletionDate.Count(g =>
                g.Completed_Date!.Value.Date <= g.DueDate.Date);

            var goalDelayed = goalsWithCompletionDate.Count(g =>
                g.Completed_Date!.Value.Date > g.DueDate.Date);

            // =====================================================
            // COMPANY PRODUCTIVITY AND PERFORMANCE
            //
            // Employee Productivity: out of 85, converted to 100.
            // Employee TotalScore: out of 100.
            // Company scores use employees, NOT department scores.
            // =====================================================

            var productivityRecords = await _context.MonthlyProductivity
                .Where(p =>
                    p.Year == year &&
                    p.Month >= 1 &&
                    p.Month <= currentMonth)
                .ToListAsync();

            double StaffProductivityPercentage(double score) =>
                Math.Round(score / 85.0 * 100.0, 2);

            var monthlyCompany = new List<object>();

            for (int month = 1; month <= currentMonth; month++)
            {
                var records = productivityRecords
                    .Where(p => p.Month == month)
                    .ToList();

                monthlyCompany.Add(new
                {
                    month,
                    employeeCount = records
                        .Select(p => p.StaffId)
                        .Distinct()
                        .Count(),

                    performance = records.Count == 0
                        ? (double?)null
                        : Math.Round(
                            records.Average(p => (double)p.TotalScore), 2),

                    productivity = records.Count == 0
                        ? (double?)null
                        : Math.Round(
                            records.Average(p =>
                                StaffProductivityPercentage(
                                    (double)p.Productivity)), 2)
                });
            }

            // Yearly overall company scores.
            // Average employee records with scores; missing months are
            // excluded rather than treated as zero.
            var yearlyOverallCompanyScore = productivityRecords.Count == 0
                ? (double?)null
                : Math.Round(
                    productivityRecords.Average(p => (double)p.TotalScore), 2);

            var yearlyCompanyProductivity = productivityRecords.Count == 0
                ? (double?)null
                : Math.Round(
                    productivityRecords.Average(p =>
                        StaffProductivityPercentage((double)p.Productivity)), 2);

            // =====================================================
            // DEPARTMENT SUMMARIES
            // These remain separate from company score calculations.
            // =====================================================

            var departments = await _context.Departments
                .Select(d => d.DepartmentName)
                .ToListAsync();

            var departmentResults = new List<object>();

            // Load task members once, outside the department loop.
            var members = await _context.TaskMembers
                .Where(m => m.Assign_To != null && m.Assign_To != "")
                .ToListAsync();

            foreach (var departmentName in departments)
            {
                var userIds = await _context.Users
                    .Where(u => u.Department == departmentName)
                    .Select(u => u.UserId)
                    .ToListAsync();

                // Tasks assigned to this department.
                var taskCodes = members
                    .Where(m =>
                    {
                        var value = m.Assign_To!.Split('-')[0].Trim();
                        return int.TryParse(value, out int id) &&
                               userIds.Contains(id);
                    })
                    .Select(m => m.TaskCode)
                    .Distinct()
                    .ToHashSet();

                var deptTasks = tasks
                    .Where(t => taskCodes.Contains(t.TaskCode))
                    .ToList();

                // Goals assigned to this department.
                var goalIds = await _context.GoalAssignment
                    .Where(a => userIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                var deptGoals = goals
                    .Where(g => goalIds.Contains(g.Id))
                    .ToList();

                // Include parent goals without duplicates.
                var parentIds = deptGoals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                var parents = await _context.Goal
                    .Where(g =>
                        parentIds.Contains(g.Id) &&
                        g.StartDate.Year == year)
                    .ToListAsync();

                deptGoals.AddRange(parents);

                deptGoals = deptGoals
                    .GroupBy(g => g.Id)
                    .Select(g => g.First())
                    .ToList();

                var deptCompletedTasks = deptTasks
                    .Where(t => IsCompleted(t.Status))
                    .ToList();

                var deptPendingTasks = deptTasks
                    .Where(t => !IsCompleted(t.Status))
                    .ToList();

                var deptCompletedGoals = deptGoals
                    .Where(g => IsCompleted(g.Status))
                    .ToList();

                var deptPendingGoals = deptGoals
                    .Where(g => !IsCompleted(g.Status))
                    .ToList();

                // Department monthly scores use employee records.
                // These are separate from the company-wide calculations.
                var deptScores = productivityRecords
                    .Where(p => userIds.Contains(p.StaffId))
                    .ToList();

                var monthlyDepartment = new List<object>();

                for (int month = 1; month <= currentMonth; month++)
                {
                    var records = deptScores
                        .Where(p => p.Month == month)
                        .ToList();

                    monthlyDepartment.Add(new
                    {
                        month,
                        employeeCount = records
                            .Select(p => p.StaffId)
                            .Distinct()
                            .Count(),

                        performance = records.Count == 0
                            ? (double?)null
                            : Math.Round(
                                records.Average(p => (double)p.TotalScore), 2),

                        // Department productivity remains on its existing scale.
                        productivity = records.Count == 0
                            ? (double?)null
                            : Math.Round(
                                records.Average(p => (double)p.Productivity), 2)
                    });
                }

                var deptTasksWithDates = deptCompletedTasks
    .Where(t => t.Completed_Date.Year > 1)
    .ToList();

                var deptGoalsWithDates = deptCompletedGoals
                    .Where(g =>
                        g.Completed_Date.HasValue &&
                        g.Completed_Date.Value.Year > 1)
                    .ToList();

                var deptTaskOnTime = deptTasksWithDates.Count(t =>
                    t.Completed_Date.Date <= t.Due_Date.Date);

                var deptTaskDelayed = deptTasksWithDates.Count(t =>
                    t.Completed_Date.Date > t.Due_Date.Date);

                var deptGoalOnTime = deptGoalsWithDates.Count(g =>
                    g.Completed_Date!.Value.Date <= g.DueDate.Date);

                var deptGoalDelayed = deptGoalsWithDates.Count(g =>
                    g.Completed_Date!.Value.Date > g.DueDate.Date);
                departmentResults.Add(new
                {
                    department = departmentName,
                    totalUsers = userIds.Count,

                    tasks = new
                    {
                        total = deptTasks.Count,
                        completed = deptCompletedTasks.Count,
                        pending = deptPendingTasks.Count,
                        overdue = deptPendingTasks.Count(t =>
                            t.Due_Date.Date < today),

                        completionPercentage = Percent(
                            deptCompletedTasks.Count, deptTasks.Count),

                        onTimePercentage = Percent(
                            deptTaskOnTime, deptTaskOnTime + deptTaskDelayed),

                        delayedPercentage = Percent(
                            deptTaskDelayed, deptTaskOnTime + deptTaskDelayed)
                    },

                    goals = new
                    {
                        total = deptGoals.Count,
                        completed = deptCompletedGoals.Count,
                        pending = deptPendingGoals.Count,
                        overdue = deptPendingGoals.Count(g =>
                            g.DueDate.Date < today),

                        completionPercentage = Percent(
                            deptCompletedGoals.Count, deptGoals.Count),

                        onTimePercentage = Percent(
                            deptGoalOnTime, deptGoalOnTime + deptGoalDelayed),

                        delayedPercentage = Percent(
                            deptGoalDelayed, deptGoalOnTime + deptGoalDelayed)
                    },

                    monthlyPerformance = monthlyDepartment
                });
            }

            // =====================================================
            // RESPONSE
            // =====================================================

            return Ok(new
            {
                year,
                availableMonths = currentMonth,

                company = new
                {
                    totalUsers,
                    totalDepartments,

                    // Yearly score cards for the CEO.
                    yearlyOverallCompanyScore,
                    yearlyCompanyProductivity,

                    tasks = new
                    {
                        total = tasks.Count,
                        completed = completedTasks.Count,
                        pending = pendingTasks.Count,
                        overdue = overdueTasks,

                        completionPercentage = Percent(
                            completedTasks.Count, tasks.Count),

                        onTimePercentage = Percent(
                            taskOnTime, taskOnTime + taskDelayed),

                        delayedPercentage = Percent(
                            taskDelayed, taskOnTime + taskDelayed)
                    },

                    goals = new
                    {
                        total = goals.Count,
                        completed = completedGoals.Count,
                        pending = pendingGoals.Count,
                        overdue = overdueGoals,

                        completionPercentage = Percent(
                            completedGoals.Count, goals.Count),

                        onTimePercentage = Percent(
                            goalOnTime, goalOnTime + goalDelayed),

                        delayedPercentage = Percent(
                            goalDelayed, goalOnTime + goalDelayed)
                    }
                },

                // Monthly company chart data.
                monthlyCompanyPerformance = monthlyCompany,

                // Separate department comparisons.
                departments = departmentResults
            });
        }


        //[HttpGet("all-departments-productivity")]
        //public async Task<IActionResult> GetAllDepartmentsProductivity(int year,int? month = null,int? quarter = null)
        //{
        //    try
        //    {
        //        // =====================================================
        //        // VALIDATION
        //        // =====================================================
        //        if (year <= 0)
        //            return BadRequest("Invalid year.");

        //        if (month.HasValue && (month.Value < 1 || month.Value > 12))
        //            return BadRequest("Month must be between 1 and 12.");

        //        if (quarter.HasValue && (quarter.Value < 1 || quarter.Value > 4))
        //            return BadRequest("Quarter must be between 1 and 4.");

        //        if (month.HasValue && quarter.HasValue)
        //            return BadRequest("Use either month or quarter, not both.");

        //        // =====================================================
        //        // DATE RANGE
        //        // =====================================================
        //        int startMonth;
        //        int endMonth;

        //        if (month.HasValue)
        //        {
        //            startMonth = month.Value;
        //            endMonth = month.Value;
        //        }
        //        else if (quarter.HasValue)
        //        {
        //            startMonth = ((quarter.Value - 1) * 3) + 1;
        //            endMonth = startMonth + 2;
        //        }
        //        else
        //        {
        //            // Full year
        //            startMonth = 1;
        //            endMonth = 12;
        //        }

        //        // =====================================================
        //        // GET DEPARTMENTS
        //        // =====================================================
        //        var departments = await _context.Departments
        //            .Select(d => d.DepartmentName)
        //            .ToListAsync();

        //        // =====================================================
        //        // GET USERS
        //        // =====================================================
        //        var users = await _context.Users
        //            .Where(u => u.Department != null)
        //            .Select(u => new
        //            {
        //                u.UserId,
        //                u.Department
        //            })
        //            .ToListAsync();

        //        // =====================================================
        //        // GET MONTHLY PRODUCTIVITY
        //        // Only required months/year
        //        // =====================================================
        //        var productivityData = await _context.MonthlyProductivity
        //            .Where(x =>
        //                x.Year == year &&
        //                x.Month >= startMonth &&
        //                x.Month <= endMonth)
        //            .Select(x => new
        //            {
        //                x.StaffId,
        //                x.Month,
        //                x.Year,
        //                x.TotalScore,
        //                x.Productivity,
        //                x.AttitudeScore,
        //                x.TaskPoints,
        //                x.GoalPoints
        //            })
        //            .ToListAsync();

        //        // =====================================================
        //        // USER -> DEPARTMENT LOOKUP
        //        // =====================================================
        //        var userDepartmentLookup = users
        //            .GroupBy(x => x.UserId)
        //            .ToDictionary(
        //                g => g.Key,
        //                g => g.First().Department
        //            );

        //        // =====================================================
        //        // GROUP PRODUCTIVITY BY DEPARTMENT + MONTH
        //        // =====================================================
        //        var departmentMonthlyData = productivityData
        //            .Where(x => userDepartmentLookup.ContainsKey(x.StaffId))
        //            .GroupBy(x => new
        //            {
        //                Department = userDepartmentLookup[x.StaffId],
        //                x.Month
        //            })
        //            .Select(g => new
        //            {
        //                department = g.Key.Department,
        //                month = g.Key.Month,

        //                // Final score out of 100
        //                productivity = Math.Round(
        //                    g.Average(x => x.TotalScore),
        //                    2
        //                ),

        //                // Optional supporting values
        //                totalStaff = g.Select(x => x.StaffId)
        //                              .Distinct()
        //                              .Count(),

        //                averageProductivity = Math.Round(
        //                    g.Average(x => x.Productivity),
        //                    2
        //                ),

        //                averageAttitude = Math.Round(
        //                    g.Average(x => x.AttitudeScore),
        //                    2
        //                )
        //            })
        //            .ToList();

        //        // =====================================================
        //        // BUILD DEPARTMENT RESPONSE
        //        // =====================================================
        //        var result = new List<object>();

        //        foreach (var department in departments)
        //        {
        //            var monthlyResult = new List<object>();

        //            for (int m = startMonth; m <= endMonth; m++)
        //            {
        //                var data = departmentMonthlyData
        //                    .FirstOrDefault(x =>
        //                        x.department == department &&
        //                        x.month == m);

        //                monthlyResult.Add(new
        //                {
        //                    month = m,

        //                    // Final score /100
        //                    productivity = data?.productivity ?? 0,

        //                    // Number of staff having productivity record
        //                    totalStaff = data?.totalStaff ?? 0,

        //                    // Productivity /85
        //                    averageProductivity = data?.averageProductivity ?? 0,

        //                    // Attitude /15
        //                    averageAttitude = data?.averageAttitude ?? 0
        //                });
        //            }

        //            result.Add(new
        //            {
        //                department,
        //                monthlyData = monthlyResult
        //            });
        //        }

        //        // =====================================================
        //        // RESPONSE
        //        // =====================================================
        //        return Ok(new
        //        {
        //            year,
        //            month,
        //            quarter,
        //            startMonth,
        //            endMonth,
        //            monthsReturned = endMonth - startMonth + 1,
        //            data = result
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "Error fetching departments productivity",
        //            error = ex.Message
        //        });
        //    }
        //}

        [HttpGet("pending-users")]
        public async Task<IActionResult> GetPendingUsers()
        {
            var pendingUsers = await _context.Users
                .Where(u => u.Status == "Pending")
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Department,
                    u.Role,
                    u.Created_by
                })
                .ToListAsync();

            //return Ok(pendingUsers);

            return Ok(new
            {
                totalCount = pendingUsers.Count,
                pendingUsers
            });
        }

        [HttpPost("approve-user")]
        public async Task<IActionResult> ApproveUser([FromBody] ApproveUser dto)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == dto.UserId);

            if (user == null)
                return NotFound("User not found");

            if (user.Status != "Pending")
                return BadRequest("User already processed");

            user.Status = dto.Approve ? "Active" : "Rejected";


            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = dto.Approve ? "User approved successfully" : "User rejected",
                user.UserId,
                user.Status

            });
        }


    //    [HttpGet("Manager-dashboard/{departmentName}")]
    //    public async Task<IActionResult> GetDepartmentSummary(string departmentName, DateTime? fromDate, DateTime? toDate)
    //    {
    //        try
    //        {
    //            var today = DateTime.Today;

    //            // 1️⃣ Get Department Users
    //            var departmentUsers = await _context.Users
    //                .Where(u => u.Department == departmentName)
    //                .Select(u => new
    //                {
    //                    Id = u.UserId.ToString(),
    //                    u.Name
    //                })
    //                .ToListAsync();

    //            if (!departmentUsers.Any())
    //            {
    //                return Ok(new
    //                {
    //                    Department = departmentName,
    //                    UserCount = 0,
    //                    Message = "No users found in this department"
    //                });
    //            }

    //            var userIds = departmentUsers.Select(u => u.Id).ToList();

    //            // 2️⃣ Get Department Task Codes
    //            var departmentTaskCodes = await _context.TaskMembers
    //                .Where(tm => userIds.Contains(
    //                    tm.Assign_To.Substring(0, tm.Assign_To.IndexOf("-"))
    //                ))
    //                .Select(tm => tm.TaskCode)
    //                .Distinct()
    //                .ToListAsync();

    //            // 3️⃣ Base Task Query
    //            var taskQuery = _context.Tasks
    //                .Where(t => departmentTaskCodes.Contains(t.TaskCode));

    //            // Date Filter
    //            if (fromDate.HasValue && toDate.HasValue)
    //            {
    //                var start = fromDate.Value.Date;
    //                var end = toDate.Value.Date.AddDays(1).AddTicks(-1);

    //                taskQuery = taskQuery
    //                    .Where(t => t.Created_At >= start && t.Created_At <= end);
    //            }

    //            var taskList = await taskQuery.ToListAsync();
    //            var totalTasks = taskList.Count;

    //            // 4️⃣ Normalize Status
    //            var normalizedTasks = taskList.Select(t => new
    //            {
    //                Task = t,
    //                Status = t.Status?.Trim().ToLower() ?? ""
    //            }).ToList();

    //            int completed = normalizedTasks.Count(t => t.Status == "completed");
    //            int notStarted = normalizedTasks.Count(t => t.Status == "not started");
    //            int inProgress = normalizedTasks.Count(t => t.Status == "inprogress");
    //            int pending = normalizedTasks.Count(t =>
    //                t.Status == "pending" || t.Status == "paused");

    //            // 5️⃣ Overdue Tasks (Single Logic)
    //            var overdueTasks = normalizedTasks
    //                .Where(t =>
    //                    t.Task.Due_Date < today &&
    //                    t.Status != "completed")
    //                .ToList();

    //            int overdue = overdueTasks.Count;

    //            int lateCompleted = normalizedTasks.Count(t =>
    //                t.Status == "completed" &&
    //                t.Task.Completed_Date != null &&
    //                t.Task.Completed_Date > t.Task.Due_Date);

    //            // 6️⃣ Completion %
    //            double completionPercentage =
    //                totalTasks > 0
    //                    ? Math.Round((double)completed * 100 / totalTasks, 2)
    //                    : 0;

    //            // 7️⃣ SLA %
    //            int onTimeCompleted = normalizedTasks.Count(t =>
    //                t.Status == "completed" &&
    //                t.Task.Completed_Date != null &&
    //                t.Task.Completed_Date <= t.Task.Due_Date);

    //            double slaPercentage =
    //                completed > 0
    //                    ? Math.Round((double)onTimeCompleted * 100 / completed, 2)
    //                    : 0;

    //            // 8️⃣ Average Completion Days (Safe)
    //            var validCompletedTasks = normalizedTasks
    //                .Where(t =>
    //                    t.Status == "completed" &&
    //                    t.Task.Completed_Date != null &&
    //                    t.Task.Completed_Date >= t.Task.Created_At)
    //                .Select(t =>
    //                    (t.Task.Completed_Date - t.Task.Created_At).TotalDays)
    //                .ToList();

    //            double avgCompletionDays =
    //                validCompletedTasks.Any()
    //                    ? Math.Round(validCompletedTasks.Average(), 2)
    //                    : 0;

    //            // 9️⃣ Monthly Trend (Last 6 Months)
    //            var trendStart = DateTime.Today.AddMonths(-5);

    //            var monthlyTrend = normalizedTasks
    //                .Where(t => t.Task.Created_At >= trendStart)
    //                .GroupBy(t => new
    //                {
    //                    t.Task.Created_At.Year,
    //                    t.Task.Created_At.Month
    //                })
    //                .Select(g => new
    //                {
    //                    g.Key.Year,
    //                    g.Key.Month,
    //                    Total = g.Count(),
    //                    Completed = g.Count(x => x.Status == "completed"),
    //                    Overdue = g.Count(x =>
    //                        x.Task.Due_Date < today &&
    //                        x.Status != "completed")
    //                })
    //                .OrderBy(x => x.Year)
    //                .ThenBy(x => x.Month)
    //                .ToList();

    //            // 🔟 Growth %
    //            double growth = 0;

    //            if (monthlyTrend.Count >= 2)
    //            {
    //                var last = monthlyTrend.Last().Total;
    //                var previous = monthlyTrend[monthlyTrend.Count - 2].Total;

    //                if (previous > 0)
    //                    growth = Math.Round(((double)(last - previous) / previous) * 100, 2);
    //            }

    //            // 1️⃣1️⃣ Overdue Task List
    //            var overdueTaskList = overdueTasks
    //                .OrderBy(t => t.Task.Due_Date)
    //                .Select(t => new
    //                {
    //                    t.Task.TaskCode,
    //                    t.Task.Task,
    //                    Description = t.Task.Description ?? "",
    //                    Priority = t.Task.Priority ?? "",
    //                    Status = t.Task.Status ?? "",
    //                    CreatedAt = t.Task.Created_At,
    //                    DueDate = t.Task.Due_Date,
    //                    TotalMembers = t.Task.Members,
    //                    //AssignedTo = _context.TaskMembers
    //                    //    .Where(tm => tm.TaskCode == t.Task.TaskCode)
    //                    //    .Select(tm => tm.Assign_To)
    //                    //    .ToList()

    //                    AssignedTo = _context.TaskMembers
    //.Where(tm => tm.TaskCode == t.Task.TaskCode)
    //.Select(tm => new
    //{
    //    userId = tm.Assign_To,
    //    role = _context.Users
    //        .Where(u =>
    //            u.UserId.ToString() ==
    //            (tm.Assign_To.Contains("-")
    //                ? tm.Assign_To.Substring(0, tm.Assign_To.IndexOf("-"))
    //                : tm.Assign_To)
    //        )
    //        .Select(u => u.Role)
    //        .FirstOrDefault()
    //})
    //.ToList()
    //                })
    //                .ToList();

    //            // 1️⃣2️⃣ Performer Analysis (All Department Users Included)
    //            var performerStats = departmentUsers
    //                .Select(u => new
    //                {
    //                    User = u.Name,
    //                    TotalTasks = _context.TaskMembers
    //                        .Count(tm =>
    //                            departmentTaskCodes.Contains(tm.TaskCode) &&
    //                            tm.Assign_To.StartsWith(u.Id)),
    //                    Completed = _context.TaskMembers
    //                        .Join(_context.Tasks,
    //                            tm => tm.TaskCode,
    //                            t => t.TaskCode,
    //                            (tm, t) => new { tm, t })
    //                        .Count(x =>
    //                            x.t.Status.ToLower() == "completed" &&
    //                            x.tm.Assign_To.StartsWith(u.Id))
    //                })
    //                .ToList();

    //            var topPerformer = performerStats
    //                .OrderByDescending(x => x.Completed)
    //                .FirstOrDefault();

    //            var lowPerformer = performerStats
    //                .OrderBy(x => x.Completed)
    //                .FirstOrDefault();

    //            // 🔹 Final Response
    //            return Ok(new
    //            {
    //                Department = departmentName,
    //                UserCount = departmentUsers.Count,
    //                TotalTasks = totalTasks,
    //                Completed = completed,
    //                NotStarted = notStarted,
    //                InProgress = inProgress,
    //                Pending = pending,
    //                Overdue = overdue,
    //                LateCompleted = lateCompleted,
    //                CompletionPercentage = completionPercentage,
    //                SLAPercentage = slaPercentage,
    //                AverageCompletionDays = avgCompletionDays,
    //                GrowthPercentage = growth,
    //                MonthlyTrend = monthlyTrend,
    //                OverdueTaskList = overdueTaskList,
    //                TopPerformer = topPerformer,
    //                LowPerformer = lowPerformer
    //            });
    //        }
    //        catch (Exception ex)
    //        {
    //            return StatusCode(500, new
    //            {
    //                message = "Error fetching department summary",
    //                error = ex.Message
    //            });
    //        }
    //    }

        [HttpGet("punch-corrections")]
        public async Task<IActionResult> GetPunchCorrections(int managerId)
        {
            try
            {
                // Check whether the logged-in user is Accounts Department Manager
                var manager = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.UserId == managerId &&
                        u.Department == "Accounts Department" &&
                        u.Role == "3");

                if (manager == null)
                {
                    return Forbid();
                }

                // Get ONLY APPROVED punch correction requests
                var corrections = await (
                    from correction in _context.PunchCorrection
                    join user in _context.Users
                        on correction.UserId equals user.UserId

                    where correction.Status == "Approved"

                    orderby correction.Date descending

                    select new
                    {
                        correction.Id,
                        Name = user.Name,
                        Department = user.Department,
                        Date = correction.Date,
                        CorrectionType = correction.CorrectionType,
                        PunchTime = correction.PunchTime,
                        Reason = correction.Reason,
                        Status = correction.Status,
                        ApprovedById = correction.ApprovedById
                    }
                ).ToListAsync();

                return Ok(corrections);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching approved punch corrections",
                    error = ex.Message
                });
            }
        }


        [HttpGet("hr-leaves")]
        public async Task<IActionResult> GetApprovedLeaves()
        {
            try
            {
                var leaves = await (
                    from leave in _context.LeaveForm
                    join user in _context.Users
                        on leave.SenderId equals user.UserId

                    where leave.Status == "Approved"

                    orderby leave.FromDate descending

                    select new
                    {
                        leave.Id,
                        leave.SenderId,
                        leave.ReceiverId,
                        Name = user.Name,
                        Department = user.Department,      // <-- was missing, needed for grouping/filtering
                        Designation = leave.Designation,
                        Reason = leave.Reason,
                        FromDate = leave.FromDate,
                        ToDate = leave.ToDate,              // <-- was missing, needed for date-range display
                        LeaveType = leave.LeaveType,
                        LeaveTyp=leave.LeaveTyp,
                        Status = leave.Status,
                      
                    }
                ).ToListAsync();

                return Ok(leaves);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error while fetching approved leaves.",
                    error = ex.Message
                });
            }
        }

      
    }
}
