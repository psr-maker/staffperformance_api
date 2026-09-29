
using Microsoft.AspNetCore.Authorization;
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

                // ============================================================
                // 1. USERS IN DEPARTMENT
                // ============================================================

                var users = await _context.Users
                    .Where(u =>
                        u.Department != null &&
                        u.Department.Trim().ToLower() ==
                        departmentName.Trim().ToLower())
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Department
                    })
                    .ToListAsync();

                var totalUsers = users.Count;

                var departmentUserIds = users
                    .Select(u => u.UserId)
                    .ToList();

                // ============================================================
                // 2. GET GOAL ASSIGNMENTS
                // ============================================================

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => departmentUserIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                // ============================================================
                // 3. GET ASSIGNED GOALS
                // ============================================================

                var goals = await _context.Goal
                    .Where(g => assignedGoalIds.Contains(g.Id))
                    .ToListAsync();

                // ============================================================
                // 4. INCLUDE YEARLY PARENT GOALS
                //
                // If a Monthly goal is assigned to department users,
                // include its Yearly parent also.
                // ============================================================

                var parentGoalIds = goals
                    .Where(g =>
                        g.ParentGoalId.HasValue &&
                        g.ParentGoalId.Value > 0)
                    .Select(g => g.ParentGoalId.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g => parentGoalIds.Contains(g.Id))
                        .ToListAsync();

                    var existingGoalIds = goals
                        .Select(g => g.Id)
                        .ToHashSet();

                    foreach (var parentGoal in parentGoals)
                    {
                        if (!existingGoalIds.Contains(parentGoal.Id))
                        {
                            goals.Add(parentGoal);
                        }
                    }
                }

                // ============================================================
                // 5. DATE FILTER
                // ============================================================

                if (fromDate.HasValue || toDate.HasValue)
                {
                    DateTime? startDate =
                        fromDate.HasValue
                            ? fromDate.Value.Date
                            : null;

                    DateTime? endDate =
                        toDate.HasValue
                            ? toDate.Value.Date
                            : null;

                    goals = goals
                        .Where(g =>
                            (!startDate.HasValue ||
                             g.StartDate.Date >= startDate.Value) &&

                            (!endDate.HasValue ||
                             g.StartDate.Date <= endDate.Value))
                        .ToList();
                }

                // ============================================================
                // 6. GOAL CODES
                // ============================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
                    .Select(g => g.GoalCode)
                    .Distinct()
                    .ToList();

                // ============================================================
                // 7. TASKS
                // ============================================================

                var tasks = await _context.Tasks
                    .Where(t =>
                        t.GoalCode != null &&
                        goalCodes.Contains(t.GoalCode))
                    .ToListAsync();

                // ============================================================
                // 8. TASK CODES
                // ============================================================

                var taskCodes = tasks
                    .Where(t => !string.IsNullOrWhiteSpace(t.TaskCode))
                    .Select(t => t.TaskCode)
                    .Distinct()
                    .ToList();

                // ============================================================
                // 9. TASK MEMBERS
                // ============================================================

                var taskMembers = await _context.TaskMembers
                    .Where(tm => taskCodes.Contains(tm.TaskCode))
                    .ToListAsync();

                // ============================================================
                // 10. GOAL CALCULATIONS
                // ============================================================

                int totalGoals = goals.Count;

                int completedGoals = goals.Count(g =>
                    string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int pendingGoals = goals.Count(g =>
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int overdueGoals = goals.Count(g =>
                    g.DueDate.Date < today &&
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                double goalCompletionPercentage =
                    totalGoals > 0
                        ? Math.Round(
                            (double)completedGoals * 100 / totalGoals,
                            2)
                        : 0;

                // ============================================================
                // 11. ON-TIME GOAL COMPLETION
                // ============================================================

                int onTimeGoals = goals.Count(g =>
                    string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase) &&
                    g.Completed_Date.HasValue &&
                    g.Completed_Date.Value <= g.DueDate);

                double onTimePercentage =
                    completedGoals > 0
                        ? Math.Round(
                            (double)onTimeGoals * 100 / completedGoals,
                            2)
                        : 0;

                // ============================================================
                // 12. DELAYED COMPLETED GOALS
                // ============================================================

                var completedGoalsList = goals
                    .Where(g =>
                        string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        g.Completed_Date.HasValue)
                    .ToList();

                int delayedGoals = completedGoalsList.Count(g =>
                    g.Completed_Date!.Value > g.DueDate);

                double delayedGoalPercentage =
                    completedGoalsList.Count > 0
                        ? Math.Round(
                            (double)delayedGoals *
                            100 /
                            completedGoalsList.Count,
                            2)
                        : 0;

                // ============================================================
                // 13. TASK CALCULATIONS
                // ============================================================

                int totalTasks = tasks.Count;

                int completedTasks = tasks.Count(t =>
                    string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int pendingTasks = tasks.Count(t =>
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int overdueTasks = tasks.Count(t =>
                    t.Due_Date.Date < today &&
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                // ============================================================
                // 14. OVERDUE GOALS LIST
                // ============================================================

                var overdueGoalsList = goals
                    .Where(g =>
                        g.DueDate.Date < today &&
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(g => new
                    {
                        goalId = g.Id,
                        goalCode = g.GoalCode,
                        goalType = g.GoalType,
                        parentGoalId = g.ParentGoalId,
                        goal = g.Title,
                        status = g.Status ?? "",
                        createdAt = g.StartDate,
                        dueDate = g.DueDate,
                        priority = g.Priority ?? "",
                        progress = g.Progress
                    })
                    .ToList();

                // ============================================================
                // 15. OVERDUE TASK LIST
                // ============================================================

                var overdueTasksList = tasks
                    .Where(t =>
                        t.Due_Date.Date < today &&
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(t => new
                    {
                        taskCode = t.TaskCode,
                        goalCode = t.GoalCode,
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

                // ============================================================
                // 16. GOAL DETAILS
                // ============================================================

                var goalDetails = goals
                    .OrderBy(g => g.GoalType == "Yearly" ? 0 : 1)
                    .ThenBy(g => g.StartDate)
                    .Select(g => new
                    {
                        id = g.Id,
                        goalCode = g.GoalCode,
                        goalType = g.GoalType,
                        parentGoalId = g.ParentGoalId,
                        title = g.Title,
                        priority = g.Priority ?? "",
                        startDate = g.StartDate,
                        dueDate = g.DueDate,
                        completedDate = g.Completed_Date,
                        status = g.Status ?? "",
                        progress = g.Progress,
                        goalPoints = g.Goalpoints,

                        tasks = tasks
                            .Where(t => t.GoalCode == g.GoalCode)
                            .Select(t => new
                            {
                                id = t.Id,
                                taskCode = t.TaskCode,
                                task = t.Task,
                                description = t.Description ?? "",
                                priority = t.Priority ?? "",
                                status = t.Status ?? "",
                                createdAt = t.Created_At,
                                dueDate = t.Due_Date,
                                completedDate = t.Completed_Date,
                                members = t.Members,
                                performanceType = t.PerformanceType,
                                quantity = t.Quantity,
                                startTime = t.StartTime,
                                endTime = t.EndTime
                            })
                            .ToList()
                    })
                    .ToList();

                // ============================================================
                // 17. FINAL RESPONSE
                // ============================================================

                return Ok(new
                {
                    department = departmentName,

                    totalUsers = totalUsers,

                    totalGoals = totalGoals,
                    completedGoals = completedGoals,
                    pendingGoals = pendingGoals,
                    overdueGoals = overdueGoals,

                    goalCompletionPercentage = goalCompletionPercentage,
                    onTimeGoalCompletionPercentage = onTimePercentage,
                    delayedGoalPercentage = delayedGoalPercentage,

                    totalTasks = totalTasks,
                    completedTasks = completedTasks,
                    pendingTasks = pendingTasks,
                    overdueTasks = overdueTasks,

                    overdueGoalsList = overdueGoalsList,
                    overdueTasksList = overdueTasksList,

                    goals = goalDetails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching department summary.",
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

        //[HttpGet("Staff/{employeeId}/Year/{year}")]
        //public async Task<IActionResult> GetEmployeeReportByYear(int employeeId,int year,DateTime? fromDate,DateTime? toDate)
        //{
        //    try
        //    {
        //        // ============================================================
        //        // 1. YEAR RANGE
        //        // ============================================================

        //        var yearStart = new DateTime(year, 1, 1);
        //        var nextYearStart = new DateTime(year + 1, 1, 1);

        //        // ============================================================
        //        // 2. CHECK EMPLOYEE
        //        // ============================================================

        //        var employee = await _context.Users
        //            .FirstOrDefaultAsync(u => u.UserId == employeeId);

        //        if (employee == null)
        //        {
        //            return NotFound(new
        //            {
        //                message = "Employee not found."
        //            });
        //        }

        //        // ============================================================
        //        // 3. DATE FILTER RANGE
        //        // ============================================================

        //        DateTime filterStart = fromDate.HasValue
        //            ? fromDate.Value.Date
        //            : yearStart;

        //        DateTime filterEndExclusive = toDate.HasValue
        //            ? toDate.Value.Date.AddDays(1)
        //            : nextYearStart;

        //        // Make sure supplied dates stay inside selected year
        //        if (filterStart < yearStart)
        //            filterStart = yearStart;

        //        if (filterEndExclusive > nextYearStart)
        //            filterEndExclusive = nextYearStart;

        //        // ============================================================
        //        // 4. TASKS ASSIGNED TO EMPLOYEE
        //        // ============================================================

        //        var employeePrefix = employeeId + "-";

        //        var tasksQuery = _context.Tasks
        //            .Join(
        //                _context.TaskMembers,
        //                t => t.TaskCode,
        //                tm => tm.TaskCode,
        //                (t, tm) => new
        //                {
        //                    Task = t,
        //                    Member = tm
        //                })
        //            .Where(x =>
        //                x.Member.Assign_To != null &&
        //                x.Member.Assign_To.StartsWith(employeePrefix) &&

        //                x.Task.Created_At >= yearStart &&
        //                x.Task.Created_At < nextYearStart &&

        //                x.Task.Created_At >= filterStart &&
        //                x.Task.Created_At < filterEndExclusive)
        //            .Select(x => x.Task)
        //            .Distinct();

        //        var tasks = await tasksQuery.ToListAsync();

        //        // ============================================================
        //        // 5. TASK CALCULATIONS
        //        // ============================================================

        //        int totalTasks = tasks.Count;

        //        int completedTasks = tasks.Count(t =>
        //            string.Equals(
        //                t.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase));

        //        int pendingTasks = tasks.Count(t =>
        //            !string.Equals(
        //                t.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase));

        //        int overdueTasks = tasks.Count(t =>
        //            !string.Equals(
        //                t.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase) &&
        //            t.Due_Date < DateTime.Now);

        //        // ============================================================
        //        // 6. GET EMPLOYEE GOAL ASSIGNMENTS
        //        // ============================================================

        //        var assignedGoalIds = await _context.GoalAssignment
        //            .Where(a => a.UserId == employeeId)
        //            .Select(a => a.GoalId)
        //            .Distinct()
        //            .ToListAsync();

        //        // ============================================================
        //        // 7. GET EMPLOYEE GOALS
        //        // ============================================================

        //        var goals = await _context.Goal
        //            .Where(g =>
        //                assignedGoalIds.Contains(g.Id) &&

        //                g.StartDate >= yearStart &&
        //                g.StartDate < nextYearStart &&

        //                g.StartDate >= filterStart &&
        //                g.StartDate < filterEndExclusive)
        //            .ToListAsync();

        //        // ============================================================
        //        // 8. INCLUDE YEARLY PARENT GOALS
        //        //
        //        // If employee has a Monthly goal:
        //        //
        //        // Yearly
        //        //   └── Monthly
        //        //
        //        // include the Yearly parent in the report.
        //        // ============================================================

        //        var parentGoalIds = goals
        //            .Where(g => g.ParentGoalId.HasValue)
        //            .Select(g => g.ParentGoalId!.Value)
        //            .Distinct()
        //            .ToList();

        //        if (parentGoalIds.Any())
        //        {
        //            var parentGoals = await _context.Goal
        //                .Where(g =>
        //                    parentGoalIds.Contains(g.Id) &&
        //                    g.GoalType == "Yearly")
        //                .ToListAsync();

        //            var existingGoalIds = goals
        //                .Select(g => g.Id)
        //                .ToHashSet();

        //            foreach (var parentGoal in parentGoals)
        //            {
        //                if (!existingGoalIds.Contains(parentGoal.Id))
        //                {
        //                    goals.Add(parentGoal);
        //                }
        //            }
        //        }

        //        // ============================================================
        //        // 9. GOAL CODES
        //        // ============================================================

        //        var goalCodes = goals
        //            .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
        //            .Select(g => g.GoalCode)
        //            .Distinct()
        //            .ToList();

        //        // ============================================================
        //        // 10. GET TASKS UNDER EMPLOYEE GOALS
        //        //
        //        // This catches tasks using GoalCode even if task membership
        //        // is already available above.
        //        // ============================================================

        //        var goalTasks = await _context.Tasks
        //            .Where(t =>
        //                t.GoalCode != null &&
        //                goalCodes.Contains(t.GoalCode) &&

        //                t.Created_At >= yearStart &&
        //                t.Created_At < nextYearStart &&

        //                t.Created_At >= filterStart &&
        //                t.Created_At < filterEndExclusive)
        //            .ToListAsync();

        //        // ============================================================
        //        // 11. COMBINE EMPLOYEE TASKS + GOAL TASKS
        //        // ============================================================

        //        var allTasks = tasks
        //            .Concat(goalTasks)
        //            .GroupBy(t => t.Id)
        //            .Select(g => g.First())
        //            .ToList();

        //        // ============================================================
        //        // 12. GOAL CALCULATIONS
        //        // ============================================================

        //        int totalGoals = goals.Count;

        //        int completedGoals = goals.Count(g =>
        //            string.Equals(
        //                g.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase));

        //        int pendingGoals = goals.Count(g =>
        //            !string.Equals(
        //                g.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase));

        //        int overdueGoals = goals.Count(g =>
        //            !string.Equals(
        //                g.Status,
        //                "Completed",
        //                StringComparison.OrdinalIgnoreCase) &&
        //            g.DueDate < DateTime.Now);

        //        // ============================================================
        //        // 13. GOAL COMPLETION %
        //        // ============================================================

        //        double goalCompletionPercent = totalGoals == 0
        //            ? 0
        //            : (double)completedGoals * 100 / totalGoals;

        //        // ============================================================
        //        // 14. ON-TIME GOALS
        //        // ============================================================

        //        var completedGoalsWithDate = goals
        //            .Where(g =>
        //                string.Equals(
        //                    g.Status,
        //                    "Completed",
        //                    StringComparison.OrdinalIgnoreCase) &&
        //                g.Completed_Date.HasValue)
        //            .ToList();

        //        int onTimeGoals = completedGoalsWithDate.Count(g =>
        //            g.Completed_Date!.Value <= g.DueDate);

        //        double goalOnTimePercent =
        //            completedGoalsWithDate.Count == 0
        //                ? 0
        //                : (double)onTimeGoals *
        //                  100 /
        //                  completedGoalsWithDate.Count;

        //        // ============================================================
        //        // 15. DELAYED GOAL %
        //        // ============================================================

        //        int delayedGoalsCount = completedGoalsWithDate.Count(g =>
        //            g.Completed_Date!.Value > g.DueDate);

        //        double delayedGoalPercent =
        //            completedGoalsWithDate.Count == 0
        //                ? 0
        //                : (double)delayedGoalsCount *
        //                  100 /
        //                  completedGoalsWithDate.Count;

        //        // ============================================================
        //        // 16. MONTHLY GOAL TREND
        //        // ============================================================

        //        var monthlyTrend = goals
        //            .GroupBy(g => new
        //            {
        //                Year = g.StartDate.Year,
        //                Month = g.StartDate.Month
        //            })
        //            .Select(g => new
        //            {
        //                year = g.Key.Year,
        //                month = g.Key.Month,

        //                total = g.Count(),

        //                completed = g.Count(x =>
        //                    string.Equals(
        //                        x.Status,
        //                        "Completed",
        //                        StringComparison.OrdinalIgnoreCase)),

        //                pending = g.Count(x =>
        //                    !string.Equals(
        //                        x.Status,
        //                        "Completed",
        //                        StringComparison.OrdinalIgnoreCase)),

        //                overdue = g.Count(x =>
        //                    !string.Equals(
        //                        x.Status,
        //                        "Completed",
        //                        StringComparison.OrdinalIgnoreCase) &&
        //                    x.DueDate < DateTime.Now)
        //            })
        //            .OrderBy(x => x.year)
        //            .ThenBy(x => x.month)
        //            .ToList();

        //        // ============================================================
        //        // 17. YEARLY PRODUCTIVITY
        //        // ============================================================

        //        var monthlyData = await _context.MonthlyProductivity
        //            .Where(x =>
        //                x.StaffId == employeeId &&
        //                x.Year == year)
        //            .ToListAsync();

        //        int lastMonth;

        //        if (year == DateTime.Now.Year)
        //        {
        //            lastMonth = DateTime.Now.Month - 1;
        //        }
        //        else
        //        {
        //            lastMonth = 12;
        //        }

        //        var completedMonthData = monthlyData
        //            .Where(x =>
        //                x.Month >= 1 &&
        //                x.Month <= lastMonth)
        //            .ToList();

        //        double yearlyProductivity = 0;

        //        if (completedMonthData.Any())
        //        {
        //            yearlyProductivity = Math.Round(
        //                completedMonthData.Average(x =>
        //                    (double)x.TotalScore),
        //                2);
        //        }

        //        // ============================================================
        //        // 18. OVERDUE TASK LIST
        //        // ============================================================

        //        var overdueTaskList = allTasks
        //            .Where(t =>
        //                !string.Equals(
        //                    t.Status,
        //                    "Completed",
        //                    StringComparison.OrdinalIgnoreCase) &&
        //                t.Due_Date < DateTime.Now)
        //            .Select(t => new
        //            {
        //                taskCode = t.TaskCode,
        //                goalCode = t.GoalCode,
        //                task = t.Task,
        //                description = t.Description ?? "",
        //                priority = t.Priority ?? "",
        //                status = t.Status ?? "",
        //                createdAt = t.Created_At,
        //                dueDate = t.Due_Date,
        //                totalMembers = t.Members,
        //                wasEdited = t.wasEdited
        //            })
        //            .ToList();

        //        // ============================================================
        //        // 19. OVERDUE GOAL LIST
        //        // ============================================================

        //        var overdueGoalList = goals
        //            .Where(g =>
        //                !string.Equals(
        //                    g.Status,
        //                    "Completed",
        //                    StringComparison.OrdinalIgnoreCase) &&
        //                g.DueDate < DateTime.Now)
        //            .Select(g => new
        //            {
        //                goalId = g.Id,
        //                goalCode = g.GoalCode,
        //                goalType = g.GoalType,
        //                parentGoalId = g.ParentGoalId,
        //                goal = g.Title,
        //                status = g.Status ?? "",
        //                createdAt = g.StartDate,
        //                dueDate = g.DueDate,
        //                priority = g.Priority ?? "",
        //                progress = g.Progress
        //            })
        //            .ToList();

        //        // ============================================================
        //        // 20. LEAVE MONTHLY DATA
        //        // ============================================================

        //        var leaveData = await _context.LeaveForm
        //            .Where(l =>
        //                l.SenderId == employeeId &&

        //                l.FromDate >= yearStart &&
        //                l.FromDate < nextYearStart &&

        //                l.FromDate >= filterStart &&
        //                l.FromDate < filterEndExclusive &&

        //                string.Equals(
        //                    l.Status,
        //                    "Approved",
        //                    StringComparison.OrdinalIgnoreCase) &&

        //                !l.CompensationExtraWorkId.HasValue)
        //            .GroupBy(l => new
        //            {
        //                Year = l.FromDate.Year,
        //                Month = l.FromDate.Month
        //            })
        //            .Select(g => new
        //            {
        //                year = g.Key.Year,
        //                month = g.Key.Month,
        //                leaveDays = g.Sum(x => x.TotalDays ?? 0)
        //            })
        //            .ToListAsync();

        //        // ============================================================
        //        // 21. PERMISSION MONTHLY DATA
        //        // ============================================================

        //        var permissionData = await _context.PermissionForm
        //            .Where(p =>
        //                p.SenderId == employeeId &&

        //                p.Date >= yearStart &&
        //                p.Date < nextYearStart &&

        //                p.Date >= filterStart &&
        //                p.Date < filterEndExclusive &&

        //                string.Equals(
        //                    p.Status,
        //                    "Approved",
        //                    StringComparison.OrdinalIgnoreCase))
        //            .GroupBy(p => new
        //            {
        //                Year = p.Date.Year,
        //                Month = p.Date.Month
        //            })
        //            .Select(g => new
        //            {
        //                year = g.Key.Year,
        //                month = g.Key.Month,
        //                permissionHours = g.Sum(x => x.TotalHours)
        //            })
        //            .ToListAsync();

        //        // ============================================================
        //        // 22. COMBINE LEAVE + PERMISSION
        //        // ============================================================

        //        var leavePermissionMonthly = Enumerable
        //            .Range(1, 12)
        //            .Select(month => new
        //            {
        //                year = year,
        //                month = month,

        //                leave = leaveData
        //                    .Where(x => x.month == month)
        //                    .Select(x => x.leaveDays)
        //                    .FirstOrDefault(),

        //                permission = permissionData
        //                    .Where(x => x.month == month)
        //                    .Select(x => x.permissionHours)
        //                    .FirstOrDefault()
        //            })
        //            .ToList();

        //        // ============================================================
        //        // 23. GOAL DETAILS
        //        // ============================================================

        //        var goalDetails = goals
        //            .OrderBy(g =>
        //                string.Equals(
        //                    g.GoalType,
        //                    "Yearly",
        //                    StringComparison.OrdinalIgnoreCase)
        //                    ? 0
        //                    : 1)
        //            .ThenBy(g => g.StartDate)
        //            .Select(g => new
        //            {
        //                id = g.Id,
        //                goalCode = g.GoalCode,
        //                goalType = g.GoalType,
        //                parentGoalId = g.ParentGoalId,
        //                title = g.Title,
        //                priority = g.Priority ?? "",
        //                startDate = g.StartDate,
        //                dueDate = g.DueDate,
        //                completedDate = g.Completed_Date,
        //                status = g.Status ?? "",
        //                progress = g.Progress,
        //                goalPoints = g.Goalpoints,

        //                tasks = allTasks
        //                    .Where(t => t.GoalCode == g.GoalCode)
        //                    .Select(t => new
        //                    {
        //                        id = t.Id,
        //                        taskCode = t.TaskCode,
        //                        task = t.Task,
        //                        description = t.Description ?? "",
        //                        priority = t.Priority ?? "",
        //                        status = t.Status ?? "",
        //                        createdAt = t.Created_At,
        //                        dueDate = t.Due_Date,
        //                        completedDate = t.Completed_Date,
        //                        members = t.Members,
        //                        performanceType = t.PerformanceType,
        //                        quantity = t.Quantity,
        //                        startTime = t.StartTime,
        //                        endTime = t.EndTime
        //                    })
        //                    .ToList()
        //            })
        //            .ToList();

        //        // ============================================================
        //        // 24. FINAL RESPONSE
        //        // ============================================================

        //        return Ok(new
        //        {
        //            employeeId,
        //            employeeName = employee.Name,
        //            year,

        //            fromDate = filterStart,
        //            toDate = filterEndExclusive.AddTicks(-1),

        //            // ---------------- TASKS ----------------

        //            totalTasks,
        //            completedTasks,
        //            pendingTasks,
        //            overdueTasks,

        //            // ---------------- GOALS ----------------

        //            totalGoals,
        //            completedGoals,
        //            pendingGoals,
        //            overdueGoals,

        //            goalCompletionPercent =
        //                Math.Round(goalCompletionPercent, 2),

        //            goalOnTimePercent =
        //                Math.Round(goalOnTimePercent, 2),

        //            delayedGoalPercent =
        //                Math.Round(delayedGoalPercent, 2),

        //            // ---------------- PRODUCTIVITY ----------------

        //            yearlyProductivity,

        //            // ---------------- TREND ----------------

        //            monthlyTrend,

        //            // ---------------- OVERDUE ----------------

        //            overdueTaskList,
        //            overdueGoalList,

        //            // ---------------- LEAVE / PERMISSION ----------------

        //            leavePermissionMonthly,

        //            // ---------------- GOALS + TASKS ----------------

        //            goals = goalDetails
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "Error fetching employee report.",
        //            error = ex.Message,
        //            innerError = ex.InnerException?.Message
        //        });
        //    }
        //}


        [Authorize]
        [HttpGet("Staff/{employeeId}/Year/{year}")]
        public async Task<IActionResult> GetEmployeeReportByYear(
    int employeeId,
    int year,
    DateTime? fromDate,
    DateTime? toDate)
        {
            try
            {
                // ============================================================
                // 1. YEAR RANGE
                // ============================================================

                var yearStart = new DateTime(year, 1, 1);
                var nextYearStart = new DateTime(year + 1, 1, 1);

                // ============================================================
                // 2. CHECK EMPLOYEE
                // ============================================================

                var employee = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == employeeId);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        message = "Employee not found."
                    });
                }

                // ============================================================
                // 3. DATE FILTER
                // ============================================================

                DateTime filterStart = fromDate?.Date ?? yearStart;

                DateTime filterEndExclusive = toDate.HasValue
                    ? toDate.Value.Date.AddDays(1)
                    : nextYearStart;

                // Keep dates inside selected year
                if (filterStart < yearStart)
                    filterStart = yearStart;

                if (filterStart >= nextYearStart)
                    filterStart = yearStart;

                if (filterEndExclusive > nextYearStart)
                    filterEndExclusive = nextYearStart;

                if (filterEndExclusive <= filterStart)
                {
                    return BadRequest(new
                    {
                        message = "Invalid date range."
                    });
                }

                // ============================================================
                // 4. EMPLOYEE TASK PREFIX
                //
                // Assign_To example:
                // 12-John
                // 15-Keerthana
                // ============================================================

                var employeePrefix = employeeId + "-";

                // ============================================================
                // 5. GET TASKS DIRECTLY ASSIGNED TO EMPLOYEE
                // ============================================================

                var employeeTasks = await _context.Tasks
                    .Join(
                        _context.TaskMembers,
                        t => t.TaskCode,
                        tm => tm.TaskCode,
                        (t, tm) => new
                        {
                            Task = t,
                            Member = tm
                        })
                    .Where(x =>
                        x.Member.Assign_To != null &&
                        x.Member.Assign_To.StartsWith(employeePrefix) &&

                        x.Task.Created_At >= yearStart &&
                        x.Task.Created_At < nextYearStart &&

                        x.Task.Created_At >= filterStart &&
                        x.Task.Created_At < filterEndExclusive)
                    .Select(x => x.Task)
                    .Distinct()
                    .ToListAsync();

                // ============================================================
                // 6. GET EMPLOYEE GOAL ASSIGNMENTS
                // ============================================================

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => a.UserId == employeeId)
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                // ============================================================
                // 7. GET EMPLOYEE GOALS
                //
                // Monthly goals are filtered by selected date range.
                // ============================================================

                var goals = await _context.Goal
                    .Where(g =>
                        assignedGoalIds.Contains(g.Id) &&

                        g.StartDate >= yearStart &&
                        g.StartDate < nextYearStart &&

                        g.StartDate < filterEndExclusive &&
                        g.DueDate >= filterStart)
                    .ToListAsync();

                // ============================================================
                // 8. INCLUDE YEARLY PARENT GOALS
                //
                // Example:
                //
                // YG001
                //   |
                //   +-- MG001
                //   +-- MG002
                //
                // If employee has MG001, include YG001.
                // ============================================================

                var parentGoalIds = goals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g =>
                            parentGoalIds.Contains(g.Id) &&
                            g.GoalType == "Yearly")
                        .ToListAsync();

                    var existingGoalIds = goals
                        .Select(g => g.Id)
                        .ToHashSet();

                    foreach (var parentGoal in parentGoals)
                    {
                        if (!existingGoalIds.Contains(parentGoal.Id))
                        {
                            goals.Add(parentGoal);
                        }
                    }
                }

                // ============================================================
                // 9. GOAL CODES
                // ============================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
                    .Select(g => g.GoalCode!)
                    .Distinct()
                    .ToList();
                // ============================================================
                // 10. GET TASKS UNDER EMPLOYEE GOALS
                // ============================================================

                var goalTasks = new List<TaskTable>();

                if (goalCodes.Any())
                {
                    goalTasks = await _context.Tasks
                        .Join(
                            _context.TaskMembers,
                            t => t.TaskCode,
                            tm => tm.TaskCode,
                            (t, tm) => new
                            {
                                Task = t,
                                Member = tm
                            })
                        .Where(x =>
                            x.Member.Assign_To != null &&
                            x.Member.Assign_To.StartsWith(employeePrefix) &&

                            x.Task.GoalCode != null &&
                            goalCodes.Contains(x.Task.GoalCode) &&

                            x.Task.Created_At >= yearStart &&
                            x.Task.Created_At < nextYearStart &&

                            x.Task.Created_At >= filterStart &&
                            x.Task.Created_At < filterEndExclusive)
                        .Select(x => x.Task)
                        .Distinct()
                        .ToListAsync();
                }

                // ============================================================
                // 11. COMBINE TASKS
                // ============================================================

                var allTasks = employeeTasks
                    .Concat(goalTasks)
                    .GroupBy(t => t.Id)
                    .Select(g => g.First())
                    .ToList();

                // ============================================================
                // 12. TASK COUNTS
                // ============================================================

                int totalTasks = allTasks.Count;

                int completedTasks = allTasks.Count(t =>
                    string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int pendingTasks = allTasks.Count(t =>
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int overdueTasks = allTasks.Count(t =>
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase) &&
                    t.Due_Date < DateTime.Now);

                // ============================================================
                // 13. GOAL COUNTS
                // ============================================================

                int totalGoals = goals.Count;

                int completedGoals = goals.Count(g =>
                    string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int pendingGoals = goals.Count(g =>
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                int overdueGoals = goals.Count(g =>
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase) &&
                    g.DueDate < DateTime.Now);

                // ============================================================
                // 14. GOAL COMPLETION %
                // ============================================================

                double goalCompletionPercent = totalGoals == 0
                    ? 0
                    : (double)completedGoals * 100 / totalGoals;

                // ============================================================
                // 15. ON-TIME GOALS
                // ============================================================

                var completedGoalsWithDate = goals
                    .Where(g =>
                        string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        g.Completed_Date.HasValue)
                    .ToList();

                int onTimeGoals = completedGoalsWithDate.Count(g =>
                    g.Completed_Date!.Value <= g.DueDate);

                double goalOnTimePercent =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : (double)onTimeGoals *
                          100 /
                          completedGoalsWithDate.Count;

                // ============================================================
                // 16. DELAYED GOALS
                // ============================================================

                int delayedGoalsCount = completedGoalsWithDate.Count(g =>
                    g.Completed_Date!.Value > g.DueDate);

                double delayedGoalPercent =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : (double)delayedGoalsCount *
                          100 /
                          completedGoalsWithDate.Count;

                // ============================================================
                // 17. MONTHLY GOAL TREND
                // ============================================================

                var monthlyTrend = goals
                    .GroupBy(g => new
                    {
                        Year = g.StartDate.Year,
                        Month = g.StartDate.Month
                    })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,

                        total = g.Count(),

                        completed = g.Count(x =>
                            string.Equals(
                                x.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase)),

                        pending = g.Count(x =>
                            !string.Equals(
                                x.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase)),

                        overdue = g.Count(x =>
                            !string.Equals(
                                x.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase) &&
                            x.DueDate < DateTime.Now)
                    })
                    .OrderBy(x => x.year)
                    .ThenBy(x => x.month)
                    .ToList();

                // ============================================================
                // 18. PRODUCTIVITY
                // ============================================================

                var monthlyData = await _context.MonthlyProductivity
                    .Where(x =>
                        x.StaffId == employeeId &&
                        x.Year == year)
                    .ToListAsync();

                int lastMonth;

                if (year == DateTime.Now.Year)
                {
                    lastMonth = DateTime.Now.Month - 1;
                }
                else
                {
                    lastMonth = 12;
                }

                var completedMonthData = monthlyData
                    .Where(x =>
                        x.Month >= 1 &&
                        x.Month <= lastMonth)
                    .ToList();

                double yearlyProductivity = 0;

                if (completedMonthData.Any())
                {
                    yearlyProductivity = Math.Round(
                        completedMonthData.Average(x =>
                            (double)x.TotalScore),
                        2);
                }

                // ============================================================
                // 19. OVERDUE TASK LIST
                // ============================================================

                var overdueTaskList = allTasks
                    .Where(t =>
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        t.Due_Date < DateTime.Now)
                    .Select(t => new
                    {
                        id = t.Id,
                        taskCode = t.TaskCode,
                        goalCode = t.GoalCode,

                        task = t.Task,
                        description = t.Description ?? "",

                        priority = t.Priority ?? "",
                        status = t.Status ?? "",

                        createdAt = t.Created_At,
                        dueDate = t.Due_Date,

                        completedDate = t.Completed_Date,

                        totalMembers = t.Members,
                        wasEdited = t.wasEdited,

                        // TASK QUANTITY
                        targetQuantity = t.Quantity,
                        completedQuantity = t.CompletedQuantity ?? 0,

                        pendingQuantity = t.Quantity.HasValue
                            ? Math.Max(
                                0,
                                t.Quantity.Value -
                                (t.CompletedQuantity ?? 0))
                            : (int?)null
                    })
                    .ToList();

                // ============================================================
                // 20. OVERDUE GOAL LIST
                // ============================================================

                var overdueGoalList = goals
                    .Where(g =>
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        g.DueDate < DateTime.Now)
                    .Select(g => new
                    {
                        goalId = g.Id,
                        goalCode = g.GoalCode,
                        goalType = g.GoalType,
                        parentGoalId = g.ParentGoalId,

                        goal = g.Title,

                        status = g.Status ?? "",

                        createdAt = g.StartDate,
                        dueDate = g.DueDate,

                        priority = g.Priority ?? "",
                        progress = g.Progress,

                        // GOAL QUANTITY
                        targetQuantity = g.TargetQuantity,
                        completedQuantity = g.CompletedQuantity ?? 0,

                        pendingQuantity = g.TargetQuantity.HasValue
                            ? Math.Max(
                                0,
                                g.TargetQuantity.Value -
                                (g.CompletedQuantity ?? 0))
                            : (int?)null
                    })
                    .ToList();

                // ============================================================
                // 21. LEAVE MONTHLY DATA
                // ============================================================

                var leaveData = await _context.LeaveForm
                    .Where(l =>
                        l.SenderId == employeeId &&

                        l.FromDate >= yearStart &&
                        l.FromDate < nextYearStart &&

                        l.FromDate >= filterStart &&
                        l.FromDate < filterEndExclusive &&

                        l.Status != null &&
                        l.Status.Trim().ToLower() == "approved" &&

                        !l.CompensationExtraWorkId.HasValue)
                    .GroupBy(l => new
                    {
                        Year = l.FromDate.Year,
                        Month = l.FromDate.Month
                    })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,
                        leaveDays = g.Sum(x => x.TotalDays ?? 0)
                    })
                    .ToListAsync();

                // ============================================================
                // 22. PERMISSION MONTHLY DATA
                // ============================================================

                var permissionData = await _context.PermissionForm
                    .Where(p =>
                        p.SenderId == employeeId &&

                        p.Date >= yearStart &&
                        p.Date < nextYearStart &&

                        p.Date >= filterStart &&
                        p.Date < filterEndExclusive &&

                        p.Status != null &&
                        p.Status.Trim().ToLower() == "approved")
                    .GroupBy(p => new
                    {
                        Year = p.Date.Year,
                        Month = p.Date.Month
                    })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,
                        permissionHours = g.Sum(x => x.TotalHours)
                    })
                    .ToListAsync();

                // ============================================================
                // 23. LEAVE + PERMISSION MONTHLY
                // ============================================================

                var leavePermissionMonthly = Enumerable
                    .Range(1, 12)
                    .Select(month => new
                    {
                        year = year,
                        month = month,

                        leave = leaveData
                            .Where(x => x.month == month)
                            .Select(x => x.leaveDays)
                            .FirstOrDefault(),

                        permission = permissionData
                            .Where(x => x.month == month)
                            .Select(x => x.permissionHours)
                            .FirstOrDefault()
                    })
                    .ToList();

                // ============================================================
                // 24. GOAL DETAILS + TASK DETAILS
                // ============================================================

                var goalDetails = goals
                    .OrderBy(g =>
                        string.Equals(
                            g.GoalType,
                            "Yearly",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                    .ThenBy(g => g.StartDate)
                    .Select(g => new
                    {
                        id = g.Id,

                        goalCode = g.GoalCode,

                        goalType = g.GoalType,

                        parentGoalId = g.ParentGoalId,

                        title = g.Title,

                        priority = g.Priority ?? "",

                        startDate = g.StartDate,

                        dueDate = g.DueDate,

                        completedDate = g.Completed_Date,

                        status = g.Status ?? "",

                        progress = g.Progress,

                        goalPoints = g.Goalpoints,

                        // ====================================================
                        // GOAL QUANTITY
                        // ====================================================

                        targetQuantity = g.TargetQuantity,

                        completedQuantity = g.CompletedQuantity ?? 0,

                        pendingQuantity = g.TargetQuantity.HasValue
                            ? Math.Max(
                                0,
                                g.TargetQuantity.Value -
                                (g.CompletedQuantity ?? 0))
                            : (int?)null,

                        // ====================================================
                        // TASKS
                        // ====================================================

                        tasks = allTasks
                            .Where(t =>
                                !string.IsNullOrWhiteSpace(t.GoalCode) &&
                                t.GoalCode == g.GoalCode)
                            .OrderBy(t => t.Created_At)
                            .Select(t => new
                            {
                                id = t.Id,

                                taskCode = t.TaskCode,

                                task = t.Task,

                                description = t.Description ?? "",

                                priority = t.Priority ?? "",

                                status = t.Status ?? "",

                                createdAt = t.Created_At,

                                dueDate = t.Due_Date,

                                completedDate = t.Completed_Date,

                                members = t.Members,

                                performanceType = t.PerformanceType,

                                // =================================================
                                // TASK QUANTITY
                                // =================================================

                                targetQuantity = t.Quantity,

                                completedQuantity = t.CompletedQuantity ?? 0,

                                pendingQuantity = t.Quantity.HasValue
                                    ? Math.Max(
                                        0,
                                        t.Quantity.Value -
                                        (t.CompletedQuantity ?? 0))
                                    : (int?)null,

                                startTime = t.StartTime,

                                endTime = t.EndTime
                            })
                            .ToList()
                    })
                    .ToList();

                // ============================================================
                // 25. FINAL RESPONSE
                // ============================================================

                return Ok(new
                {
                    employeeId,

                    employeeName = employee.Name,

                    department = employee.Department,

                    role = employee.Role,

                    year,

                    fromDate = filterStart,

                    toDate = filterEndExclusive.AddTicks(-1),

                    // ========================================================
                    // TASK SUMMARY
                    // ========================================================

                    totalTasks,

                    completedTasks,

                    pendingTasks,

                    overdueTasks,

                    // ========================================================
                    // GOAL SUMMARY
                    // ========================================================

                    totalGoals,

                    completedGoals,

                    pendingGoals,

                    overdueGoals,

                    goalCompletionPercent =
                        Math.Round(
                            goalCompletionPercent,
                            2),

                    goalOnTimePercent =
                        Math.Round(
                            goalOnTimePercent,
                            2),

                    delayedGoalPercent =
                        Math.Round(
                            delayedGoalPercent,
                            2),

                    // ========================================================
                    // PRODUCTIVITY
                    // ========================================================

                    yearlyProductivity,

                    // ========================================================
                    // TREND
                    // ========================================================

                    monthlyTrend,

                    // ========================================================
                    // OVERDUE
                    // ========================================================

                    overdueTaskList,

                    overdueGoalList,

                    // ========================================================
                    // LEAVE / PERMISSION
                    // ========================================================

                    leavePermissionMonthly,

                    // ========================================================
                    // COMPLETE GOAL + TASK REPORT
                    // ========================================================

                    goals = goalDetails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Error fetching employee report.",

                        error = ex.Message,

                        innerError =
                            ex.InnerException?.Message
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


        [Authorize]
        [HttpGet("FilteredFullReport")]
        public async Task<IActionResult> GetFilteredFullReport(
    int? userId,
    string? department)
        {
            try
            {
                var today = DateTime.Today;

                // ============================================================
                // 1. FILTER USERS
                // ============================================================

                var usersQuery = _context.Users.AsQueryable();

                // Filter by specific user
                if (userId.HasValue)
                {
                    usersQuery = usersQuery
                        .Where(u => u.UserId == userId.Value);
                }

                // Filter by department
                if (!string.IsNullOrWhiteSpace(department))
                {
                    var departmentName = department.Trim();

                    usersQuery = usersQuery
                        .Where(u =>
                            u.Department != null &&
                            u.Department.Trim().ToLower() ==
                            departmentName.ToLower());
                }

                var userList = await usersQuery
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Department,
                        u.Role
                    })
                    .ToListAsync();

                // No users found
                if (!userList.Any())
                {
                    return Ok(new
                    {
                        users = new List<object>(),
                        tasks = new List<object>(),
                        goals = new List<object>(),
                        leaveList = new List<object>(),
                        permissionList = new List<object>()
                    });
                }

                var userIds = userList
                    .Select(u => u.UserId)
                    .ToList();


                // ============================================================
                // 2. TASKS
                // ============================================================

                var allTasks = await _context.Tasks
                    .ToListAsync();

                var allTaskMembers = await _context.TaskMembers
                    .ToListAsync();

                var allTaskReviews = await _context.TaskReview
                    .ToListAsync();


                // ------------------------------------------------------------
                // Get task members belonging to selected users
                //
                // Assign_To format:
                // "12-John"
                // "15-Keerthana"
                // ------------------------------------------------------------

                var employeePrefixes = userIds
                    .Select(id => id + "-")
                    .ToList();


                // ------------------------------------------------------------
                // Task members assigned to selected employees
                // ------------------------------------------------------------

                var selectedTaskMembers = allTaskMembers
                    .Where(tm =>
                        !string.IsNullOrWhiteSpace(tm.Assign_To) &&
                        employeePrefixes.Any(prefix =>
                            tm.Assign_To.StartsWith(prefix)))
                    .ToList();


                // ------------------------------------------------------------
                // Get task codes belonging to selected employees
                // ------------------------------------------------------------

                var employeeTaskCodes = selectedTaskMembers
                    .Where(tm => !string.IsNullOrWhiteSpace(tm.TaskCode))
                    .Select(tm => tm.TaskCode)
                    .Distinct()
                    .ToHashSet();


                // ------------------------------------------------------------
                // Build task response
                // ------------------------------------------------------------

                var tasks = allTasks
                    .Where(t =>
                        !string.IsNullOrWhiteSpace(t.TaskCode) &&
                        employeeTaskCodes.Contains(t.TaskCode))
                    .Select(t =>
                    {
                        // Reviews are currently matched by TaskCode,
                        // same as your existing implementation.
                        var review = allTaskReviews
                            .FirstOrDefault(tr =>
                                tr.TaskCode == t.TaskCode);

                        //return new
                        //{
                        //    taskCode = t.TaskCode,

                        //    task = t.Task,

                        //    description = t.Description ?? "",

                        //    status = t.Status ?? "",

                        //    priority = t.Priority ?? "",

                        //    members = t.Members,

                        //    createdAt = t.Created_At,

                        //    dueDate = t.Due_Date,

                        //    completedDate = t.Completed_Date,

                        //    points = review?.FinalPoints ?? 0,

                        //    isOverdue =
                        //        !string.Equals(
                        //            t.Status,
                        //            "Completed",
                        //            StringComparison.OrdinalIgnoreCase) &&
                        //        t.Due_Date < today
                        //};
                        return new
                        {
                            taskCode = t.TaskCode,

                            task = t.Task,

                            description = t.Description ?? "",

                            status = t.Status ?? "",

                            priority = t.Priority ?? "",

                            members = t.Members,

                            createdAt = t.Created_At,

                            dueDate = t.Due_Date,

                            completedDate = t.Completed_Date,

                            points = review?.FinalPoints ?? 0,

                            // =====================================================
                            // TASK QUANTITY
                            // =====================================================

                            targetQuantity = t.Quantity,

                            completedQuantity = t.CompletedQuantity ?? 0,

                            isOverdue =
        !string.Equals(
            t.Status,
            "Completed",
            StringComparison.OrdinalIgnoreCase) &&
        t.Due_Date < today
                        };
                    })
                    .ToList();


                // ============================================================
                // 3. GOALS
                // ============================================================

                // ------------------------------------------------------------
                // Get goals assigned to selected users through GoalAssignment
                // ------------------------------------------------------------

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => userIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                var allGoals = await _context.Goal
                    .Where(g => assignedGoalIds.Contains(g.Id))
                    .ToListAsync();


                // ------------------------------------------------------------
                // Include Yearly parent goals
                //
                // Example:
                //
                // YG001
                //   └── MG001
                //
                // If MG001 belongs to selected user,
                // include YG001 also.
                // ------------------------------------------------------------

                var parentGoalIds = allGoals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g =>
                            parentGoalIds.Contains(g.Id) &&
                            g.GoalType == "Yearly")
                        .ToListAsync();

                    var existingGoalIds = allGoals
                        .Select(g => g.Id)
                        .ToHashSet();

                    foreach (var parentGoal in parentGoals)
                    {
                        if (!existingGoalIds.Contains(parentGoal.Id))
                        {
                            allGoals.Add(parentGoal);
                        }
                    }
                }


                // ------------------------------------------------------------
                // Goal pending quantity
                // ------------------------------------------------------------

                int? GetPendingQuantity(Goal goal)
                {
                    if (!goal.TargetQuantity.HasValue)
                        return null;

                    int completed =
                        goal.CompletedQuantity ?? 0;

                    return Math.Max(
                        0,
                        goal.TargetQuantity.Value - completed
                    );
                }


                // ------------------------------------------------------------
                // Goal response
                // ------------------------------------------------------------

                //var goals = allGoals
                //    .OrderBy(g =>
                //        string.Equals(
                //            g.GoalType,
                //            "Yearly",
                //            StringComparison.OrdinalIgnoreCase)
                //            ? 0
                //            : 1)
                //    .ThenBy(g => g.StartDate)
                //    .Select(g => new
                //    {
                //        goalId = g.Id,

                //        goalCode = g.GoalCode,

                //        goalType = g.GoalType,

                //        parentGoalId = g.ParentGoalId,

                //        title = g.Title,

                //        priority = g.Priority ?? "",

                //        status = g.Status ?? "",

                //        startDate = g.StartDate,

                //        dueDate = g.DueDate,

                //        completedDate = g.Completed_Date,

                //        progress = g.Progress,

                //        points = g.Goalpoints,

                //        isOverdue =
                //            !string.Equals(
                //                g.Status,
                //                "Completed",
                //                StringComparison.OrdinalIgnoreCase) &&
                //            g.DueDate < today,


                //        // =====================================================
                //        // IMPORTANT:
                //        // Only tasks assigned to selected users
                //        // are included here.
                //        // =====================================================

                //        tasks = allTasks
                //            .Where(t =>
                //                t.GoalCode == g.GoalCode &&
                //                !string.IsNullOrWhiteSpace(t.TaskCode) &&
                //                employeeTaskCodes.Contains(t.TaskCode))
                //            .Select(t => new
                //            {
                //                taskCode = t.TaskCode,

                //                task = t.Task,

                //                description = t.Description ?? "",

                //                status = t.Status ?? "",

                //                priority = t.Priority ?? "",

                //                dueDate = t.Due_Date,

                //                completedDate = t.Completed_Date,

                //                members = t.Members
                //            })
                //            .ToList()
                //    })
                //    .ToList();
                var goals = allGoals
    .OrderBy(g =>
        string.Equals(
            g.GoalType,
            "Yearly",
            StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1)
    .ThenBy(g => g.StartDate)
    .Select(g => new
    {
        goalId = g.Id,
        goalCode = g.GoalCode,
        goalType = g.GoalType,
        parentGoalId = g.ParentGoalId,

        title = g.Title,
        priority = g.Priority ?? "",
        status = g.Status ?? "",

        startDate = g.StartDate,
        dueDate = g.DueDate,
        completedDate = g.Completed_Date,

        progress = g.Progress,
        points = g.Goalpoints,

        // =====================================================
        // GOAL QUANTITY
        // =====================================================

        targetQuantity = g.TargetQuantity,

        completedQuantity = g.CompletedQuantity ?? 0,

        pendingQuantity = GetPendingQuantity(g),

        isOverdue =
            !string.Equals(
                g.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase) &&
            g.DueDate < today,

        // =====================================================
        // TASKS UNDER THIS GOAL
        // =====================================================

        tasks = allTasks
            .Where(t =>
                t.GoalCode == g.GoalCode &&
                !string.IsNullOrWhiteSpace(t.TaskCode) &&
                employeeTaskCodes.Contains(t.TaskCode))
            .Select(t => new
            {
                taskCode = t.TaskCode,

                task = t.Task,

                description = t.Description ?? "",

                status = t.Status ?? "",

                priority = t.Priority ?? "",

                dueDate = t.Due_Date,

                completedDate = t.Completed_Date,

                members = t.Members,

                // =================================================
                // TASK QUANTITY
                // =================================================

                targetQuantity = t.Quantity,

                completedQuantity = t.CompletedQuantity ?? 0
            })
            .ToList()
    })
    .ToList();

                // ============================================================
                // 4. LEAVES
                // ============================================================

                var leaves = await _context.LeaveForm
                    .Where(l =>
                        l.LeaveType != "Holiday" &&
                        userIds.Contains(l.SenderId))
                    .ToListAsync();


                // ------------------------------------------------------------
                // Compensation IDs
                // ------------------------------------------------------------

                var compensationIds = leaves
                    .Where(l =>
                        !string.IsNullOrWhiteSpace(l.Status) &&
                        l.Status.Trim().Equals(
                            "Approved",
                            StringComparison.OrdinalIgnoreCase) &&
                        l.CompensationExtraWorkId.HasValue)
                    .Select(l =>
                        l.CompensationExtraWorkId!.Value)
                    .Distinct()
                    .ToList();


                // ------------------------------------------------------------
                // Compensation data
                // ------------------------------------------------------------

                var compensationData = await _context.ExtraWork
                    .Where(x => compensationIds.Contains(x.Id))
                    .ToListAsync();


                // ------------------------------------------------------------
                // Leave response
                // ------------------------------------------------------------

                var leaveList = leaves
                    .Select(l =>
                    {
                        var compensation =
                            l.CompensationExtraWorkId.HasValue
                                ? compensationData.FirstOrDefault(x =>
                                    x.Id ==
                                    l.CompensationExtraWorkId.Value)
                                : null;

                        bool isApproved =
                            !string.IsNullOrWhiteSpace(l.Status) &&
                            l.Status.Trim().Equals(
                                "Approved",
                                StringComparison.OrdinalIgnoreCase);

                        return new
                        {
                            leaveId = l.Id,

                            employeeId = l.SenderId,

                            type = l.LeaveType,

                            status = l.Status ?? "",

                            fromDate = l.FromDate,

                            submdate = l.SubmittedDate,

                            reason = l.Reason ?? "",

                            rejreason = l.RejectionReason,

                            approvedate = l.ApprovedDate,

                            contactno = l.ContactNumber,

                            leavecategory = l.LeaveTyp,

                            compensationUsed =
                                isApproved &&
                                l.CompensationExtraWorkId.HasValue,

                            compensationExtraWorkId =
                                l.CompensationExtraWorkId,

                            compensationDate =
                                compensation?.WorkDate
                        };
                    })
                    .ToList();


                // ============================================================
                // 5. PERMISSIONS
                // ============================================================

                var permissionList = await _context.PermissionForm
                    .Where(p => userIds.Contains(p.SenderId))
                    .Select(p => new
                    {
                        permissionId = p.Id,

                        employeeId = p.SenderId,

                        date = p.Date,

                        fromTime = p.FromTime,

                        toTime = p.ToTime,

                        reason = p.Reason ?? "",

                        status = p.Status ?? "",

                        totalHours = p.TotalHours,

                        submittedDate = p.SubmittedDate
                    })
                    .ToListAsync();


                // ============================================================
                // 6. FINAL RESPONSE
                // ============================================================

                return Ok(new
                {
                    users = userList,

                    tasks,

                    goals,

                    leaveList,

                    permissionList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Error fetching filtered report.",

                        error = ex.Message,

                        innerError =
                            ex.InnerException?.Message
                    }
                );
            }
        }


    }
}
