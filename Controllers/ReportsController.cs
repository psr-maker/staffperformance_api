
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff_work_tracking.Data;



namespace staff.Controllers
{
    [Route("api/Reports")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public ReportsController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }


        [HttpGet("GetAllDepartments")]
        public async Task<IActionResult> GetAllDepartments()
        {
            try
            {
                var departments = await _context.Users
                    .Where(u => !string.IsNullOrEmpty(u.Department))
                    .Select(u => u.Department)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();

                return Ok(new
                {
                    message = "Departments fetched successfully",
                    data = departments
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching departments",
                    error = ex.Message
                });
            }
        }


        [HttpGet("department-summary/{departmentName}")]
        public async Task<IActionResult> GetDepartmentSummary(string departmentName,DateTime? fromDate,DateTime? toDate)
        {
            try
            {
                var today = DateTime.Today;

                // ================= USERS =================
                var users = await _context.Users
                    .Where(u => u.Department == departmentName)
                    .Select(u => new { u.UserId, u.Name })
                    .ToListAsync();

                var totalUsers = users.Count;

                // ================= GOALS =================
                var goalsQuery = _context.Goal
                    .Where(g => g.Department == departmentName);

                if (fromDate.HasValue && toDate.HasValue)
                {
                    var start = fromDate.Value.Date;
                    var end = toDate.Value.Date.AddDays(1).AddTicks(-1);

                    goalsQuery = goalsQuery
                        .Where(g => g.StartDate >= start && g.StartDate <= end);
                }

                var goals = await goalsQuery.ToListAsync();

                var goalCodes = goals.Select(g => g.GoalCode).ToList();

                // ================= TASKS =================
                var tasks = await _context.Tasks
                    .Where(t => goalCodes.Contains(t.GoalCode))
                    .ToListAsync();

                var taskCodes = tasks.Select(t => t.TaskCode).ToList();

                // ================= TASK MEMBERS =================
                var taskMembers = await _context.TaskMembers
                    .Where(tm => taskCodes.Contains(tm.TaskCode))
                    .ToListAsync();

                // ================= GOAL CALCULATIONS =================
                int totalGoals = goals.Count;

                int completedGoals = goals.Count(g =>
                    (g.Status ?? "").ToLower() == "completed");

                int pendingGoals = goals.Count(g =>
                    (g.Status ?? "").ToLower() != "completed");

                int overdueGoals = goals.Count(g =>
                    g.DueDate < today &&
                    (g.Status ?? "").ToLower() != "completed");

                double goalCompletionPercentage =
                    totalGoals > 0
                        ? Math.Round((double)completedGoals * 100 / totalGoals, 2)
                        : 0;

                // ✅ ON TIME GOAL COMPLETION
                int onTimeGoals = goals.Count(g =>
                    (g.Status ?? "").ToLower() == "completed" &&
                    g.Completed_Date != null &&
                    g.Completed_Date <= g.DueDate);

                double onTimePercentage =
                    completedGoals > 0
                        ? Math.Round((double)onTimeGoals * 100 / completedGoals, 2)
                        : 0;

                // ================= TASK CALCULATIONS =================
                int totalTasks = tasks.Count;

                int completedTasks = tasks.Count(t =>
                    (t.Status ?? "").ToLower() == "completed");

                int pendingTasks = tasks.Count(t =>
                    (t.Status ?? "").ToLower() != "completed");

                int overdueTasks = tasks.Count(t =>
                    t.Due_Date < today &&
                    (t.Status ?? "").ToLower() != "completed");

                // ================= DELAYED GOAL % =================

                var completedGoalsList = goals
                    .Where(g =>
                        (g.Status ?? "").ToLower() == "completed" &&
                        g.Completed_Date != null &&
                        g.DueDate != null)
                    .ToList();

                int delayedGoals = completedGoalsList.Count(g =>
                    g.Completed_Date > g.DueDate);

                double delayedGoalPercentage = completedGoalsList.Any()
                    ? Math.Round((double)delayedGoals * 100 / completedGoalsList.Count, 2)
                    : 0;

                // ================= TOP / LOW PERFORMER (TASK BASED) =================
                // ================= TOP / LOW PERFORMER =================

                //int GetPriorityWeight(string? priority)
                //{
                //    return priority?.ToLower() switch
                //    {
                //        "high" => 100,
                //        "medium" => 70,
                //        "low" => 40,
                //        _ => 40
                //    };
                //}

                //var performerStats = new List<dynamic>();

                //foreach (var u in users)
                //{
                //    // Exact user match: "5-Abay" => userId = 5
                //    var userTaskCodes = taskMembers
                //        .Where(tm =>
                //            !string.IsNullOrEmpty(tm.Assign_To) &&
                //            tm.Assign_To.StartsWith($"{u.UserId}-"))
                //        .Select(tm => tm.TaskCode)
                //        .Distinct()
                //        .ToList();

                //    var assignedTasks = tasks
                //        .Where(t => userTaskCodes.Contains(t.TaskCode))
                //        .ToList();

                //    // Skip users with no assigned tasks
                //    if (!assignedTasks.Any())
                //        continue;

                //    var completedTasksList = assignedTasks
                //        .Where(t => (t.Status ?? "").ToLower() == "completed")
                //        .ToList();

                //    int assignedCount = assignedTasks.Count;
                //    int completedCount = completedTasksList.Count;

                //    int onTimeCount = completedTasksList.Count(t =>
                //        t.Completed_Date != null &&
                //        t.Completed_Date.Date <= t.Due_Date.Date);

                //    // Completion (30 points)
                //    double completionScore =
                //        assignedCount > 0
                //            ? ((double)completedCount / assignedCount) * 30
                //            : 0;

                //    // On Time (35 points)
                //    double onTimeScore =
                //        completedCount > 0
                //            ? ((double)onTimeCount / completedCount) * 35
                //            : 0;

                //    // Priority (35 points)
                //    double earnedPriorityPoints = completedTasksList.Sum(t =>
                //        GetPriorityWeight(t.Priority));

                //    double totalPriorityPoints = assignedTasks.Sum(t =>
                //        GetPriorityWeight(t.Priority));

                //    double priorityScore =
                //        totalPriorityPoints > 0
                //            ? (earnedPriorityPoints / totalPriorityPoints) * 35
                //            : 0;

                //    double finalScore =
                //        completionScore +
                //        onTimeScore +
                //        priorityScore;

                //    performerStats.Add(new
                //    {
                //        user = u.Name,
                //        assignedTasks = assignedCount,
                //        completedTasks = completedCount,
                //        onTimeTasks = onTimeCount,
                //        completionScore = Math.Round(completionScore, 2),
                //        onTimeScore = Math.Round(onTimeScore, 2),
                //        priorityScore = Math.Round(priorityScore, 2),
                //        score = Math.Round(finalScore, 2)
                //    });
                //}

                // ================= TOP & LOW PERFORMERS =================

                //var topPerformers = new List<object>();
                //var lowPerformers = new List<object>();

                //if (performerStats.Count >= 2)
                //{
                //    double maxScore = performerStats.Max(x => (double)x.score);
                //    double minScore = performerStats.Min(x => (double)x.score);

                //    // Everyone has same score → no low performer
                //    if (maxScore == minScore)
                //    {
                //        topPerformers = performerStats.Cast<object>().ToList();
                //        lowPerformers = new List<object>();
                //    }
                //    else
                //    {
                //        topPerformers = performerStats
                //            .Where(x => (double)x.score == maxScore)
                //            .Cast<object>()
                //            .ToList();

                //        lowPerformers = performerStats
                //            .Where(x => (double)x.score == minScore)
                //            .Cast<object>()
                //            .ToList();
                //    }
                //}
                // ================= OverDue Goal =================

                var overdueGoalsList = goals
    .Where(g =>
        g.DueDate < today &&
        (g.Status ?? "").ToLower() != "completed")
    .Select(g => new
    {
        goalId = g.Id,
        goal = g.Title,
        status = g.Status ?? "",
        createdAt = g.StartDate,
        dueDate = g.DueDate,
        priority = g.Priority ?? ""
    })
    .ToList();
                // ================= OverDue Task =================

                var overdueTasksList = tasks
    .Where(t =>
        t.Due_Date < today &&
        (t.Status ?? "").ToLower() != "completed")
    .Select(t => new
    {
        taskCode = t.TaskCode,
        task = t.Task,
        description = t.Description ?? "",
        priority = t.Priority ?? "",
        status = t.Status ?? "",
        createdAt = t.Created_At,
        dueDate = t.Due_Date,
        totalMembers = t.Members,
        wasEdited = t.wasEdited
    })
    .ToList();

                // ================= FINAL RESPONSE =================
                return Ok(new
                {
                    Department = departmentName,

                    TotalUsers = totalUsers,

                    TotalGoals = totalGoals,
                    CompletedGoals = completedGoals,
                    PendingGoals = pendingGoals,
                    OverdueGoals = overdueGoals,
                    GoalCompletionPercentage = goalCompletionPercentage,
                    OnTimeGoalCompletionPercentage = onTimePercentage,

                    TotalTasks = totalTasks,
                    CompletedTasks = completedTasks,
                    PendingTasks = pendingTasks,
                    OverdueTasks = overdueTasks,

                    OverdueGoalsList = overdueGoalsList,
                    OverdueTasksList = overdueTasksList,

                    DelayedGoalPercentage = delayedGoalPercentage,

                   // MonthlyTrend = monthlyTrend,

                    //TopPerformer = topPerformers,
                    //LowPerformer = lowPerformers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching department summary",
                    error = ex.Message
                });
            }
        }

