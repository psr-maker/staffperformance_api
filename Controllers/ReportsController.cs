
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
        public async Task<IActionResult> GetDepartmentSummary(
            string departmentName,
            DateTime? fromDate,
            DateTime? toDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(departmentName))
                    return BadRequest(new { message = "Department name is required." });

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

                int totalUsers = users.Count;

                var departmentUserIds = users
                    .Select(u => u.UserId)
                    .ToList();

                // ============================================================
                // 2. GOAL ASSIGNMENTS
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
                // ============================================================

                var parentGoalIds = goals
                    .Where(g => g.ParentGoalId.HasValue &&
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
                            goals.Add(parentGoal);
                    }
                }

                // ============================================================
                // 5. DATE FILTER
                // ============================================================

                if (fromDate.HasValue || toDate.HasValue)
                {
                    DateTime? startDate = fromDate?.Date;
                    DateTime? endDate = toDate?.Date;

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
                // 7. GET TASKS
                // ============================================================

                var tasks = await _context.Tasks
                    .Where(t =>
                        t.GoalCode != null &&
                        goalCodes.Contains(t.GoalCode))
                    .ToListAsync();

                // ============================================================
                // 8. TASK MEMBERS
                // ============================================================

                var taskCodes = tasks
                    .Where(t => !string.IsNullOrWhiteSpace(t.TaskCode))
                    .Select(t => t.TaskCode)
                    .Distinct()
                    .ToList();

                var taskMembers = await _context.TaskMembers
                    .Where(tm => taskCodes.Contains(tm.TaskCode))
                    .ToListAsync();

                // ============================================================
                // 9. GOAL COUNTS
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

                double goalCompletionPercentage = totalGoals > 0
                    ? Math.Round((double)completedGoals * 100 / totalGoals, 2)
                    : 0;

                // ============================================================
                // 10. ON-TIME AND DELAYED GOALS
                // ============================================================

                int onTimeGoals = goals.Count(g =>
                    string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase) &&
                    g.Completed_Date.HasValue &&
                    g.Completed_Date.Value <= g.DueDate);

                double onTimeGoalCompletionPercentage = completedGoals > 0
                    ? Math.Round((double)onTimeGoals * 100 / completedGoals, 2)
                    : 0;

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

                double delayedGoalPercentage = completedGoalsList.Count > 0
                    ? Math.Round(
                        (double)delayedGoals * 100 / completedGoalsList.Count, 2)
                    : 0;

                // ============================================================
                // 11. TASK COUNTS
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

                double taskCompletionPercentage = totalTasks > 0
                    ? Math.Round((double)completedTasks * 100 / totalTasks, 2)
                    : 0;

                // ============================================================
                // 12. ON-TIME AND DELAYED TASKS
                // ============================================================


                int onTimeTasks = tasks.Count(t =>
                    string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase) &&
                    t.Completed_Date <= t.Due_Date);


                double onTimeTaskCompletionPercentage = completedTasks > 0
                    ? Math.Round((double)onTimeTasks * 100 / completedTasks, 2)
                    : 0;


                var completedTasksList = tasks
                    .Where(t =>
                        string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int delayedTasks = completedTasksList.Count(t =>
                    t.Completed_Date > t.Due_Date);

                double delayedTaskPercentage = completedTasksList.Count > 0
                    ? Math.Round(
                        (double)delayedTasks * 100 / completedTasksList.Count, 2)
                    : 0;

             

                // ============================================================
                // 13. OVERDUE GOALS LIST
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
                // 14. OVERDUE TASK LIST
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
                // 15. EMPLOYEE PERFORMANCE AND PRODUCTIVITY
                // ============================================================

                int selectedYear = toDate?.Year
                    ?? fromDate?.Year
                    ?? DateTime.Now.Year;

                int lastMonth = selectedYear == DateTime.Now.Year
                    ? DateTime.Now.Month - 1
                    : 12;

                var monthlyRecords = await _context.MonthlyProductivity
                    .Where(x =>
                        departmentUserIds.Contains(x.StaffId) &&
                        x.Year == selectedYear)
                    .ToListAsync();

                var latestStaffScores = monthlyRecords
                    .GroupBy(x => x.StaffId)
                    .Select(group => group
                        .OrderByDescending(x => x.Year)
                        .ThenByDescending(x => x.Month)
                        .First())
                    .ToList();

                var staffScores = users
                    .Select(user =>
                    {
                        var score = latestStaffScores
                            .FirstOrDefault(x => x.StaffId == user.UserId);

                        decimal staffTask = score == null
                            ? 0m
                            : Math.Clamp(score.TaskPoints / 45m * 35m, 0m, 35m);

                        decimal staffGoal = score == null
                            ? 0m
                            : Math.Clamp(score.GoalPoints, 0m, 40m);

                        decimal staffProductivity = score == null
                            ? 0m
                            : Math.Clamp(
                                (staffTask + staffGoal) / 75m * 100m,
                                0m, 100m);

                        return new
                        {
                            employeeId = user.UserId,
                            employeeName = user.Name,
                            department = user.Department,
                            productivity = Math.Round(staffProductivity, 2),
                            performance = score?.TotalScore ?? 0m,
                            scoreYear = score?.Year,
                            scoreMonth = score?.Month
                        };
                    })
                    .ToList();

                // ============================================================
                // 16. DEPARTMENT MONTHLY AND YEARLY SCORES
                // ============================================================

                var fiveSRecords = await _context.FiveSPoints
                    .Where(x =>
                        x.Department != null &&
                        x.Department.Trim().ToLower() ==
                            departmentName.Trim().ToLower() &&
                        x.Year == selectedYear &&
                        x.Month <= lastMonth)
                    .ToListAsync();

                var departmentMonthlyScores = new List<object>();

                var yearlyProductivityValues = new List<decimal>();
                var yearlyPerformanceValues = new List<decimal>();

                for (int month = 1; month <= lastMonth; month++)
                {
                    var staffData = monthlyRecords
                        .Where(x => x.Month == month)
                        .ToList();

                    decimal averageTask = staffData.Any()
                        ? staffData.Average(x => x.TaskPoints)
                        : 0m;

                    // Stored task score is out of 45; department task score is out of 35.
                    decimal taskPoints = Math.Clamp(
                        averageTask / 45m * 35m,
                        0m, 35m);

                    decimal averageGoal = staffData.Any()
                        ? staffData.Average(x => x.GoalPoints)
                        : 0m;

                    // Goal score is out of 40.
                    decimal goalPoints = Math.Clamp(
                        averageGoal, 0m, 40m);

                    // Department productivity: Goal 40 + Task 35 = 75.
                    decimal rawProductivity = taskPoints + goalPoints;

                    // Return productivity out of 100.
                    decimal monthlyProductivity = Math.Clamp(
                        rawProductivity / 75m * 100m,
                        0m, 100m);

                    decimal attitudeScore = staffData.Any()
                        ? staffData.Average(x => x.AttitudeScore)
                        : 0m;

                    attitudeScore = Math.Clamp(attitudeScore, 0m, 15m);

                    decimal fiveSRaw = fiveSRecords
                        .Where(x => x.Month == month)
                        .Select(x => (decimal?)x.Points)
                        .Average() ?? 0m;

                    // Convert 5S from 100 points to 10 points.
                    decimal fiveS = Math.Clamp(
                        fiveSRaw / 100m * 10m,
                        0m, 10m);

                    // Department performance: Task + Goal + Attitude + 5S.
                    decimal monthlyPerformance = Math.Clamp(
                        rawProductivity + attitudeScore + fiveS,
                        0m, 100m);

                    monthlyProductivity = Math.Round(monthlyProductivity, 2);
                    monthlyPerformance = Math.Round(monthlyPerformance, 2);

                    departmentMonthlyScores.Add(new
                    {
                        month,
                        taskPoints = Math.Round(taskPoints, 2),
                        goalPoints = Math.Round(goalPoints, 2),
                        productivity = monthlyProductivity,
                        attitudeScore = Math.Round(attitudeScore, 2),
                        fiveS = Math.Round(fiveS, 2),
                        performance = monthlyPerformance
                    });

                    // Calculate yearly averages only for months with staff records.
                    // This avoids treating missing months as zero performance.
                    if (staffData.Any())
                    {
                        yearlyProductivityValues.Add(monthlyProductivity);
                        yearlyPerformanceValues.Add(monthlyPerformance);
                    }
                }

                decimal yearlyProductivity = yearlyProductivityValues.Any()
                    ? Math.Round(yearlyProductivityValues.Average(), 2)
                    : 0m;

                decimal yearlyPerformance = yearlyPerformanceValues.Any()
                    ? Math.Round(yearlyPerformanceValues.Average(), 2)
                    : 0m;

                // Keep these names if Sections 17 and 18 use them.
                double departmentProductivity = (double)yearlyProductivity;
                double departmentPerformance = (double)yearlyPerformance;


                // ============================================================
                // 17. TOP PERFORMERS
                // Either performance OR productivity must be above 80.
                // ============================================================

                var topPerformers = staffScores
                    .Where(x =>
                        x.performance > 80 ||
                        x.productivity > 80)
                    .OrderByDescending(x => Math.Max(
                        (double)x.performance,
                        (double)x.productivity))
                    .ToList();

                // ============================================================
                // 18. LOW PERFORMERS
                // Either performance OR productivity must be 50 or below.
                // ============================================================

                var lowPerformers = staffScores
                    .Where(x =>
                        x.performance <= 50 ||
                        x.productivity <= 50)
                    .OrderBy(x => Math.Min(
                        (double)x.performance,
                        (double)x.productivity))
                    .ToList();

                // ============================================================
                // 19. GOAL DETAILS WITH TASKS
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
                // 20. FINAL RESPONSE
                // ============================================================

                return Ok(new
                {
                    department = departmentName,
                    totalUsers,

                    // GOALS
                    totalGoals,
                    completedGoals,
                    pendingGoals,
                    overdueGoals,
                    goalCompletionPercentage,
                    onTimeGoals,
                    onTimeGoalCompletionPercentage,
                    delayedGoals,
                    delayedGoalPercentage,

                    // TASKS
                    totalTasks,
                    completedTasks,
                    pendingTasks,
                    overdueTasks,
                    taskCompletionPercentage,
                    onTimeTasks,
                    onTimeTaskCompletionPercentage,
                    delayedTasks,
                    delayedTaskPercentage,
                    // DEPARTMENT YEARLY SCORES
                    scoreYear = selectedYear,
                    departmentYearlyProductivity = departmentProductivity,
                    departmentYearlyPerformance = departmentPerformance,

                    // DEPARTMENT MONTHLY SCORES
                    departmentMonthlyScores,

                    // EMPLOYEE SCORES AND PERFORMERS
                    employees = staffScores,
                    topPerformers,
                    lowPerformers,

                    // EXISTING DETAILS
                    overdueGoalsList,
                    overdueTasksList,
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
                if (string.IsNullOrWhiteSpace(departmentName))
                {
                    return BadRequest(new
                    {
                        message = "Department name is required."
                    });
                }

                if (year < 2000 || year > DateTime.Now.Year)
                {
                    return BadRequest(new
                    {
                        message = "Invalid year."
                    });
                }

                var result = new List<object>();

                int currentMonth = DateTime.Now.Month;
                int lastMonth = year == DateTime.Now.Year
                    ? currentMonth - 1
                    : 12;

                if (lastMonth <= 0)
                {
                    return Ok(new
                    {
                        department = departmentName,
                        year,
                        monthsReturned = 0,
                        monthlyData = result
                    });
                }

                var normalizedDepartment = departmentName.Trim().ToLower();

                var userIds = await _context.Users
                    .Where(u =>
                        u.Department != null &&
                        u.Department.Trim().ToLower() == normalizedDepartment)
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

                for (int month = 1; month <= lastMonth; month++)
                {
                    var staffData = await _context.MonthlyProductivity
                        .Where(x =>
                            userIds.Contains(x.StaffId) &&
                            x.Month == month &&
                            x.Year == year)
                        .ToListAsync();

                    // Average staff task points.
                    // Convert from 45-point scale to 35-point scale.
                    decimal averageTask = staffData.Any()
                        ? staffData.Average(x => x.TaskPoints)
                        : 0m;

                    decimal taskPoints = Math.Clamp(
                        (averageTask / 45m) * 35m,
                        0m,
                        35m);

                    // Average staff goal points.
                    // Goal maximum = 40.
                    decimal averageGoal = staffData.Any()
                        ? staffData.Average(x => x.GoalPoints)
                        : 0m;

                    decimal goalPoints = Math.Clamp(
                        averageGoal,
                        0m,
                        40m);

                    // Department productivity = Task + Goal (out of 75).
                    decimal productivity = Math.Clamp(
                        taskPoints + goalPoints,
                        0m,
                        75m);

                    // Attitude maximum = 15.
                    decimal attitudeScore = staffData.Any()
                        ? staffData.Average(x => x.AttitudeScore)
                        : 0m;

                    attitudeScore = Math.Clamp(
                        attitudeScore,
                        0m,
                        15m);

                    // Get department 5S score for this month.
                    decimal fiveSRaw = await _context.FiveSPoints
                        .Where(x =>
                            x.Department != null &&
                            x.Department.Trim().ToLower() == normalizedDepartment &&
                            x.Year == year &&
                            x.Month == month)
                        .Select(x => (decimal?)x.Points)
                        .AverageAsync() ?? 0m;

                    // Convert 5S from 100 points to 10 points.
                    decimal fiveS = Math.Clamp(
                        (fiveSRaw / 100m) * 10m,
                        0m,
                        10m);

                    // Department performance = Productivity + Attitude + 5S.
                    decimal totalScore = Math.Clamp(
                        productivity + attitudeScore + fiveS,
                        0m,
                        100m);

                    result.Add(new
                    {
                        month,
                        taskPoints = Math.Round(taskPoints, 2),
                        goalPoints = Math.Round(goalPoints, 2),
                        productivity = Math.Round(productivity, 2),
                        attitudeScore = Math.Round(attitudeScore, 2),
                        fiveS = Math.Round(fiveS, 2),
                        totalScore = Math.Round(totalScore, 2)
                    });
                }

                return Ok(new
                {
                    department = departmentName.Trim(),
                    year,
                    monthsReturned = result.Count,
                    monthlyData = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching department monthly productivity.",
                    error = ex.Message
                });
            }
        }



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
                // =========================================================
                // 1. VALIDATE YEAR AND DATE RANGE
                // =========================================================

                if (year < 1 || year >= 9999)
                {
                    return BadRequest(new { message = "Invalid year." });
                }

                if (fromDate.HasValue &&
                    toDate.HasValue &&
                    fromDate.Value.Date > toDate.Value.Date)
                {
                    return BadRequest(new
                    {
                        message = "From date cannot be after To date."
                    });
                }

                var now = DateTime.Now;
                var yearStart = new DateTime(year, 1, 1);
                var nextYearStart = new DateTime(year + 1, 1, 1);

                DateTime filterStart = fromDate?.Date ?? yearStart;

                DateTime filterEndExclusive = toDate.HasValue
                    ? toDate.Value.Date.AddDays(1)
                    : nextYearStart;

                if (filterStart < yearStart)
                    filterStart = yearStart;

                if (filterEndExclusive > nextYearStart)
                    filterEndExclusive = nextYearStart;

                if (filterStart >= nextYearStart ||
                    filterEndExclusive <= filterStart)
                {
                    return BadRequest(new
                    {
                        message = "Invalid date range for the selected year."
                    });
                }

                // =========================================================
                // 2. EMPLOYEE DETAILS
                // =========================================================

                var employee = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == employeeId);

                if (employee == null)
                {
                    return NotFound(new
                    {
                        message = "Employee not found."
                    });
                }

                var employeePrefix = employeeId + "-";

                // =========================================================
                // 3. GET EMPLOYEE TASKS
                // =========================================================

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
                        x.Task.Created_At >= filterStart &&
                        x.Task.Created_At < filterEndExclusive)
                    .Select(x => x.Task)
                    .Distinct()
                    .ToListAsync();

                // =========================================================
                // 4. GET EMPLOYEE-ASSIGNED GOALS
                // =========================================================

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => a.UserId == employeeId)
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                var goals = await _context.Goal
                    .Where(g =>
                        assignedGoalIds.Contains(g.Id) &&
                        g.StartDate < filterEndExclusive &&
                        g.DueDate >= filterStart &&
                        g.StartDate < nextYearStart &&
                        g.DueDate >= yearStart)
                    .ToListAsync();

                // =========================================================
                // 5. INCLUDE YEARLY PARENT GOALS
                // =========================================================

                var parentGoalIds = goals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Count > 0)
                {
                    var parentGoals = await _context.Goal
                        .Where(g =>
                            parentGoalIds.Contains(g.Id) &&
                            g.GoalType == "Yearly" &&
                            g.StartDate < nextYearStart &&
                            g.DueDate >= yearStart)
                        .ToListAsync();

                    var existingGoalIds = goals
                        .Select(g => g.Id)
                        .ToHashSet();

                    foreach (var parent in parentGoals)
                    {
                        if (existingGoalIds.Add(parent.Id))
                            goals.Add(parent);
                    }
                }

                // =========================================================
                // 6. GET TASKS LINKED TO THESE GOALS
                // =========================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
                    .Select(g => g.GoalCode!)
                    .Distinct()
                    .ToList();

                var goalTasks = new List<TaskTable>();

                if (goalCodes.Count > 0)
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
                            x.Task.Created_At >= filterStart &&
                            x.Task.Created_At < filterEndExclusive)
                        .Select(x => x.Task)
                        .Distinct()
                        .ToListAsync();
                }

                // =========================================================
                // 7. COMBINE AND DEDUPLICATE TASKS
                // =========================================================

                var allTasks = employeeTasks
                    .Concat(goalTasks)
                    .GroupBy(t => t.Id)
                    .Select(g => g.First())
                    .ToList();

                bool IsCompleted(string? status) =>
                    string.Equals(
                        status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase);

                // =========================================================
                // 8. TASK SUMMARY
                // =========================================================

                int totalTasks = allTasks.Count;

                int completedTasks = allTasks.Count(t =>
                    IsCompleted(t.Status));

                int pendingTasks = totalTasks - completedTasks;

                int overdueTasks = allTasks.Count(t =>
                    !IsCompleted(t.Status) &&
                    t.Due_Date < now);

                double taskCompletionPercent = totalTasks == 0
                    ? 0
                    : Math.Round(
                        completedTasks * 100.0 / totalTasks, 2);

                // Completed tasks with a valid completion date
                var completedTasksWithDate = allTasks
                    .Where(t =>
                        IsCompleted(t.Status) &&
                        t.Completed_Date != default(DateTime))
                    .ToList();

                int onTimeTasks = completedTasksWithDate.Count(t =>
                    t.Completed_Date <= t.Due_Date);

                int delayedTasks = completedTasksWithDate.Count(t =>
                    t.Completed_Date > t.Due_Date);

                double taskOnTimePercent =
                    completedTasksWithDate.Count == 0
                        ? 0
                        : Math.Round(
                            onTimeTasks * 100.0 /
                            completedTasksWithDate.Count, 2);

                double delayedTaskPercent =
                    completedTasksWithDate.Count == 0
                        ? 0
                        : Math.Round(
                            delayedTasks * 100.0 /
                            completedTasksWithDate.Count, 2);

                // =========================================================
                // 9. GOAL SUMMARY
                // =========================================================

                int totalGoals = goals.Count;

                int completedGoals = goals.Count(g =>
                    IsCompleted(g.Status));

                int pendingGoals = totalGoals - completedGoals;

                int overdueGoals = goals.Count(g =>
                    !IsCompleted(g.Status) &&
                    g.DueDate < now);

                double goalCompletionPercent = totalGoals == 0
                    ? 0
                    : Math.Round(
                        completedGoals * 100.0 / totalGoals, 2);

                // Completed goals with a valid completion date
                var completedGoalsWithDate = goals
                    .Where(g =>
                        IsCompleted(g.Status) &&
                        g.Completed_Date != default(DateTime))
                    .ToList();

                int onTimeGoals = completedGoalsWithDate.Count(g =>
                    g.Completed_Date <= g.DueDate);

                int delayedGoalsCount = completedGoalsWithDate.Count(g =>
                    g.Completed_Date > g.DueDate);

                double goalOnTimePercent =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : Math.Round(
                            onTimeGoals * 100.0 /
                            completedGoalsWithDate.Count, 2);

                double delayedGoalPercent =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : Math.Round(
                            delayedGoalsCount * 100.0 /
                            completedGoalsWithDate.Count, 2);

                // =========================================================
                // 10. YEARLY AND MONTHLY PRODUCTIVITY / PERFORMANCE
                // =========================================================

                var monthlyData = await _context.MonthlyProductivity
                    .Where(x =>
                        x.StaffId == employeeId &&
                        x.Year == year &&
                        x.Month >= 1 &&
                        x.Month <= 12)
                    .ToListAsync();

                // Exclude current and future months.
                int lastCompletedMonth = year < now.Year
                    ? 12
                    : year == now.Year
                        ? now.Month - 1
                        : 0;

                // Completed months only.
                var completedMonthData = monthlyData
                    .Where(x => x.Month <= lastCompletedMonth)
                    .ToList();

                // YEARLY PRODUCTIVITY OUT OF 100
                double yearlyProductivity = completedMonthData.Count == 0
                    ? 0
                    : Math.Round(
                        completedMonthData.Average(
                            x => (double)x.Productivity / 85.0 * 100.0),
                        2);

                // YEARLY PERFORMANCE OUT OF 100
                double yearlyPerformance = completedMonthData.Count == 0
                    ? 0
                    : Math.Round(
                        completedMonthData.Average(
                            x => (double)x.TotalScore),
                        2);

                // MONTHLY PRODUCTIVITY AND PERFORMANCE OUT OF 100
                var monthlyProductivityReport = Enumerable
                    .Range(1, 12)
                    .Select(month =>
                    {
                        var data = monthlyData.FirstOrDefault(
                            x => x.Month == month);

                        return new
                        {
                            month,

                            // Convert productivity from 85 to 100
                            productivity = data == null
                                ? 0
                                : Math.Round(
                                    (double)data.Productivity / 85.0 * 100.0,
                                    2),

                            // Already out of 100
                            performance = data?.TotalScore ?? 0
                        };
                    })
                    .ToList();

                // =========================================================
                // 11. MONTHLY GOAL TREND
                // =========================================================

                var monthlyTrend = goals
                    .Where(g =>
                        g.StartDate >= filterStart &&
                        g.StartDate < filterEndExclusive)
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
                        completed = g.Count(x => IsCompleted(x.Status)),
                        pending = g.Count(x => !IsCompleted(x.Status)),
                        overdue = g.Count(x =>
                            !IsCompleted(x.Status) &&
                            x.DueDate < now)
                    })
                    .OrderBy(x => x.year)
                    .ThenBy(x => x.month)
                    .ToList();

                // =========================================================
                // 12. OVERDUE TASK DETAILS
                // =========================================================

                var overdueTaskList = allTasks
                    .Where(t =>
                        !IsCompleted(t.Status) &&
                        t.Due_Date < now)
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

                        completedDate =
                            t.Completed_Date == default(DateTime)
                                ? (DateTime?)null
                                : t.Completed_Date,

                        totalMembers = t.Members,
                        wasEdited = t.wasEdited,
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

                // =========================================================
                // 13. OVERDUE GOAL DETAILS
                // =========================================================

                var overdueGoalList = goals
                    .Where(g =>
                        !IsCompleted(g.Status) &&
                        g.DueDate < now)
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

                // =========================================================
                // 14. GET APPROVED LEAVES
                // =========================================================

                var approvedLeaves = await _context.LeaveForm
                    .Where(l =>
                        l.SenderId == employeeId &&
                        l.Status != null &&
                        l.Status.Trim().ToLower() == "approved" &&
                        l.FromDate < filterEndExclusive &&
                        l.ToDate >= filterStart)
                    .ToListAsync();

                int approvedLeaveCount = approvedLeaves.Count;

                // =========================================================
                // 15. GET APPROVED PERMISSIONS
                // =========================================================

                var approvedPermissions = await _context.PermissionForm
                    .Where(p =>
                        p.SenderId == employeeId &&
                        p.Date >= filterStart &&
                        p.Date < filterEndExclusive &&
                        p.Status != null &&
                        p.Status.Trim().ToLower() == "approved")
                    .ToListAsync();

                int approvedPermissionCount = approvedPermissions.Count;

                // =========================================================
                // 18. GOAL DETAILS WITH EMPLOYEE TASKS
                // =========================================================

                var goalDetails = goals
                    .OrderBy(g =>
                        string.Equals(
                            g.GoalType,
                            "Yearly",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0 : 1)
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

                        completedDate =
                            g.Completed_Date == default(DateTime)
                                ? (DateTime?)null
                                : g.Completed_Date,

                        status = g.Status ?? "",
                        progress = g.Progress,
                        goalPoints = g.Goalpoints,

                        targetQuantity = g.TargetQuantity,
                        completedQuantity = g.CompletedQuantity ?? 0,

                        pendingQuantity = g.TargetQuantity.HasValue
                            ? Math.Max(
                                0,
                                g.TargetQuantity.Value -
                                (g.CompletedQuantity ?? 0))
                            : (int?)null,

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

                                completedDate =
                                    t.Completed_Date == default(DateTime)
                                        ? (DateTime?)null
                                        : t.Completed_Date,

                                members = t.Members,
                                performanceType = t.PerformanceType,
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

                // =========================================================
                // 19. FINAL RESPONSE
                // =========================================================

                return Ok(new
                {
                    employeeId,
                    employeeName = employee.Name,
                    department = employee.Department,
                    role = employee.Role,
                    year,
                    fromDate = filterStart,
                    toDate = filterEndExclusive.AddTicks(-1),

                    // TASK SUMMARY
                    totalTasks,
                    completedTasks,
                    pendingTasks,
                    overdueTasks,
                    taskCompletionPercent,
                    onTimeTasks,
                    taskOnTimePercent,
                    delayedTasks,
                    delayedTaskPercent,

                    // GOAL SUMMARY
                    totalGoals,
                    completedGoals,
                    pendingGoals,
                    overdueGoals,
                    goalCompletionPercent,
                    onTimeGoals,
                    goalOnTimePercent,
                    delayedGoalsCount,
                    delayedGoalPercent,

                    // PERFORMANCE & productivity

                    yearlyPerformance,
                    yearlyProductivity,
                    monthlyProductivityReport,


                    // TRENDS
                    monthlyTrend,

                    // OVERDUE DETAILS
                    overdueTaskList,
                    overdueGoalList,

                    // LEAVE AND PERMISSION
                    approvedLeaveCount,
                    approvedPermissionCount,

                    // GOAL AND TASK DETAILS
                    goals = goalDetails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching employee report.",
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
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
                        WarningPenaltyPoints = data?.WarningPenaltyPoints ?? 0,
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

                var allTasks = await _context.Tasks
                    .ToListAsync();

                var allTaskMembers = await _context.TaskMembers
                    .ToListAsync();

                var allTaskReviews = await _context.TaskReview
                    .ToListAsync();

                var employeePrefixes = userIds
                    .Select(id => id + "-")
                    .ToList();

                var selectedTaskMembers = allTaskMembers
                    .Where(tm =>
                        !string.IsNullOrWhiteSpace(tm.Assign_To) &&
                        employeePrefixes.Any(prefix =>
                            tm.Assign_To.StartsWith(prefix)))
                    .ToList();

                var employeeTaskCodes = selectedTaskMembers
                    .Where(tm => !string.IsNullOrWhiteSpace(tm.TaskCode))
                    .Select(tm => tm.TaskCode)
                    .Distinct()
                    .ToHashSet();

                var tasks = allTasks
                    .Where(t =>
                        !string.IsNullOrWhiteSpace(t.TaskCode) &&
                        employeeTaskCodes.Contains(t.TaskCode))
                    .Select(t =>
                    {

                        var review = allTaskReviews
                            .FirstOrDefault(tr =>
                                tr.TaskCode == t.TaskCode);

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

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => userIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                var allGoals = await _context.Goal
                    .Where(g => assignedGoalIds.Contains(g.Id))
                    .ToListAsync();

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

        targetQuantity = g.TargetQuantity,

        completedQuantity = g.CompletedQuantity ?? 0,

        pendingQuantity = GetPendingQuantity(g),

        isOverdue =
            !string.Equals(
                g.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase) &&
            g.DueDate < today,

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

                targetQuantity = t.Quantity,

                completedQuantity = t.CompletedQuantity ?? 0
            })
            .ToList()
    })
    .ToList();

                var leaves = await _context.LeaveForm
                    .Where(l =>
                        l.LeaveType != "Holiday" &&
                        userIds.Contains(l.SenderId))
                    .ToListAsync();

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

                var compensationData = await _context.ExtraWork
                    .Where(x => compensationIds.Contains(x.Id))
                    .ToListAsync();

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
                // 6. OVERTIME REPORT
                // ============================================================

                var overtimeList = await _context.OverTime
                    .Where(o => userIds.Contains(o.Uid))
                    .Select(o => new
                    {
                        overtimeId = o.Id,

                        employeeId = o.Uid,

                        department = o.Dept,

                        date = o.Date,

                        fromTime = o.FromTime,

                        toTime = o.ToTime,

                        totalHours = o.TotalHours,

                        reason = o.Reason ?? "",

                        staffStatus = o.StaffStatus ?? "",

                        managerStatus = o.ManagerStatus ?? "",

                        staffResponseReason = o.StaffResponseReason ?? "",

                        managerResponseReason = o.ManagerResponseReason ?? "",

                        requestedBy = o.RequestedBy,

                        approvedBy = o.Approved_by
                    })
                    .ToListAsync();


                // ============================================================
                // 7. EXTRA WORK REPORT
                // ============================================================

                var extraWorkList = await _context.ExtraWork
                    .Where(x => userIds.Contains(x.StaffId))
                    .Select(x => new
                    {
                        extraWorkId = x.Id,

                        employeeId = x.StaffId,

                        managerId = x.ManagerId,

                        taskId = x.TaskId,

                        workType = x.WorkType,

                        workDate = x.WorkDate,

                        expectedHours = x.ExpectedHours,

                        startTime = x.StartTime,

                        endTime = x.EndTime,

                        reason = x.Reason ?? "",

                        status = x.Status ?? "",

                        staffRemarks = x.StaffRemarks ?? "",

                        managerRemarks = x.ManagerRemarks ?? "",

                        verifiedBy = x.VerifiedBy,

                        isCompensationUsed = x.IsCompensationUsed
                    })
                    .ToListAsync();

                return Ok(new
                {
                    users = userList,

                    tasks,

                    goals,

                    leaveList,

                    permissionList,
                    overtimeList,

                    extraWorkList
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
