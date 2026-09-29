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



        [Authorize]
        [HttpGet("dashboard-summary")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            var today = DateTime.Today;

            // =========================================================
            // USERS
            // =========================================================

            var totalUsers = await _context.Users.CountAsync();

            // =========================================================
            // DEPARTMENTS
            // =========================================================

            var totalDepartments = await _context.Departments.CountAsync();

            // =========================================================
            // TASK SUMMARY
            // =========================================================

            var totalTasks = await _context.Tasks.CountAsync();

            var completedTasks = await _context.Tasks
                .CountAsync(t =>
                    t.Status != null &&
                    t.Status.ToLower() == "completed");

            var pendingTasks = await _context.Tasks
                .CountAsync(t =>
                    t.Status == null ||
                    t.Status.ToLower() != "completed");

            var overdueTasks = await _context.Tasks
                .CountAsync(t =>
                    t.Due_Date < today &&
                    (t.Status == null ||
                     t.Status.ToLower() != "completed"));

            // =========================================================
            // GOAL SUMMARY
            // =========================================================

            var totalGoals = await _context.Goal.CountAsync();

            var completedGoals = await _context.Goal
                .CountAsync(g =>
                    g.Status != null &&
                    g.Status.ToLower() == "completed");

            var pendingGoals = await _context.Goal
                .CountAsync(g =>
                    g.Status == null ||
                    g.Status.ToLower() != "completed");

            var overdueGoals = await _context.Goal
                .CountAsync(g =>
                    g.DueDate < today &&
                    (g.Status == null ||
                     g.Status.ToLower() != "completed"));

            // =========================================================
            // GOAL COMPLETION %
            // =========================================================

            double goalCompletionPercentage = totalGoals == 0
                ? 0
                : completedGoals * 100.0 / totalGoals;

            // =========================================================
            // COMPLETED GOALS
            // =========================================================

            var completedGoalsWithDate = await _context.Goal
                .Where(g =>
                    g.Status != null &&
                    g.Status.ToLower() == "completed" &&
                    g.Completed_Date.HasValue)
                .ToListAsync();

            // =========================================================
            // ON-TIME / DELAYED GOALS
            // =========================================================

            var onTimeGoals = completedGoalsWithDate
                .Count(g =>
                    g.Completed_Date!.Value.Date <= g.DueDate.Date);

            var delayedGoals = completedGoalsWithDate
                .Count(g =>
                    g.Completed_Date!.Value.Date > g.DueDate.Date);

            double goalOnTimePercentage =
                completedGoalsWithDate.Count == 0
                    ? 0
                    : onTimeGoals * 100.0 /
                      completedGoalsWithDate.Count;

            double delayedPercentage =
                completedGoalsWithDate.Count == 0
                    ? 0
                    : delayedGoals * 100.0 /
                      completedGoalsWithDate.Count;

            // =========================================================
            // OVERDUE TASK LIST
            // =========================================================

            var overdueTasksList = await _context.Tasks
                .Where(t =>
                    t.Due_Date < today &&
                    (t.Status == null ||
                     t.Status.ToLower() != "completed"))
                .OrderBy(t => t.Due_Date)
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
                .ToListAsync();

            // =========================================================
            // OVERDUE GOAL LIST
            // =========================================================

            var overdueGoalsList = await _context.Goal
                .Where(g =>
                    g.DueDate < today &&
                    (g.Status == null ||
                     g.Status.ToLower() != "completed"))
                .OrderBy(g => g.DueDate)
                .Select(g => new
                {
                    goalId = g.Id,
                    goalCode = g.GoalCode,
                    goalType = g.GoalType,
                    parentGoalId = g.ParentGoalId,

                    goal = g.Title,
                    status = g.Status ?? "",
                    priority = g.Priority ?? "",

                    createdAt = g.StartDate,
                    dueDate = g.DueDate,

                    createdBy = g.CreatedBy
                })
                .ToListAsync();

            // =========================================================
            // DEPARTMENT PERFORMANCE
            // =========================================================

            var departments = await _context.Departments
                .Select(d => d.DepartmentName)
                .ToListAsync();

            var departmentData = new List<object>();

            foreach (var departmentName in departments)
            {
                // -----------------------------------------------------
                // USERS IN DEPARTMENT
                // -----------------------------------------------------

                var departmentUserIds = await _context.Users
                    .Where(u => u.Department == departmentName)
                    .Select(u => u.UserId)
                    .ToListAsync();

                // -----------------------------------------------------
                // TASKS FOR DEPARTMENT
                // -----------------------------------------------------

                var departmentTaskCodes = await _context.TaskMembers
                    .Where(tm =>
                        !string.IsNullOrEmpty(tm.Assign_To))
                    .ToListAsync();

                var taskCodesForDepartment = new List<string>();

                foreach (var member in departmentTaskCodes)
                {
                    if (string.IsNullOrWhiteSpace(member.Assign_To))
                        continue;

                    var assignTo = member.Assign_To;

                    int dashIndex = assignTo.IndexOf("-");

                    string userIdText = dashIndex >= 0
                        ? assignTo.Substring(0, dashIndex)
                        : assignTo;

                    if (int.TryParse(userIdText, out int assignedUserId))
                    {
                        if (departmentUserIds.Contains(assignedUserId))
                        {
                            taskCodesForDepartment.Add(member.TaskCode);
                        }
                    }
                }

                taskCodesForDepartment = taskCodesForDepartment
                    .Distinct()
                    .ToList();

                var departmentTasks = await _context.Tasks
                    .Where(t => taskCodesForDepartment.Contains(t.TaskCode))
                    .ToListAsync();

                var totalTasksDept = departmentTasks.Count;

                var completedTasksDept = departmentTasks
                    .Count(t =>
                        t.Status != null &&
                        t.Status.ToLower() == "completed");

                var pendingTasksDept = departmentTasks
                    .Count(t =>
                        t.Status == null ||
                        t.Status.ToLower() != "completed");

                var overdueTasksDept = departmentTasks
                    .Count(t =>
                        t.Due_Date < today &&
                        (t.Status == null ||
                         t.Status.ToLower() != "completed"));

                // -----------------------------------------------------
                // GOALS FOR DEPARTMENT
                // -----------------------------------------------------
                //
                // New structure:
                //
                // Department Users
                //       ↓
                // GoalAssignments
                //       ↓
                // Goal
                //

                var departmentGoalIds = await _context.GoalAssignment
                    .Where(a => departmentUserIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                var departmentGoals = await _context.Goal
                    .Where(g => departmentGoalIds.Contains(g.Id))
                    .ToListAsync();

                // -----------------------------------------------------
                // INCLUDE YEARLY PARENT GOALS
                // -----------------------------------------------------

                var parentGoalIds = departmentGoals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g => parentGoalIds.Contains(g.Id))
                        .ToListAsync();

                    departmentGoals.AddRange(parentGoals);
                }

                departmentGoals = departmentGoals
                    .GroupBy(g => g.Id)
                    .Select(g => g.First())
                    .ToList();

                var totalGoalsDept = departmentGoals.Count;

                var completedGoalsDept = departmentGoals
                    .Count(g =>
                        g.Status != null &&
                        g.Status.ToLower() == "completed");

                var pendingGoalsDept = departmentGoals
                    .Count(g =>
                        g.Status == null ||
                        g.Status.ToLower() != "completed");

                var overdueGoalsDept = departmentGoals
                    .Count(g =>
                        g.DueDate < today &&
                        (g.Status == null ||
                         g.Status.ToLower() != "completed"));

                // -----------------------------------------------------
                // DEPARTMENT RESULT
                // -----------------------------------------------------

                departmentData.Add(new
                {
                    department = departmentName,

                    tasks = new
                    {
                        total = totalTasksDept,
                        completed = completedTasksDept,
                        pending = pendingTasksDept,
                        overdue = overdueTasksDept
                    },

                    goals = new
                    {
                        total = totalGoalsDept,
                        completed = completedGoalsDept,
                        pending = pendingGoalsDept,
                        overdue = overdueGoalsDept
                    },

                    completionPercentage = totalTasksDept == 0
                        ? 0
                        : Math.Round(
                            completedTasksDept * 100.0 /
                            totalTasksDept,
                            2)
                });
            }

            // =========================================================
            // TOP OVERDUE DEPARTMENT
            // =========================================================

            var topOverdueDepartment = departmentData
                .Select(d => new
                {
                    Department = (string)d.GetType()
                        .GetProperty("department")!
                        .GetValue(d)!,

                    TotalOverdue =
                        (int)d.GetType()
                            .GetProperty("tasks")!
                            .GetValue(d)!
                            .GetType()
                            .GetProperty("overdue")!
                            .GetValue(
                                d.GetType()
                                    .GetProperty("tasks")!
                                    .GetValue(d)!
                            )
                        +
                        (int)d.GetType()
                            .GetProperty("goals")!
                            .GetValue(d)!
                            .GetType()
                            .GetProperty("overdue")!
                            .GetValue(
                                d.GetType()
                                    .GetProperty("goals")!
                                    .GetValue(d)!
                            )
                })
                .OrderByDescending(x => x.TotalOverdue)
                .FirstOrDefault();

            // =========================================================
            // FINAL RESULT
            // =========================================================

            var result = new
            {
                totalManagers = totalUsers,

                totalDepartments,

                tasks = new
                {
                    total = totalTasks,
                    completed = completedTasks,
                    pending = pendingTasks,
                    overdue = overdueTasks
                },

                goals = new
                {
                    total = totalGoals,
                    completed = completedGoals,
                    pending = pendingGoals,
                    overdue = overdueGoals,

                    completionPercentage =
                        Math.Round(goalCompletionPercentage, 2),

                    onTimeCompletionPercentage =
                        Math.Round(goalOnTimePercentage, 2),

                    delayedPercentage =
                        Math.Round(delayedPercentage, 2)
                },

                overdueTasksList,
                overdueGoalsList,

                departmentData,

                topOverdueDepartment
            };

            return Ok(result);
        }

        [HttpGet("all-departments-productivity")]
        public async Task<IActionResult> GetAllDepartmentsProductivity(int year,int? month = null,int? quarter = null)
        {
            try
            {
                // =====================================================
                // VALIDATION
                // =====================================================
                if (year <= 0)
                    return BadRequest("Invalid year.");

                if (month.HasValue && (month.Value < 1 || month.Value > 12))
                    return BadRequest("Month must be between 1 and 12.");

                if (quarter.HasValue && (quarter.Value < 1 || quarter.Value > 4))
                    return BadRequest("Quarter must be between 1 and 4.");

                if (month.HasValue && quarter.HasValue)
                    return BadRequest("Use either month or quarter, not both.");

                // =====================================================
                // DATE RANGE
                // =====================================================
                int startMonth;
                int endMonth;

                if (month.HasValue)
                {
                    startMonth = month.Value;
                    endMonth = month.Value;
                }
                else if (quarter.HasValue)
                {
                    startMonth = ((quarter.Value - 1) * 3) + 1;
                    endMonth = startMonth + 2;
                }
                else
                {
                    // Full year
                    startMonth = 1;
                    endMonth = 12;
                }

                // =====================================================
                // GET DEPARTMENTS
                // =====================================================
                var departments = await _context.Departments
                    .Select(d => d.DepartmentName)
                    .ToListAsync();

                // =====================================================
                // GET USERS
                // =====================================================
                var users = await _context.Users
                    .Where(u => u.Department != null)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Department
                    })
                    .ToListAsync();

                // =====================================================
                // GET MONTHLY PRODUCTIVITY
                // Only required months/year
                // =====================================================
                var productivityData = await _context.MonthlyProductivity
                    .Where(x =>
                        x.Year == year &&
                        x.Month >= startMonth &&
                        x.Month <= endMonth)
                    .Select(x => new
                    {
                        x.StaffId,
                        x.Month,
                        x.Year,
                        x.TotalScore,
                        x.Productivity,
                        x.AttitudeScore,
                        x.TaskPoints,
                        x.GoalPoints
                    })
                    .ToListAsync();

                // =====================================================
                // USER -> DEPARTMENT LOOKUP
                // =====================================================
                var userDepartmentLookup = users
                    .GroupBy(x => x.UserId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().Department
                    );

                // =====================================================
                // GROUP PRODUCTIVITY BY DEPARTMENT + MONTH
                // =====================================================
                var departmentMonthlyData = productivityData
                    .Where(x => userDepartmentLookup.ContainsKey(x.StaffId))
                    .GroupBy(x => new
                    {
                        Department = userDepartmentLookup[x.StaffId],
                        x.Month
                    })
                    .Select(g => new
                    {
                        department = g.Key.Department,
                        month = g.Key.Month,

                        // Final score out of 100
                        productivity = Math.Round(
                            g.Average(x => x.TotalScore),
                            2
                        ),

                        // Optional supporting values
                        totalStaff = g.Select(x => x.StaffId)
                                      .Distinct()
                                      .Count(),

                        averageProductivity = Math.Round(
                            g.Average(x => x.Productivity),
                            2
                        ),

                        averageAttitude = Math.Round(
                            g.Average(x => x.AttitudeScore),
                            2
                        )
                    })
                    .ToList();

                // =====================================================
                // BUILD DEPARTMENT RESPONSE
                // =====================================================
                var result = new List<object>();

                foreach (var department in departments)
                {
                    var monthlyResult = new List<object>();

                    for (int m = startMonth; m <= endMonth; m++)
                    {
                        var data = departmentMonthlyData
                            .FirstOrDefault(x =>
                                x.department == department &&
                                x.month == m);

                        monthlyResult.Add(new
                        {
                            month = m,

                            // Final score /100
                            productivity = data?.productivity ?? 0,

                            // Number of staff having productivity record
                            totalStaff = data?.totalStaff ?? 0,

                            // Productivity /85
                            averageProductivity = data?.averageProductivity ?? 0,

                            // Attitude /15
                            averageAttitude = data?.averageAttitude ?? 0
                        });
                    }

                    result.Add(new
                    {
                        department,
                        monthlyData = monthlyResult
                    });
                }

                // =====================================================
                // RESPONSE
                // =====================================================
                return Ok(new
                {
                    year,
                    month,
                    quarter,
                    startMonth,
                    endMonth,
                    monthsReturned = endMonth - startMonth + 1,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching departments productivity",
                    error = ex.Message
                });
            }
        }

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


        [HttpGet("Manager-dashboard/{departmentName}")]
        public async Task<IActionResult> GetDepartmentSummary(string departmentName, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var today = DateTime.Today;

                // 1️⃣ Get Department Users
                var departmentUsers = await _context.Users
                    .Where(u => u.Department == departmentName)
                    .Select(u => new
                    {
                        Id = u.UserId.ToString(),
                        u.Name
                    })
                    .ToListAsync();

                if (!departmentUsers.Any())
                {
                    return Ok(new
                    {
                        Department = departmentName,
                        UserCount = 0,
                        Message = "No users found in this department"
                    });
                }

                var userIds = departmentUsers.Select(u => u.Id).ToList();

                // 2️⃣ Get Department Task Codes
                var departmentTaskCodes = await _context.TaskMembers
                    .Where(tm => userIds.Contains(
                        tm.Assign_To.Substring(0, tm.Assign_To.IndexOf("-"))
                    ))
                    .Select(tm => tm.TaskCode)
                    .Distinct()
                    .ToListAsync();

                // 3️⃣ Base Task Query
                var taskQuery = _context.Tasks
                    .Where(t => departmentTaskCodes.Contains(t.TaskCode));

                // Date Filter
                if (fromDate.HasValue && toDate.HasValue)
                {
                    var start = fromDate.Value.Date;
                    var end = toDate.Value.Date.AddDays(1).AddTicks(-1);

                    taskQuery = taskQuery
                        .Where(t => t.Created_At >= start && t.Created_At <= end);
                }

                var taskList = await taskQuery.ToListAsync();
                var totalTasks = taskList.Count;

                // 4️⃣ Normalize Status
                var normalizedTasks = taskList.Select(t => new
                {
                    Task = t,
                    Status = t.Status?.Trim().ToLower() ?? ""
                }).ToList();

                int completed = normalizedTasks.Count(t => t.Status == "completed");
                int notStarted = normalizedTasks.Count(t => t.Status == "not started");
                int inProgress = normalizedTasks.Count(t => t.Status == "inprogress");
                int pending = normalizedTasks.Count(t =>
                    t.Status == "pending" || t.Status == "paused");

                // 5️⃣ Overdue Tasks (Single Logic)
                var overdueTasks = normalizedTasks
                    .Where(t =>
                        t.Task.Due_Date < today &&
                        t.Status != "completed")
                    .ToList();

                int overdue = overdueTasks.Count;

                int lateCompleted = normalizedTasks.Count(t =>
                    t.Status == "completed" &&
                    t.Task.Completed_Date != null &&
                    t.Task.Completed_Date > t.Task.Due_Date);

                // 6️⃣ Completion %
                double completionPercentage =
                    totalTasks > 0
                        ? Math.Round((double)completed * 100 / totalTasks, 2)
                        : 0;

                // 7️⃣ SLA %
                int onTimeCompleted = normalizedTasks.Count(t =>
                    t.Status == "completed" &&
                    t.Task.Completed_Date != null &&
                    t.Task.Completed_Date <= t.Task.Due_Date);

                double slaPercentage =
                    completed > 0
                        ? Math.Round((double)onTimeCompleted * 100 / completed, 2)
                        : 0;

                // 8️⃣ Average Completion Days (Safe)
                var validCompletedTasks = normalizedTasks
                    .Where(t =>
                        t.Status == "completed" &&
                        t.Task.Completed_Date != null &&
                        t.Task.Completed_Date >= t.Task.Created_At)
                    .Select(t =>
                        (t.Task.Completed_Date - t.Task.Created_At).TotalDays)
                    .ToList();

                double avgCompletionDays =
                    validCompletedTasks.Any()
                        ? Math.Round(validCompletedTasks.Average(), 2)
                        : 0;

                // 9️⃣ Monthly Trend (Last 6 Months)
                var trendStart = DateTime.Today.AddMonths(-5);

                var monthlyTrend = normalizedTasks
                    .Where(t => t.Task.Created_At >= trendStart)
                    .GroupBy(t => new
                    {
                        t.Task.Created_At.Year,
                        t.Task.Created_At.Month
                    })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.Month,
                        Total = g.Count(),
                        Completed = g.Count(x => x.Status == "completed"),
                        Overdue = g.Count(x =>
                            x.Task.Due_Date < today &&
                            x.Status != "completed")
                    })
                    .OrderBy(x => x.Year)
                    .ThenBy(x => x.Month)
                    .ToList();

                // 🔟 Growth %
                double growth = 0;

                if (monthlyTrend.Count >= 2)
                {
                    var last = monthlyTrend.Last().Total;
                    var previous = monthlyTrend[monthlyTrend.Count - 2].Total;

                    if (previous > 0)
                        growth = Math.Round(((double)(last - previous) / previous) * 100, 2);
                }

                // 1️⃣1️⃣ Overdue Task List
                var overdueTaskList = overdueTasks
                    .OrderBy(t => t.Task.Due_Date)
                    .Select(t => new
                    {
                        t.Task.TaskCode,
                        t.Task.Task,
                        Description = t.Task.Description ?? "",
                        Priority = t.Task.Priority ?? "",
                        Status = t.Task.Status ?? "",
                        CreatedAt = t.Task.Created_At,
                        DueDate = t.Task.Due_Date,
                        TotalMembers = t.Task.Members,
                        //AssignedTo = _context.TaskMembers
                        //    .Where(tm => tm.TaskCode == t.Task.TaskCode)
                        //    .Select(tm => tm.Assign_To)
                        //    .ToList()

                        AssignedTo = _context.TaskMembers
    .Where(tm => tm.TaskCode == t.Task.TaskCode)
    .Select(tm => new
    {
        userId = tm.Assign_To,
        role = _context.Users
            .Where(u =>
                u.UserId.ToString() ==
                (tm.Assign_To.Contains("-")
                    ? tm.Assign_To.Substring(0, tm.Assign_To.IndexOf("-"))
                    : tm.Assign_To)
            )
            .Select(u => u.Role)
            .FirstOrDefault()
    })
    .ToList()
                    })
                    .ToList();

                // 1️⃣2️⃣ Performer Analysis (All Department Users Included)
                var performerStats = departmentUsers
                    .Select(u => new
                    {
                        User = u.Name,
                        TotalTasks = _context.TaskMembers
                            .Count(tm =>
                                departmentTaskCodes.Contains(tm.TaskCode) &&
                                tm.Assign_To.StartsWith(u.Id)),
                        Completed = _context.TaskMembers
                            .Join(_context.Tasks,
                                tm => tm.TaskCode,
                                t => t.TaskCode,
                                (tm, t) => new { tm, t })
                            .Count(x =>
                                x.t.Status.ToLower() == "completed" &&
                                x.tm.Assign_To.StartsWith(u.Id))
                    })
                    .ToList();

                var topPerformer = performerStats
                    .OrderByDescending(x => x.Completed)
                    .FirstOrDefault();

                var lowPerformer = performerStats
                    .OrderBy(x => x.Completed)
                    .FirstOrDefault();

                // 🔹 Final Response
                return Ok(new
                {
                    Department = departmentName,
                    UserCount = departmentUsers.Count,
                    TotalTasks = totalTasks,
                    Completed = completed,
                    NotStarted = notStarted,
                    InProgress = inProgress,
                    Pending = pending,
                    Overdue = overdue,
                    LateCompleted = lateCompleted,
                    CompletionPercentage = completionPercentage,
                    SLAPercentage = slaPercentage,
                    AverageCompletionDays = avgCompletionDays,
                    GrowthPercentage = growth,
                    MonthlyTrend = monthlyTrend,
                    OverdueTaskList = overdueTaskList,
                    TopPerformer = topPerformer,
                    LowPerformer = lowPerformer
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


        [HttpGet("head_dashboard-summary")]
        public async Task<IActionResult> GetHeadDashboardSummary()
        {
            try
            {
                var today = DateTime.Today;

                // ============================================================
                // DIRECTOR ALLOWED DEPARTMENTS
                // ============================================================

                var allowedDepartments = new[]
                {
            "Production Department",
            "IT Department",
            "Purchase Department",
            "Quality Department",
            "Store Department"
        };

                // ============================================================
                // USERS IN ALLOWED DEPARTMENTS
                // ============================================================

                var departmentUsers = await _context.Users
                    .Where(u =>
                        u.Department != null &&
                        allowedDepartments.Contains(u.Department))
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Department,
                        u.Role
                    })
                    .ToListAsync();

                var allowedUserIds = departmentUsers
                    .Select(u => u.UserId)
                    .ToHashSet();

                // ============================================================
                // DEPARTMENT COUNT
                // ============================================================

                var totalDepartments = await _context.Departments
                    .CountAsync(d =>
                        d.DepartmentName != null &&
                        allowedDepartments.Contains(d.DepartmentName));

                // ============================================================
                // USER / MANAGER COUNT
                // ============================================================
                // Keeping your existing meaning:
                // total users belonging to the allowed departments.
                //
                // If you want ONLY managers/head-of-department count,
                // filter by the actual Manager role ID here.
                // ============================================================

                var totalManagers = departmentUsers.Count;

                // ============================================================
                // LOAD TASKS + TASK MEMBERS
                // ============================================================

                var allTasks = await _context.Tasks
                    .ToListAsync();

                var allTaskMembers = await _context.TaskMembers
                    .ToListAsync();

                // ============================================================
                // FIND TASKS BELONGING TO ALLOWED DEPARTMENT USERS
                // ============================================================

                var departmentTaskCodes = allTaskMembers
                    .Where(tm =>
                    {
                        if (string.IsNullOrWhiteSpace(tm.Assign_To))
                            return false;

                        var value = tm.Assign_To.Trim();

                        var dashIndex = value.IndexOf("-");

                        var userIdText = dashIndex > 0
                            ? value.Substring(0, dashIndex)
                            : value;

                        return int.TryParse(userIdText, out var userId)
                               && allowedUserIds.Contains(userId);
                    })
                    .Select(tm => tm.TaskCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct()
                    .ToHashSet();

                var departmentTasks = allTasks
                    .Where(t => departmentTaskCodes.Contains(t.TaskCode))
                    .ToList();

                // ============================================================
                // TASK SUMMARY
                // ============================================================

                var totalTasks = departmentTasks.Count;

                var completedTasks = departmentTasks.Count(t =>
                    string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                var pendingTasks = departmentTasks.Count(t =>
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                var overdueTasks = departmentTasks.Count(t =>
                    t.Due_Date < today &&
                    !string.Equals(
                        t.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                // ============================================================
                // GOAL ASSIGNMENTS
                // ============================================================

                var allowedGoalAssignments = await _context.GoalAssignment
                    .Where(a => allowedUserIds.Contains(a.UserId))
                    .ToListAsync();

                var assignedGoalIds = allowedGoalAssignments
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToHashSet();

                // ============================================================
                // LOAD ASSIGNED GOALS
                // ============================================================

                var departmentGoals = await _context.Goal
                    .Where(g => assignedGoalIds.Contains(g.Id))
                    .ToListAsync();

                // ============================================================
                // INCLUDE YEARLY PARENT GOALS
                // ============================================================
                // If a Monthly Goal is assigned to a department user,
                // also include its Yearly parent.
                // ============================================================

                var parentGoalIds = departmentGoals
                    .Where(g =>
                        g.ParentGoalId.HasValue &&
                        g.ParentGoalId.Value > 0)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g =>
                            parentGoalIds.Contains(g.Id) &&
                            string.Equals(
                                g.GoalType,
                                "Yearly",
                                StringComparison.OrdinalIgnoreCase))
                        .ToListAsync();

                    var existingGoalIds = departmentGoals
                        .Select(g => g.Id)
                        .ToHashSet();

                    foreach (var parentGoal in parentGoals)
                    {
                        if (!existingGoalIds.Contains(parentGoal.Id))
                        {
                            departmentGoals.Add(parentGoal);
                        }
                    }
                }

                // ============================================================
                // GOAL SUMMARY
                // ============================================================

                var totalGoals = departmentGoals.Count;

                var completedGoals = departmentGoals.Count(g =>
                    string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                var pendingGoals = departmentGoals.Count(g =>
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                var overdueGoals = departmentGoals.Count(g =>
                    g.DueDate < today &&
                    !string.Equals(
                        g.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase));

                // ============================================================
                // GOAL COMPLETION %
                // ============================================================

                double goalCompletionPercentage = totalGoals == 0
                    ? 0
                    : (double)completedGoals * 100 / totalGoals;

                // ============================================================
                // COMPLETED GOALS WITH DATE
                // ============================================================

                var completedGoalsWithDate = departmentGoals
                    .Where(g =>
                        string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        g.Completed_Date.HasValue)
                    .ToList();

                // ============================================================
                // ON-TIME GOALS
                // ============================================================

                var onTimeGoals = completedGoalsWithDate
                    .Count(g =>
                        g.Completed_Date!.Value <= g.DueDate);

                double goalOnTimePercentage =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : (double)onTimeGoals * 100 /
                          completedGoalsWithDate.Count;

                // ============================================================
                // DELAYED GOALS
                // ============================================================

                var delayedGoals = completedGoalsWithDate
                    .Count(g =>
                        g.Completed_Date!.Value > g.DueDate);

                double delayedPercentage =
                    completedGoalsWithDate.Count == 0
                        ? 0
                        : (double)delayedGoals * 100 /
                          completedGoalsWithDate.Count;

                // ============================================================
                // OVERDUE TASK LIST
                // ============================================================

                var overdueTaskCodes = departmentTaskCodes;

                var overdueTasksList = allTasks
                    .Where(t =>
                        overdueTaskCodes.Contains(t.TaskCode) &&
                        t.Due_Date < today &&
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t.Due_Date)
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

                // ============================================================
                // OVERDUE GOAL LIST
                // ============================================================

                var overdueGoalsList = departmentGoals
                    .Where(g =>
                        g.DueDate < today &&
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(g => g.DueDate)
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
                        points = g.Goalpoints
                    })
                    .ToList();

                // ============================================================
                // DEPARTMENT PERFORMANCE
                // ============================================================

                var departmentData = new List<object>();

                foreach (var departmentName in allowedDepartments)
                {
                    // --------------------------------------------------------
                    // USERS OF THIS DEPARTMENT
                    // --------------------------------------------------------

                    var departmentUserIds = departmentUsers
                        .Where(u =>
                            string.Equals(
                                u.Department,
                                departmentName,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(u => u.UserId)
                        .ToHashSet();

                    // --------------------------------------------------------
                    // TASKS FOR DEPARTMENT
                    // --------------------------------------------------------

                    var departmentTaskCodesForDept = allTaskMembers
                        .Where(tm =>
                        {
                            if (string.IsNullOrWhiteSpace(tm.Assign_To))
                                return false;

                            var value = tm.Assign_To.Trim();

                            var dashIndex = value.IndexOf("-");

                            var userIdText = dashIndex > 0
                                ? value.Substring(0, dashIndex)
                                : value;

                            return int.TryParse(userIdText, out var userId)
                                   && departmentUserIds.Contains(userId);
                        })
                        .Select(tm => tm.TaskCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct()
                        .ToHashSet();

                    var departmentTasksForDept = allTasks
                        .Where(t =>
                            departmentTaskCodesForDept.Contains(t.TaskCode))
                        .ToList();

                    var totalTasksDept = departmentTasksForDept.Count;

                    var completedTasksDept = departmentTasksForDept.Count(t =>
                        string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    var pendingTasksDept = departmentTasksForDept.Count(t =>
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    var overdueTasksDept = departmentTasksForDept.Count(t =>
                        t.Due_Date < today &&
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    // --------------------------------------------------------
                    // GOALS FOR DEPARTMENT
                    // --------------------------------------------------------

                    var departmentGoalAssignments =
                        allowedGoalAssignments
                            .Where(a =>
                                departmentUserIds.Contains(a.UserId))
                            .ToList();

                    var departmentGoalIds = departmentGoalAssignments
                        .Select(a => a.GoalId)
                        .Distinct()
                        .ToHashSet();

                    var goalsForDepartment = await _context.Goal
                        .Where(g => departmentGoalIds.Contains(g.Id))
                        .ToListAsync();

                    // --------------------------------------------------------
                    // INCLUDE YEARLY PARENT GOALS
                    // --------------------------------------------------------

                    var departmentParentGoalIds = goalsForDepartment
                        .Where(g => g.ParentGoalId.HasValue)
                        .Select(g => g.ParentGoalId!.Value)
                        .Distinct()
                        .ToList();

                    if (departmentParentGoalIds.Any())
                    {
                        var parentGoalsForDepartment =
                            await _context.Goal
                                .Where(g =>
                                    departmentParentGoalIds.Contains(g.Id) &&
                                    string.Equals(
                                        g.GoalType,
                                        "Yearly",
                                        StringComparison.OrdinalIgnoreCase))
                                .ToListAsync();

                        var existingIds = goalsForDepartment
                            .Select(g => g.Id)
                            .ToHashSet();

                        foreach (var parentGoal in parentGoalsForDepartment)
                        {
                            if (!existingIds.Contains(parentGoal.Id))
                            {
                                goalsForDepartment.Add(parentGoal);
                            }
                        }
                    }

                    // --------------------------------------------------------
                    // GOAL COUNTS
                    // --------------------------------------------------------

                    var totalGoalsDept = goalsForDepartment.Count;

                    var completedGoalsDept = goalsForDepartment.Count(g =>
                        string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    var pendingGoalsDept = goalsForDepartment.Count(g =>
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    var overdueGoalsDept = goalsForDepartment.Count(g =>
                        g.DueDate < today &&
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    // --------------------------------------------------------
                    // TASK COMPLETION %
                    // --------------------------------------------------------

                    var completionPercentage =
                        totalTasksDept > 0
                            ? completedTasksDept * 100.0 / totalTasksDept
                            : 0;

                    // --------------------------------------------------------
                    // ADD DEPARTMENT RESULT
                    // --------------------------------------------------------

                    departmentData.Add(new
                    {
                        Department = departmentName,

                        users = departmentUserIds.Count,

                        tasks = new
                        {
                            total = totalTasksDept,
                            completed = completedTasksDept,
                            pending = pendingTasksDept,
                            overdue = overdueTasksDept
                        },

                        goals = new
                        {
                            total = totalGoalsDept,
                            completed = completedGoalsDept,
                            pending = pendingGoalsDept,
                            overdue = overdueGoalsDept
                        },

                        completionPercentage =
                            Math.Round(completionPercentage, 2)
                    });
                }

                // ============================================================
                // TOP OVERDUE DEPARTMENT
                // ============================================================

                var topOverdueDepartmentData =
                    new List<(string Department, int OverdueTasks, int OverdueGoals)>();

                foreach (var departmentName in allowedDepartments)
                {
                    // --------------------------------------------------------
                    // USERS
                    // --------------------------------------------------------

                    var departmentUserIds = departmentUsers
                        .Where(u =>
                            string.Equals(
                                u.Department,
                                departmentName,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(u => u.UserId)
                        .ToHashSet();

                    // --------------------------------------------------------
                    // OVERDUE TASKS
                    // --------------------------------------------------------

                    var departmentTaskCodesForDept = allTaskMembers
                        .Where(tm =>
                        {
                            if (string.IsNullOrWhiteSpace(tm.Assign_To))
                                return false;

                            var value = tm.Assign_To.Trim();

                            var dashIndex = value.IndexOf("-");

                            var userIdText = dashIndex > 0
                                ? value.Substring(0, dashIndex)
                                : value;

                            return int.TryParse(userIdText, out var userId)
                                   && departmentUserIds.Contains(userId);
                        })
                        .Select(tm => tm.TaskCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct()
                        .ToHashSet();

                    var overdueTaskCount = allTasks.Count(t =>
                        departmentTaskCodesForDept.Contains(t.TaskCode) &&
                        t.Due_Date < today &&
                        !string.Equals(
                            t.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    // --------------------------------------------------------
                    // OVERDUE GOALS
                    // --------------------------------------------------------

                    var departmentGoalIds = allowedGoalAssignments
                        .Where(a => departmentUserIds.Contains(a.UserId))
                        .Select(a => a.GoalId)
                        .Distinct()
                        .ToHashSet();

                    var departmentGoalsForOverdue =
                        await _context.Goal
                            .Where(g => departmentGoalIds.Contains(g.Id))
                            .ToListAsync();

                    // Include yearly parents
                    var parentIds = departmentGoalsForOverdue
                        .Where(g => g.ParentGoalId.HasValue)
                        .Select(g => g.ParentGoalId!.Value)
                        .Distinct()
                        .ToList();

                    if (parentIds.Any())
                    {
                        var parents = await _context.Goal
                            .Where(g =>
                                parentIds.Contains(g.Id) &&
                                string.Equals(
                                    g.GoalType,
                                    "Yearly",
                                    StringComparison.OrdinalIgnoreCase))
                            .ToListAsync();

                        var existingIds = departmentGoalsForOverdue
                            .Select(g => g.Id)
                            .ToHashSet();

                        foreach (var parent in parents)
                        {
                            if (!existingIds.Contains(parent.Id))
                            {
                                departmentGoalsForOverdue.Add(parent);
                            }
                        }
                    }

                    var overdueGoalCount = departmentGoalsForOverdue.Count(g =>
                        g.DueDate < today &&
                        !string.Equals(
                            g.Status,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase));

                    topOverdueDepartmentData.Add(
                        (
                            departmentName,
                            overdueTaskCount,
                            overdueGoalCount
                        ));
                }

                // ============================================================
                // GET TOP OVERDUE DEPARTMENT
                // ============================================================

                var topOverdue = topOverdueDepartmentData
                    .OrderByDescending(x =>
                        x.OverdueTasks + x.OverdueGoals)
                    .FirstOrDefault();

                object? topOverdueDepartment = null;

                if (!string.IsNullOrWhiteSpace(topOverdue.Department))
                {
                    topOverdueDepartment = new
                    {
                        Department = topOverdue.Department,
                        overdueTasks = topOverdue.OverdueTasks,
                        overdueGoals = topOverdue.OverdueGoals,
                        totalOverdue =
                            topOverdue.OverdueTasks +
                            topOverdue.OverdueGoals
                    };
                }

                // ============================================================
                // FINAL RESULT
                // ============================================================

                var result = new
                {
                    totalManagers,

                    totalDepartments,

                    departments = allowedDepartments,

                    tasks = new
                    {
                        total = totalTasks,
                        completed = completedTasks,
                        pending = pendingTasks,
                        overdue = overdueTasks
                    },

                    goals = new
                    {
                        total = totalGoals,
                        completed = completedGoals,
                        pending = pendingGoals,
                        overdue = overdueGoals,

                        completionPercentage =
                            Math.Round(
                                goalCompletionPercentage,
                                2),

                        onTimeCompletionPercentage =
                            Math.Round(
                                goalOnTimePercentage,
                                2),

                        delayedPercentage =
                            Math.Round(
                                delayedPercentage,
                                2)
                    },

                    overdueTasksList,

                    overdueGoalsList,

                    departmentData,

                    topOverdueDepartment
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching head dashboard summary.",
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }
    }
}