        [HttpGet("department-monthly-productivity/{departmentName}")]
        public async Task<IActionResult> GetDepartmentMonthlyProductivity(
    string departmentName,
    int year)
        {
            try
            {
                var result = new List<object>();

                int currentMonth = DateTime.Now.Month;

                // Current year -> completed months only
                // Previous year -> all 12 months
                int lastMonth = year == DateTime.Now.Year
                    ? currentMonth - 1
                    : 12;

                // =====================================================
                // GET DEPARTMENT STAFF
                // =====================================================

                var userIds = await _context.Users
                    .Where(u => u.Department == departmentName)
                    .Select(u => u.UserId)
                    .ToListAsync();

                if (!userIds.Any())
                {
                    return Ok(new
                    {
                        department = departmentName,
                        year,
                        monthsReturned = 0,
                        monthlyData = result
                    });
                }

                // =====================================================
                // MONTH LOOP
                // =====================================================

                for (int month = 1; month <= lastMonth; month++)
                {
                    // =================================================
                    // STAFF MONTHLY PRODUCTIVITY
                    // =================================================

                    var staffData = await _context.MonthlyProductivity
                        .Where(x =>
                            userIds.Contains(x.StaffId) &&
                            x.Month == month &&
                            x.Year == year)
                        .ToListAsync();

                    // =================================================
                    // TASK
                    // Employee Task = /45
                    // Department Task = /40
                    // =================================================

                    decimal averageTask = staffData.Any()
                        ? staffData.Average(x => x.TaskPoints)
                        : 0m;

                    decimal taskPoints =
                        (averageTask / 45m) * 40m;

                    taskPoints = Math.Clamp(
                        taskPoints,
                        0m,
                        40m
                    );

                    // =================================================
                    // GOAL
                    // Employee Goal = /40
                    // Department Goal = /35
                    // =================================================

                    decimal averageGoal = staffData.Any()
                        ? staffData.Average(x => x.GoalPoints)
                        : 0m;

                    decimal goalPoints =
                        (averageGoal / 40m) * 35m;

                    goalPoints = Math.Clamp(
                        goalPoints,
                        0m,
                        35m
                    );

                    // =================================================
                    // ATTITUDE & BEHAVIOUR
                    // /15
                    // =================================================

                    decimal attitudeScore = staffData.Any()
                        ? staffData.Average(x => x.AttitudeScore)
                        : 0m;

                    attitudeScore = Math.Clamp(
                        attitudeScore,
                        0m,
                        15m
                    );

                    // =================================================
                    // PRODUCTIVITY /90
                    // =================================================

                    decimal productivity =
                        taskPoints +
                        goalPoints +
                        attitudeScore;

                    productivity = Math.Clamp(
                        productivity,
                        0m,
                        90m
                    );

                    // =================================================
                    // 5S
                    //
                    // Database FiveSPoints = /100
                    //
                    // Get all weekly 5S scores for this month
                    // and calculate their average.
                    // =================================================

                    decimal fiveSRaw = await _context.FiveSPoints
                        .Where(x =>
                            x.Department == departmentName &&
                            x.Year == year &&
                            x.Month == month)
                        .Select(x => (decimal?)x.Points)
                        .AverageAsync() ?? 0m;

                    // Convert /100 to /10
                    decimal fiveS =
                        (fiveSRaw / 100m) * 10m;

                    fiveS = Math.Clamp(
                        fiveS,
                        0m,
                        10m
                    );

                    // =================================================
                    // FINAL SCORE /100
                    // =================================================

                    decimal totalScore =
                        productivity + fiveS;

                    totalScore = Math.Clamp(
                        totalScore,
                        0m,
                        100m
                    );

                    // =================================================
                    // RESULT
                    // =================================================

                    result.Add(new
                    {
                        month,

                        taskPoints = Math.Round(
                            taskPoints, 2),

                        goalPoints = Math.Round(
                            goalPoints, 2),

                        attitudeScore = Math.Round(
                            attitudeScore, 2),

                        productivity = Math.Round(
                            productivity, 2),

                        fiveS = Math.Round(
                            fiveS, 2),

                        totalScore = Math.Round(
                            totalScore, 2)
                    });
                }

                // =====================================================
                // RESPONSE
                // =====================================================

                return Ok(new
                {
                    department = departmentName,
                    year,
                    monthsReturned = lastMonth,
                    monthlyData = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching department monthly productivity",
                    error = ex.Message
                });
            }
        }

