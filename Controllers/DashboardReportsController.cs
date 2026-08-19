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


        [HttpGet("dashboard-summary")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            var today = DateTime.Today;

            // ================= USERS =================
            var totalManagers = await _context.Users
                .CountAsync();

            // ================= DEPARTMENTS =================
            var totalDepartments = await _context.Departments.CountAsync();

            // ================= TASK SUMMARY =================
            var totalTasks = await _context.Tasks.CountAsync();

            var completedTasks = await _context.Tasks
                .CountAsync(t => t.Status.ToLower() == "completed");

            var pendingTasks = await _context.Tasks
                .CountAsync(t => t.Status.ToLower() != "completed");

            var overdueTasks = await _context.Tasks
                .CountAsync(t => t.Due_Date < today && t.Status.ToLower() != "completed");


            // ================= GOAL SUMMARY =================
            var totalGoals = await _context.Goal.CountAsync();

            var completedGoals = await _context.Goal
                .CountAsync(g => g.Status.ToLower() == "completed");

            var pendingGoals = await _context.Goal
                .CountAsync(g => g.Status.ToLower() != "completed");

            var overdueGoals = await _context.Goal
                .CountAsync(g => g.DueDate < today && g.Status.ToLower() != "completed");


            // ================= GOAL COMPLETION % =================
            double goalCompletionPercentage = totalGoals == 0
                ? 0
                : (double)completedGoals * 100 / totalGoals;


            // ================= GOAL ON-TIME COMPLETION % =================
            var completedGoalsWithDate = await _context.Goal
                .Where(g => g.Status.ToLower() == "completed"
                         && g.Completed_Date != null
                         && g.DueDate != null)
                .ToListAsync();

            var onTimeGoals = completedGoalsWithDate
                .Count(g => g.Completed_Date <= g.DueDate.Date);

            double goalOnTimePercentage = completedGoalsWithDate.Count == 0
                ? 0
                : (double)onTimeGoals * 100 / completedGoalsWithDate.Count;

            //--------------------------Delayed %-----------------------------

            var delayedGoals = completedGoalsWithDate
    .Count(g => g.Completed_Date > g.DueDate);
            double delayedPercentage = completedGoalsWithDate.Count == 0
    ? 0
    : (double)delayedGoals * 100 / completedGoalsWithDate.Count;

            // ================= OVERDUE TASK LIST =================
            var overdueTaskslist = await _context.Tasks
                .Where(t => t.Due_Date < today && t.Status != "completed")
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

            var overdueGoalsList = await _context.Goal
    .Where(g => g.DueDate < today && g.Status.ToLower() != "completed")
    .OrderBy(g => g.DueDate)
    .Select(g => new
    {
        goalId = g.Id,
        goal = g.Title,
        status = g.Status ?? "",
        createdAt = g.StartDate,
        dueDate = g.DueDate,
        priority =g.Priority??""

       
    })
    .ToListAsync();

            // ================= Department PERFORMANCE =================


            var departmentSummary = await _context.Departments
    .Select(d => new
    {
        Department = d.DepartmentName,

        Users = _context.Users
        .Where(u => u.Department == d.DepartmentName)
        .Select(u => u.UserId.ToString())
        .ToList()
    })
    .ToListAsync();

            var departmentData = departmentSummary.Select(d =>
            {
                // ================= TASKS =================
                var tasks = (
                    from tm in _context.TaskMembers
                    join t in _context.Tasks on tm.TaskCode equals t.TaskCode
                    where d.Users.Contains(
                        tm.Assign_To.Contains("-")
                            ? tm.Assign_To.Substring(0, tm.Assign_To.IndexOf("-"))
                            : tm.Assign_To
                    )
                    select t
                ).Distinct();

                var totalTasksDept = tasks.Count();

                var completedTasksDept = tasks.Count(t => t.Status.ToLower() == "completed");

                var pendingTasksDept = tasks.Count(t => t.Status.ToLower() != "completed");

                var overdueTasksDept = tasks.Count(t =>
                    t.Due_Date < DateTime.Today && t.Status.ToLower() != "completed");

                // ================= GOALS =================
                var goals = _context.Goal
                    .Where(g => g.Assign_To != null &&
                                d.Users.Contains(g.Assign_To.ToString()));

                var totalGoalsDept = goals.Count();

                var completedGoalsDept = goals.Count(g => g.Status.ToLower() == "completed");

                var pendingGoalsDept = goals.Count(g => g.Status.ToLower() != "completed");

                var overdueGoalsDept = goals.Count(g =>
                    g.DueDate < DateTime.Today && g.Status.ToLower() != "completed");

              
                return new
                {
                    Department = d.Department,

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

                    completionPercentage = totalTasksDept > 0
                        ? completedTasksDept * 100.0 / totalTasksDept
                        : 0
                };
            }).ToList();

            var topOverdueDepartment = await _context.Departments
    .Select(d => new
    {
        Department = d.DepartmentName,

        // Task overdue count
        OverdueTasks = (
            from tm in _context.TaskMembers
            join t in _context.Tasks on tm.TaskCode equals t.TaskCode
            join u in _context.Users on tm.Assign_To equals u.UserId.ToString()
            where u.Department == d.DepartmentName
                  && t.Due_Date < today
                  && t.Status.ToLower() != "completed"
            select t.TaskCode
        ).Distinct().Count(),

        // Goal overdue count
        OverdueGoals = _context.Goal
            .Where(g =>
                g.Assign_To != null &&
                _context.Users.Any(u =>
                    u.UserId.ToString() == g.Assign_To &&
                    u.Department == d.DepartmentName)
                && g.DueDate < today
                && g.Status.ToLower() != "completed"
            ).Count()
    })
    .Select(x => new
    {
        x.Department,
        TotalOverdue = x.OverdueTasks + x.OverdueGoals
    })
    .OrderByDescending(x => x.TotalOverdue)
    .FirstOrDefaultAsync();
            // ================= RESULT =================
            var result = new
            {
                totalManagers,
               
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
                    completionPercentage = Math.Round(goalCompletionPercentage, 2),
                    onTimeCompletionPercentage = Math.Round(goalOnTimePercentage, 2),
                    delayedPercentage = Math.Round(delayedPercentage, 2)
                },

                overdueTaskslist,
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

    }
}