        [HttpGet("Staff/{employeeId}/Year/{year}")]
        public async Task<IActionResult> GetEmployeeReportByYear(int employeeId,int year,DateTime? fromDate,DateTime? toDate)
        {
            try
            {
                // ---------------- YEAR RANGE ----------------
                var yearStart = new DateTime(year, 1, 1);
                var yearEnd = new DateTime(year, 12, 31, 23, 59, 59);

                // ---------------- TASKS ----------------
                var tasksQuery = _context.Tasks
                    .Join(_context.TaskMembers,
                          t => t.TaskCode,
                          tm => tm.TaskCode,
                          (t, tm) => new { Task = t, Member = tm })
                    .Where(x =>
                        x.Member.Assign_To.StartsWith(employeeId + "-") &&
                        x.Task.Created_At >= yearStart &&
                        x.Task.Created_At <= yearEnd)
                    .Select(x => x.Task)
                    .AsQueryable();

                // Optional date filter
                if (fromDate.HasValue && toDate.HasValue)
                {
                    var start = fromDate.Value.Date;
                    var end = toDate.Value.Date.AddDays(1).AddTicks(-1);

                    tasksQuery = tasksQuery.Where(t =>
                        t.Created_At >= start && t.Created_At <= end);
                }

                var tasks = await tasksQuery.ToListAsync();

                int totalTasks = tasks.Count;
                int completedTasks = tasks.Count(t => t.Status.ToLower() == "completed");
                int pendingTasks = tasks.Count(t => t.Status.ToLower() != "completed");
                int overdueTasks = tasks.Count(t =>
                    t.Status.ToLower() != "completed" && t.Due_Date < DateTime.Now);

                // ---------------- GOALS ----------------
                var goalsQuery = _context.Goal
                    .Where(g =>
                        g.Assign_To == employeeId.ToString() &&
                        g.StartDate >= yearStart &&
                        g.StartDate <= yearEnd);

                if (fromDate.HasValue && toDate.HasValue)
                {
                    var start = fromDate.Value.Date;
                    var end = toDate.Value.Date.AddDays(1).AddTicks(-1);

                    goalsQuery = goalsQuery.Where(g =>
                        g.StartDate >= start && g.StartDate <= end);
                }

                var goals = await goalsQuery.ToListAsync();

                int totalGoals = goals.Count;
                int completedGoals = goals.Count(g => g.Status.ToLower() == "completed");
                int pendingGoals = goals.Count(g => g.Status.ToLower() != "completed");
                int overdueGoals = goals.Count(g =>
                    g.Status.ToLower() != "completed" && g.DueDate < DateTime.Now);

                // ---------------- GOAL METRICS ----------------
                var completedGoalsWithDate = goals
                    .Where(g =>
                        g.Status.ToLower() == "completed" &&
                        g.Completed_Date != null &&
                        g.DueDate != null)
                    .ToList();

                double goalCompletionPercent = totalGoals == 0
                    ? 0
                    : (double)completedGoals * 100 / totalGoals;

                double goalOnTimePercent = completedGoalsWithDate.Count == 0
                    ? 0
                    : (double)completedGoalsWithDate.Count(g =>
                        g.Completed_Date <= g.DueDate) * 100 / completedGoalsWithDate.Count;

                // ---------------- DELAYED GOAL PERCENTAGE ----------------

                var completedGoalsWithValidDates = goals
                    .Where(g =>
                        (g.Status ?? "").ToLower() == "completed" &&
                        g.Completed_Date != null &&
                        g.DueDate != null)
                    .ToList();

                // Count delayed goals
                int delayedGoalsCount = completedGoalsWithValidDates.Count(g =>
                    g.Completed_Date > g.DueDate);

                // Calculate percentage
                double delayedGoalPercent = completedGoalsWithValidDates.Count == 0
                    ? 0
                    : (double)delayedGoalsCount * 100 / completedGoalsWithValidDates.Count;

                // ---------------- MONTHLY TREND ----------------
                var trendData = await _context.Goal
                    .Where(g => g.Assign_To.StartsWith(employeeId + "-") || g.Assign_To == employeeId.ToString())
    .Where(g => g.StartDate != null &&
            g.StartDate >= yearStart &&
            g.StartDate <= yearEnd)
    .GroupBy(g => new { g.StartDate.Year, g.StartDate.Month })
    .Select(g => new
    {
        Year = g.Key.Year,
        Month = g.Key.Month,
        Total = g.Count(),
        Completed = g.Count(x => x.Status.ToLower() == "completed"),
        Pending = g.Count(x => x.Status.ToLower() != "completed"),
        Overdue = g.Count(x =>
            x.Status.ToLower() != "completed" &&
            x.DueDate != null &&
            x.DueDate.Year == g.Key.Year &&
            x.DueDate.Month == g.Key.Month)
    })
    .OrderBy(x => x.Year)
    .ThenBy(x => x.Month)
    .ToListAsync();

                // Fill missing months
                var monthlyTrend = trendData
        .OrderBy(x => x.Year)
        .ThenBy(x => x.Month)
        .Select(x => new
        {
            x.Year,
            x.Month,
            x.Total,
            x.Completed,
            x.Pending,
            x.Overdue
        })
        .ToList();
                //-------------------------Yearly productivity-------------------------


                var monthlyData = await _context.MonthlyProductivity
                    .Where(x =>
                        x.StaffId == employeeId &&
                        x.Year == year)
                    .ToListAsync();

                int lastMonth;

                // Selected year is current year
                if (year == DateTime.Now.Year)
                {
                    // Do not include current month
                    lastMonth = DateTime.Now.Month - 1;
                }
                else
                {
                    // Previous/future selected year → all 12 months
                    lastMonth = 12;
                }

                // Only completed months
                var completedMonthData = monthlyData
                    .Where(x => x.Month >= 1 && x.Month <= lastMonth)
                    .ToList();


                //// No productivity data
                //if (!completedMonthData.Any())
                //{
                //    return Ok(new
                //    {
                //        employeeId,
                //        year,
                //        yearlyProductivity = 0
                //    });
                //}

                double yearlyProductivity = 0;

                if (completedMonthData.Any())
                {
                    yearlyProductivity = completedMonthData
                        .Average(x => (double)x.TotalScore);

                    yearlyProductivity = Math.Round(yearlyProductivity, 2);
                }

                //// Average of actual monthly scores
                //double yearlyProductivity =
                //    completedMonthData.Average(x => (double)x.TotalScore);


                //// Optional: round to 2 decimal places
                //yearlyProductivity =
                //    Math.Round(yearlyProductivity, 2);

                //-----------------------Overdue Goal and Task------------------


                var overdueTaskList = tasks
    .Where(t =>
        t.Status.ToLower() != "completed" &&
        t.Due_Date < DateTime.Now)
    .Select(t => new
    {
        taskCode = t.TaskCode,
        task = t.Task,
        description = t.Description ?? "",
        priority = t.Priority ?? "",
        status = t.Status ?? "",
        createdAt = t.Created_At,
        dueDate = t.Due_Date,
        totalMembers = t.Members,
        wasEdited = t.wasEdited
    })
    .ToList();
                var overdueGoalList = goals
    .Where(g =>
        g.Status.ToLower() != "completed" &&
        g.DueDate < DateTime.Now)
    .Select(g => new
    {
        goalId = g.Id,
        goal = g.Title,
        status = g.Status ?? "",
        createdAt = g.StartDate,
        dueDate = g.DueDate,
        priority = g.Priority ?? ""
    })
    .ToList();
                // ---------------- LEAVE & PERMISSION MONTHLY ----------------

                // LEAVE (group by month)
                var leaveData = await _context.LeaveForm
                    .Where(l =>
                        l.SenderId == employeeId &&
                        l.FromDate >= yearStart &&
                        l.FromDate <= yearEnd &&
                        (l.Status ?? "").ToLower() == "approved" && !l.CompensationExtraWorkId.HasValue)
                    .GroupBy(l => new { l.FromDate.Year, l.FromDate.Month })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        LeaveDays = g.Sum(x => x.TotalDays ?? 0)
                    })
                    .ToListAsync();


                // PERMISSION (group by month)
                var permissionData = await _context.PermissionForm
                    .Where(p =>
                        p.SenderId == employeeId &&
                        p.Date >= yearStart &&
                        p.Date <= yearEnd &&
                        (p.Status ?? "").ToLower() == "approved")
                    .GroupBy(p => new { p.Date.Year, p.Date.Month })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        PermissionHours = g.Sum(x => x.TotalHours)
                    })
                    .ToListAsync();
                // Combine both into single monthly structure
                var leavePermissionMonthly = Enumerable.Range(1, 12)
                    .Select(month => new
                    {
                        Year = year,
                        Month = month,

                        Leave = leaveData
                            .Where(l => l.Month == month)
                            .Select(l => l.LeaveDays)
                            .FirstOrDefault(),

                        Permission = permissionData
                            .Where(p => p.Month == month)
                            .Select(p => p.PermissionHours)
                            .FirstOrDefault()
                    })
                    .ToList();
                // ---------------- RESULT ----------------
                return Ok(new
                {
                    employeeId,
                    year,

                    totalTasks,
                    completedTasks,
                    pendingTasks,
                    overdueTasks,

                    totalGoals,
                    completedGoals,
                    pendingGoals,
                    overdueGoals,

                    goalCompletionPercent = Math.Round(goalCompletionPercent, 2),
                    goalOnTimePercent = Math.Round(goalOnTimePercent, 2),

                    delayedGoalPercent = Math.Round(delayedGoalPercent, 2),

                    monthlyTrend, 
                 //   yearlyProductivity,

                    overdueTaskList,  
                    overdueGoalList,

                    leavePermissionMonthly

                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching employee report",
                    error = ex.Message
                });
            }
        }


        [HttpGet("monthly-productivity/{employeeId}")]
        public async Task<IActionResult> GetMonthlyProductivity(int employeeId, int year)
        {
            try
            {
                var result = new List<object>();

                int currentMonth = DateTime.Now.Month;

                // ✅ Only completed months
                int lastMonth = (year == DateTime.Now.Year) ? currentMonth - 1 : 12;

                for (int month = 1; month <= lastMonth; month++)
                {
                    var data = await _context.MonthlyProductivity
                        .FirstOrDefaultAsync(x =>
                            x.StaffId == employeeId &&
                            x.Month == month &&
                            x.Year == year);

                    result.Add(new
                    {
                        month,
                        taskPoints = data?.TaskPoints ?? 0,
                        goalPoints = data?.GoalPoints ?? 0,
                        attitudeScore = data?.AttitudeScore??0,
                        taskpenaltypoints = data?.TaskPenaltyPoints ?? 0,
                        productivity = data?.Productivity ?? 0,
                        totalScore = data?.TotalScore??0
                    });
                }
             

                //-------------------------------------------
                return Ok(new
                {
                    employeeId,
                    year,
                    monthsReturned = lastMonth,
                    monthlyData = result,
                 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching monthly productivity",
                    error = ex.Message
                });
            }
        }


        [HttpGet("FilteredFullReport")]
        public async Task<IActionResult> GetFilteredFullReport(int? userId, string? department)
        {
            try
            {
                var today = DateTime.Today;

                // Filter users first
                var users = _context.Users.AsQueryable();

                if (userId.HasValue)
                    users = users.Where(u => u.UserId == userId.Value);

                if (!string.IsNullOrEmpty(department))
                    users = users.Where(u => u.Department == department);

                var userList = await users.ToListAsync(); 

                // ================= TASKS =================
                var allTaskMembers = await _context.TaskMembers.ToListAsync();
                var allTasks = await _context.Tasks.ToListAsync();
                var allTaskReviews = await _context.TaskReview.ToListAsync();

                var tasks = (from u in userList
                             join tm in allTaskMembers
                                 on u.UserId.ToString() equals (tm.Assign_To ?? "").Split('-')[0]
                             join t in allTasks
                                 on tm.TaskCode equals t.TaskCode
                             select new
                             {
                                 t.TaskCode,
                                 t.Task,
                                 t.Status,
                                 t.Priority,
                                 t.Members,
                                 t.Due_Date,
                                 t.Completed_Date,
                                 Points = allTaskReviews
                                     .Where(tr => tr.TaskCode == t.TaskCode)
                                     .Select(tr => (int?)tr.FinalPoints)
                                     .FirstOrDefault() ?? 0,
                                 IsOverdue = t.Status.ToLower() != "completed" && t.Due_Date != null && t.Due_Date < today
                             }).Distinct().ToList();
                // ================= GOALS =================
                var allGoals = await _context.Goal.ToListAsync();

                var goals = (from g in allGoals
                             where userList.Any(u => u.UserId.ToString() == g.Assign_To)
                             select new
                             {
                                 g.GoalCode,
                                 g.Title,
                                 g.Priority,
                                 g.Status,
                                 g.DueDate,
                                 g.Completed_Date,
                                 IsOverdue = g.Status.ToLower() != "completed" && g.DueDate < today,
                                 Points = g.Goalpoints,
                                 Progress = g.Progress,
                                 Tasks = allTasks
                                     .Where(t => t.GoalCode == g.GoalCode)
                                     .Select(t => t.Task)
                                     .ToList()
                             }).ToList();
                // ---------------- LEAVES ----------------

                var userIds = userList
         .Select(u => u.UserId)
         .ToList();

                var leaves = await _context.LeaveForm
                    .Where(l =>
                        l.LeaveType != "Holiday" &&
                        userIds.Contains(l.SenderId))
                    .ToListAsync();

                var compensationIds = leaves
                    .Where(l =>
                        l.Status != null &&
                        l.Status.Trim().ToLower() == "approved" &&
                        l.CompensationExtraWorkId.HasValue)
                    .Select(l => l.CompensationExtraWorkId!.Value)
                    .Distinct()
                    .ToList();

                var compensationData = await _context.ExtraWork
                    .Where(x => compensationIds.Contains(x.Id))
                    .ToListAsync();

                var leaveList = leaves
                    .Select(l =>
                    {
                        var compensation = l.CompensationExtraWorkId.HasValue
                            ? compensationData.FirstOrDefault(x =>
                                x.Id == l.CompensationExtraWorkId.Value)
                            : null;

                        return new
                        {
                            leaveId = l.Id,
                            type = l.LeaveType,
                            status = l.Status,
                            fromDate = l.FromDate,
                            submdate = l.SubmittedDate,
                            reason = l.Reason ?? "",
                            rejreason = l.RejectionReason,
                            approvedate = l.ApprovedDate,
                            contactno = l.ContactNumber,
                            leavecategory=l.LeaveTyp,

                            compensationUsed =
                                l.Status != null &&
                                l.Status.Trim().ToLower() == "approved" &&
                                l.CompensationExtraWorkId.HasValue,

                            compensationExtraWorkId =
                                l.CompensationExtraWorkId,

                            compensationDate =
                                compensation?.WorkedDate,
                        };
                    })
                    .ToList();
                // ---------------- PERMISSIONS ----------------

                var permissionQuery = _context.PermissionForm
    .Where(p =>
        userIds.Contains(p.SenderId)
    );
                var permissionList = permissionQuery.Select(p => new
                {
                    permissionId = p.Id,
                    date = p.Date,
                    fromTime = p.FromTime,
                    toTime = p.ToTime,
                    reason = p.Reason ?? "",
                    status = p.Status ?? "",
                    totalhours = p.TotalHours,
                    submdate = p.SubmittedDate
                }).ToList();


                return Ok(new
                {
                    tasks,
                    goals,
                    leaveList,
                    permissionList
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "Error fetching report",
                    error = ex.Message
                });
            }
        }


 


    }
}
