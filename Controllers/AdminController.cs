using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff.Services;
using staff_work_tracking.Data;
using StaffWork_Track.Services;
using System.Security.Claims;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace staff.Controllers
{
    [Route("api/Manager")]
    [ApiController]
    public class AdminController : ControllerBase
    {


        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private NotificationService _notific;
        private readonly FirebaseNotificationService _firebaseNotificationService;
        private readonly ProductivityService _service;

        public AdminController(AppDbContext context, IConfiguration config, NotificationService notificationService, FirebaseNotificationService firebaseNotificationService, ProductivityService service)
        {
            _context = context;
            _config = config;
            _notific = notificationService;
            _firebaseNotificationService = firebaseNotificationService;
            _service = service;
        }


        [HttpGet("staffbydept/{department}")]
        public async Task<IActionResult> GetEmployeesByDepartment(string department)
        {
            var employees = await (
                from u in _context.Users
                join role in _context.Roles
                    on u.Role equals role.Id.ToString()
                where u.Department == department
                select new
                {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Department,
                    Role = role.RoleName,
                    u.Status,
                    u.Created_by
                }
            ).ToListAsync();

            var roleIds = employees
                .Select(x => x.Created_by?.Split('-').LastOrDefault())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            var roles = await _context.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.RoleName);

            var result = employees.Select(x =>
            {
                var creatorRoleId = x.Created_by?
                    .Split('-')
                    .LastOrDefault();

                int.TryParse(creatorRoleId, out var roleId);

                return new
                {
                    x.UserId,
                    x.Name,
                    x.Email,
                    x.Department,
                    x.Role,
                    x.Status,

                    Created_by = roles.TryGetValue(roleId, out var roleName)
                        ? roleName
                        : ""
                };
            }).ToList();

            return Ok(new
            {
                department,
                totalCount = result.Count,
                employees = result
            });
        }

        [HttpGet("allStaffGoals/{department}")]
        public async Task<IActionResult> GetGoalsByDepartment(string department)
        {
            // 🔹 STEP 1: Get all staff users in this department
            var staffUsers = await _context.Users
                .Where(u => u.Department == department)
                .ToListAsync();

            var staffIds = staffUsers
                .Select(u => u.UserId.ToString())
                .ToList();

            // 🔹 STEP 2: Get ONLY goals assigned to those staff
            var goals = await _context.Goal
                .Where(g =>
                    g.Department == department &&
                    staffIds.Contains(g.Assign_To)
                )
                .OrderByDescending(g => g.Id)
                .ToListAsync();

            var goalCodes = goals.Select(g => g.GoalCode).ToList();

            // 🔹 STEP 3: Get tasks under those goals
            var tasks = await _context.Tasks
                .Where(t => goalCodes.Contains(t.GoalCode))
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            var taskMembers = await _context.TaskMembers.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var result = goals.Select(g =>
            {
                var tasksForGoal = tasks
                    .Where(t => t.GoalCode == g.GoalCode)
                    .Select(t =>
                    {
                        // 🔸 Assigned TO (only staff in this department)
                        var assignedToUsers = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_To) &&
                                tm.Assign_To.Contains("-")
                            )
                            .Select(tm =>
                            {
                                var uId = tm.Assign_To.Split('-')[0];
                                var u = users.FirstOrDefault(x => x.UserId.ToString() == uId);

                                if (u == null || !staffIds.Contains(u.UserId.ToString()))
                                    return null;

                                return new
                                {
                                    userId = u.UserId,
                                    name = u.Name,
                                    department = u.Department,
                                    role = u.Role
                                };
                            })
                            .Where(x => x != null)
                            .ToList();

                        // ❌ Skip task if no valid staff users
                        if (!assignedToUsers.Any())
                            return null;

                        // 🔸 Assigned BY
                        var assigner = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_By) &&
                                tm.Assign_By.Contains("-")
                            )
                            .Select(tm =>
                            {
                                var uId = tm.Assign_By.Split('-')[0];
                                var u = users.FirstOrDefault(x => x.UserId.ToString() == uId);

                                return u == null ? null : new
                                {
                                    Name = u.Name,
                                    Role = u.Role,
                                    Department = u.Department
                                };
                            })
                            .FirstOrDefault();

                        return new
                        {
                            taskCode = t.TaskCode,
                            task = t.Task,
                            description = t.Description,
                            priority = t.Priority,
                            status = t.Status,
                            createdAt = t.Created_At,
                            dueDate = t.Due_Date,
                            totalMembers = t.Members,

                            assignedBy = assigner?.Name ?? "N/A",
                            assignerRole = assigner?.Role ?? "N/A",
                            assignerDepartment = assigner?.Department ?? "N/A",

                            assignedTo = assignedToUsers
                        };
                    })
                    .Where(t => t != null)
                    .ToList();

                return new
                {
                    g.GoalCode,
                    g.Title,
                    g.Priority,
                    g.Status,
                    g.Progress,
                    g.StartDate,
                    g.DueDate,
                    g.Department,
                    g.Goalpoints,

                    assignBy = users
                        .Where(u => u.UserId.ToString() == g.Assign_By)
                        .Select(u => u.Name)
                        .FirstOrDefault(),

                    assignTo = users
                        .Where(u => u.UserId.ToString() == g.Assign_To)
                        .Select(u => u.Name)
                        .FirstOrDefault(),

                    taskCount = tasksForGoal.Count,
                    tasks = tasksForGoal
                };
            }).ToList();

            return Ok(new
            {
                department,
                totalGoals = result.Count,
                goals = result
            });
        }


        [HttpGet("userstaskslist/{adminId}")]
        public async Task<IActionResult> GetAdminTasks(int adminId)
        {
            var tasks = await _context.Tasks
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            var taskMembers = await _context.TaskMembers.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var result = tasks
                .Where(t =>
                    taskMembers.Any(tm =>
                        tm.TaskCode == t.TaskCode &&
                        !string.IsNullOrEmpty(tm.Assign_To) &&
                        tm.Assign_To.StartsWith(adminId + "-")
                    )
                )
                .Select(t =>
                {
                    var assigner = taskMembers
                        .Where(tm =>
                            tm.TaskCode == t.TaskCode &&
                            !string.IsNullOrEmpty(tm.Assign_By))
                        .Select(tm =>
                        {
                            var id = int.Parse(tm.Assign_By.Split('-')[0]);
                            var user = users.FirstOrDefault(u => u.UserId == id);
                            return user == null ? null : new
                            {
                                user.UserId,
                                user.Name,
                                user.Role,
                                user.Department
                            };
                        })
                        .FirstOrDefault();

                    return new
                    {
                        taskCode = t.TaskCode,
                        task = t.Task,
                        description = t.Description,
                        priority = t.Priority,
                        status = t.Status,
                        createdAt = t.Created_At,
                        dueDate = t.Due_Date,
                        totalMembers = t.Members,

                        assignedBy = assigner?.Name ?? "N/A",
                        assignerRole = assigner?.Role ?? "N/A",
                        assignerDepartment = assigner?.Department ?? "N/A",

                        assignedTo = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_To))
                            .Select(tm =>
                            {
                                var id = int.Parse(tm.Assign_To.Split('-')[0]);
                                var user = users.FirstOrDefault(u => u.UserId == id);
                                return user == null ? null : new
                                {
                                    user.UserId,
                                    user.Name,
                                    user.Role,
                                    user.Department
                                };
                            })
                            .Where(x => x != null)
                            .ToList()
                    };
                })
                .ToList();

            return Ok(new
            {
                adminId,
                totalTasks = result.Count,
                result
            });
        }


        [HttpGet("usersgoallist/{adminId}")]
        public async Task<IActionResult> GetManagerTasks(int adminId)
        {
            // 1️⃣ Get goals assigned to this admin
            var goals = await _context.Goal
                .Where(g => g.Assign_To == adminId.ToString())
                .OrderByDescending(g => g.Id)
                .ToListAsync();

            var goalCodes = goals.Select(g => g.GoalCode).ToList();

            // 2️⃣ Get tasks under those goals
            var tasks = await _context.Tasks
                .Where(t => goalCodes.Contains(t.GoalCode))
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            var taskMembers = await _context.TaskMembers.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var result = goals.Select(g =>
            {
                var tasksForGoal = tasks
                    .Where(t => t.GoalCode == g.GoalCode)
                    .Where(t =>
                        taskMembers.Any(tm =>
                            tm.TaskCode == t.TaskCode &&
                            !string.IsNullOrEmpty(tm.Assign_To) &&
                            tm.Assign_To.StartsWith(adminId + "-")
                        )
                    )
                    .Select(t =>
                    {
                        var assigner = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_By))
                            .Select(tm =>
                            {
                                var id = int.Parse(tm.Assign_By.Split('-')[0]);
                                var user = users.FirstOrDefault(u => u.UserId == id);

                                return user == null ? null : new
                                {
                                    user.UserId,
                                    user.Name,
                                    user.Role,
                                    user.Department
                                };
                            })
                            .FirstOrDefault();

                        var assignedUsers = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_To))
                            .Select(tm =>
                            {
                                var id = int.Parse(tm.Assign_To.Split('-')[0]);
                                var user = users.FirstOrDefault(u => u.UserId == id);

                                return user == null ? null : new
                                {
                                    user.UserId,
                                    user.Name,
                                    user.Role,
                                    user.Department
                                };
                            })
                            .Where(x => x != null)
                            .ToList();

                        return new
                        {
                            taskCode = t.TaskCode,
                            task = t.Task,
                            description = t.Description,
                            priority = t.Priority,
                            status = t.Status,
                            createdAt = t.Created_At,
                            dueDate = t.Due_Date,
                            totalMembers = t.Members,

                            assignedBy = assigner?.Name ?? "N/A",
                            assignerRole = assigner?.Role ?? "N/A",
                            assignerDepartment = assigner?.Department ?? "N/A",

                            assignedTo = assignedUsers
                        };
                    })
                    .ToList();

                return new
                {
                    g.GoalCode,
                    g.Title,
                    g.Priority,
                    g.Status,
                    g.Progress,
                    g.Goalpoints,
                    g.StartDate,
                    g.DueDate,
                    g.Department,
                    assignBy = users
                        .Where(u => u.UserId.ToString() == g.Assign_By)
                        .Select(u => $"{u.UserId}-{u.Name}")
                        .FirstOrDefault(),

                    assignTo = users
                        .Where(u => u.UserId.ToString() == g.Assign_To)
                        .Select(u => $"{u.UserId}-{u.Name}")
                        .FirstOrDefault(),

                    taskCount = tasksForGoal.Count,
                    tasks = tasksForGoal
                };
            });

            return Ok(result);
        }


        [HttpGet("Managergoalsassigned/{adminId}")]
        public async Task<IActionResult> GetTasksAssignedByAdmin(int adminId)
        {
            // 1️⃣ Get goals assigned BY this admin
            var goals = await _context.Goal
                .Where(g => g.Assign_By == adminId.ToString())
                .OrderByDescending(g => g.Id)
                .ToListAsync();

            var goalCodes = goals.Select(g => g.GoalCode).ToList();

            // 2️⃣ Get tasks under those goals
            var tasks = await _context.Tasks
                .Where(t => goalCodes.Contains(t.GoalCode))
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            var taskMembers = await _context.TaskMembers.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var result = goals.Select(g =>
            {
                var tasksForGoal = tasks
                    .Where(t => t.GoalCode == g.GoalCode)
                    .Where(t =>
                        taskMembers.Any(tm =>
                            tm.TaskCode == t.TaskCode &&
                            tm.Assign_By != null &&
                            tm.Assign_By.StartsWith(adminId + "-")
                        )
                    )
                    .Select(t =>
                    {
                        var assigner = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_By))
                            .Select(tm =>
                            {
                                var id = int.Parse(tm.Assign_By!.Split('-')[0]);
                                return users.FirstOrDefault(u => u.UserId == id);
                            })
                            .FirstOrDefault();

                        var assignedTo = taskMembers
                            .Where(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_To))
                            .Select(tm =>
                            {
                                var id = int.Parse(tm.Assign_To!.Split('-')[0]);
                                return users.FirstOrDefault(u => u.UserId == id);
                            })
                            .Where(u => u != null)
                            .Select(u => new
                            {
                                u!.UserId,
                                u.Name,
                                u.Role,
                                u.Department
                            })
                            .ToList();

                        return new
                        {
                            taskCode = t.TaskCode,
                            task = t.Task,
                            description = t.Description,
                            priority = t.Priority,
                            status = t.Status,
                            createdAt = t.Created_At,
                            dueDate = t.Due_Date,
                            totalMembers = t.Members,

                            assignedBy = assigner?.Name ?? "N/A",
                            assignerRole = assigner?.Role ?? "N/A",
                            assignerDepartment = assigner?.Department ?? "N/A",

                            assignedTo
                        };
                    })
                    .ToList();

                return new
                {
                    g.GoalCode,
                    g.Title,
                    g.Priority,
                    g.Status,
                    g.Progress,
                    g.Goalpoints,
                    g.StartDate,
                    g.DueDate,
                    g.Department,
                    assignBy = users
                        .Where(u => u.UserId.ToString() == g.Assign_By)
                        .Select(u => $"{u.UserId}-{u.Name}")
                        .FirstOrDefault(),

                    assignTo = users
                        .Where(u => u.UserId.ToString() == g.Assign_To)
                        .Select(u => $"{u.UserId}-{u.Name}")
                        .FirstOrDefault(),

                    taskCount = tasksForGoal.Count,
                    tasks = tasksForGoal
                };
            });

            return Ok(result);
        }

        [Authorize]
        [HttpPut("update-task-status")]
        public async Task<IActionResult> UpdateTaskStatus([FromBody] UpdateTaskStatusDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim.Value);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return NotFound("User not found");

            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.TaskCode == dto.TaskCode);
            if (task == null)
                return NotFound("Task not found");

            var taskMember = await _context.TaskMembers.FirstOrDefaultAsync(tm =>
                tm.TaskCode == dto.TaskCode &&
                !string.IsNullOrEmpty(tm.Assign_To) &&
                tm.Assign_To.StartsWith(userId + "-")
            );

            if (taskMember == null)
                return BadRequest("User is not assigned to this task");

         
            taskMember.UserStatus = dto.Status;
            task.Status = dto.Status;
            if (dto.Status == "completed")
                task.Completed_Date = DateTime.Now;

            await _context.SaveChangesAsync();

          

            if (!string.IsNullOrEmpty(task.GoalCode))
            {
                var goal = await _context.Goal.FirstOrDefaultAsync(g => g.GoalCode == task.GoalCode);

                if (goal != null)
                {
                    var goalTasks = await _context.Tasks
                        .Where(t => t.GoalCode == goal.GoalCode)
                        .ToListAsync();


                    int total = goalTasks.Count;
                    int completed = goalTasks.Count(t => !string.IsNullOrEmpty(t.Status) && t.Status.Trim().ToLower() == "completed");
                    var notStarted = goalTasks.Count(t =>
     !string.IsNullOrEmpty(t.Status) &&
     t.Status.ToLower() == "not started"
 );

                    // Update status
                    if (completed == total)
                    {
                        goal.Status = "completed";
                        goal.Completed_Date = DateTime.Now;

                      
                    }
                    else if (notStarted == total)
                        goal.Status = "not started";
                    else
                        goal.Status = "inprogress";
                    goal.Progress = (int)(((double)completed / total) * 100);
                    await _context.SaveChangesAsync();
                }
            }

         
            return Ok(new
            {
                message = "Task status updated successfully",
                taskCode = dto.TaskCode,
                userId,
                status = dto.Status,
                completedDate = task.Completed_Date
            });
        }

        [Authorize]
        [HttpPost("review-task")]
        public async Task<IActionResult> SubmitReview([FromBody] ReviewTaskDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int reviewerId = int.Parse(userIdClaim.Value);

            var reviewer = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == reviewerId);

            if (reviewer == null)
                return BadRequest("Reviewer not found");

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskCode == dto.TaskCode);

            if (task == null)
                return NotFound("Task not found");

            if (task.Status != "completed")
                return BadRequest("Task not completed");

            var alreadyReviewed = await _context.TaskReview
     .AnyAsync(r =>
         r.TaskCode == dto.TaskCode &&
         r.StaffId == dto.StaffId);

            if (alreadyReviewed)
                return BadRequest("Task already reviewed");



            int systemPoints = CalculateTaskScore(task.Due_Date, task.Completed_Date, task.Priority);

            int finalPoints = systemPoints;
            if (dto.IsDelayJustified && dto.ManagerPoints.HasValue)
            {
                finalPoints = dto.ManagerPoints.Value;
            }



            if (dto.IsDelayJustified && dto.ManagerPoints.HasValue)
            {
                finalPoints = dto.ManagerPoints.Value;
            }

            var review = new TaskReview
            {
                TaskCode = dto.TaskCode,
                StaffId = dto.StaffId,

                ReviewedById = $"{reviewer.UserId}-{reviewer.Name}",

                SystemPoints = systemPoints,
                FinalPoints = finalPoints,

                IsDelayJustified = dto.IsDelayJustified,
                DelayReason = dto.DelayReason,
                Comment = dto.Comment,

                ReviewedAt = DateTime.Now
            };

            _context.TaskReview.Add(review);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(task.GoalCode))
{
    var goal = await _context.Goal
        .FirstOrDefaultAsync(g =>
            g.GoalCode == task.GoalCode);

    if (goal != null)
    {
        var goalTasks = await _context.Tasks
            .Where(t => t.GoalCode == goal.GoalCode)
            .ToListAsync();

        var taskCodes = goalTasks
            .Select(t => t.TaskCode)
            .ToList();

        var reviews = await _context.TaskReview
            .Where(r => taskCodes.Contains(r.TaskCode))
            .ToListAsync();

        bool allTasksReviewed = true;

        // Check every staff member of every task
        foreach (var goalTask in goalTasks)
        {
            var members = await _context.TaskMembers
                .Where(tm => tm.TaskCode == goalTask.TaskCode)
                .ToListAsync();

            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member.Assign_To))
                    continue;

                var parts = member.Assign_To.Split('-');

                if (!int.TryParse(parts[0], out int staffId))
                    continue;

                bool reviewed = reviews.Any(r =>
                    r.TaskCode == goalTask.TaskCode &&
                    r.StaffId == staffId);

                if (!reviewed)
                {
                    allTasksReviewed = false;
                    break;
                }
            }

            if (!allTasksReviewed)
                break;
        }

        // Only calculate when every assigned staff member
        // has a review for every task
        if (allTasksReviewed)
        {
            var taskAveragePoints = new List<int>();

            foreach (var goalTask in goalTasks)
            {
                var taskReviews = reviews
                    .Where(r =>
                        r.TaskCode == goalTask.TaskCode)
                    .ToList();

                if (taskReviews.Count == 0)
                    continue;

                double taskAverage = taskReviews
                    .Average(r => r.FinalPoints);

                taskAveragePoints.Add(
                    (int)Math.Round(taskAverage)
                );
            }

            if (taskAveragePoints.Count > 0)
            {
                goal.Goalpoints = CalculateGoalPoints(
                    taskAveragePoints,
                    goal.Priority,
                    goal.DueDate
                );

                await _context.SaveChangesAsync();
            }
        }
    }
}
            if (finalPoints > 0)
            {
                // ✅ Step 1: Get all members for this task
                var taskMembers = await _context.TaskMembers
                    .Where(tm => tm.TaskCode == dto.TaskCode)
                    .ToListAsync();

                if (!taskMembers.Any())
                {
                    Console.WriteLine("No task members found");
                }

                foreach (var member in taskMembers)
                {
                    if (string.IsNullOrWhiteSpace(member.Assign_To))
                        continue;

                    // ✅ Split "2-Abi" → ["2", "Abi"]
                    var parts = member.Assign_To.Split('-');

                    if (parts.Length == 0)
                        continue;

                    // ✅ Take ID part
                    if (!long.TryParse(parts[0], out long receiverId))
                    {
                        Console.WriteLine($"Invalid Assign_To format: {member.Assign_To}");
                        continue;
                    }

                    //_context.Notifications.Add(new Notification
                    //{

                    //    Title = "Task Score",
                    //    Message = $"You got completed task points from manager {reviewer.Name} - {task.Task}",
                    //    SenderId = reviewer.UserId,
                    //    ReceiverId = receiverId, // ✅ now correct (2)

                    //    RelatedId = dto.TaskCode,
                    //    IsRead = false,

                    //});
                }

                await _context.SaveChangesAsync();
                }

            return Ok(new
            {
                message = "Review submitted successfully",
                systemPoints,
                finalPoints
            });
        }


        [Authorize]
        [HttpGet("completed-task")]
        public async Task<IActionResult> GetCompletedTaskPoints()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Invalid token");

            int managerId = int.Parse(userIdClaim);

            var manager = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == managerId);

            if (manager == null)
                return NotFound("Manager not found");

            var department = manager.Department;

            var users = await _context.Users
                .Where(u => u.Department == department)
                .ToDictionaryAsync(u => u.UserId);

            var tasks = await _context.Tasks
                .Where(t => t.Status.ToLower() == "completed")
                .ToListAsync();

            var taskMembers = await _context.TaskMembers
                .ToListAsync();

            var reviews = await _context.TaskReview
                .ToListAsync();

            var result = new List<object>();

            foreach (var task in tasks)
            {
                var members = taskMembers
                    .Where(tm => tm.TaskCode == task.TaskCode)
                    .ToList();

                foreach (var member in members)
                {
                    if (string.IsNullOrWhiteSpace(member.Assign_To))
                        continue;

                    var parts = member.Assign_To.Split(
                        '-',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    if (parts.Length == 0)
                        continue;

                    if (!int.TryParse(parts[0].Trim(), out int staffId))
                        continue;

                    if (!users.TryGetValue(staffId, out var user))
                        continue;

                    var review = reviews.FirstOrDefault(r =>
                        r.TaskCode == task.TaskCode &&
                        r.StaffId == staffId
                    );

                    int systemPoints = CalculateTaskScore(
                        task.Due_Date,
                        task.Completed_Date,
                        task.Priority
                    );

                    result.Add(new
                    {
                        taskCode = task.TaskCode,

                        task = task.Task,

                        description = task.Description,

                        priority = task.Priority,

                        status = task.Status,

                        createdAt = task.Created_At,

                        dueDate = task.Due_Date,

                        completedDate = task.Completed_Date,

                        // ⭐ IMPORTANT FOR FLUTTER
                        staffId = user.UserId,

                        staffName = user.Name,

                        totalMembers = task.Members,

                        assignedTo = member.Assign_To,

                        systemPoints = systemPoints,

                        // ⭐ THIS IS NOW STAFF-SPECIFIC
                        reviewed = review != null,

                        finalPoints = review?.FinalPoints,

                        isDelayJustified = review?.IsDelayJustified ?? false,

                        delayReason = review?.DelayReason,

                        comment = review?.Comment,

                        reviewedAt = review?.ReviewedAt
                    });
                }
            }

            return Ok(result);
        }


        [HttpGet("getreview/{taskCode}")]
        public async Task<IActionResult> GetTaskReview(string taskCode)
        {
            try
            {
                var reviews = await _context.TaskReview
                    .Where(r => r.TaskCode == taskCode)
                    .ToListAsync();

                if (reviews.Count == 0)
                {
                    return Ok(new List<object>());
                }

                // Get all staff IDs
                var staffIds = reviews
                    .Select(r => r.StaffId)
                    .Distinct()
                    .ToList();

                // Get all reviewer IDs
                var reviewerIds = reviews
                    .Where(r => !string.IsNullOrWhiteSpace(r.ReviewedById))
                    .Select(r =>
                    {
                        var parts = r.ReviewedById!.Split('-');

                        if (parts.Length > 0 &&
                            int.TryParse(parts[0], out int id))
                        {
                            return (int?)id;
                        }

                        return null;
                    })
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList();

                var userIds = staffIds
                    .Concat(reviewerIds)
                    .Distinct()
                    .ToList();

                var users = await _context.Users
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.Name);

                var result = reviews.Select(review =>
                {
                    // Staff name
                    string staffName = "Unknown Staff";

                    if (users.TryGetValue(review.StaffId, out var name))
                    {
                        staffName = name;
                    }

                    // Reviewer name
                    string reviewedByName = "";

                    if (!string.IsNullOrWhiteSpace(review.ReviewedById))
                    {
                        var parts = review.ReviewedById.Split('-');

                        if (parts.Length > 0 &&
                            int.TryParse(parts[0], out int reviewerId))
                        {
                            if (users.TryGetValue(reviewerId, out var reviewerName))
                            {
                                reviewedByName = reviewerName;
                            }
                        }
                    }

                    return new
                    {
                        taskCode = review.TaskCode,

                        staffId = review.StaffId,
                        staffName = staffName,

                        systemPoints = review.SystemPoints,
                        finalPoints = review.FinalPoints,

                        isDelayJustified = review.IsDelayJustified,

                        delayReason = review.DelayReason,

                        comment = review.Comment,

                        reviewedBy = reviewedByName,

                        reviewedAt = review.ReviewedAt
                    };
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Failed to get task reviews",
                        error = ex.Message
                    }
                );
            }
        }

        [HttpPost("fiveSpoints")]
        public async Task<IActionResult> SaveWeekly5S(FiveSPoints model)
        {
            var existing = await _context.FiveSPoints
                .FirstOrDefaultAsync(x =>
                    x.Department == model.Department &&
                    x.Year == model.Year &&
                    x.Month == model.Month &&
                    x.Week == model.Week);

            if (existing != null)
            {
                existing.Points = model.Points;
            }
            else
            {
                _context.FiveSPoints.Add(model);
            }

            await _context.SaveChangesAsync();

            return Ok("Saved successfully");
        }


        [HttpPost("apply-leave")]
        public async Task<IActionResult> ApplyLeave([FromBody] LeaveForm model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var sender = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == model.SenderId);

                if (sender == null)
                    return NotFound("Sender not found");

                if (model.ToDate < model.FromDate)
                    return BadRequest("Invalid date range");

                var manager = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.Department == sender.Department &&
                        u.Role == "2");

                if (manager == null)
                    return BadRequest("Manager not found");

                var types = model.LeaveType?.Split(',').Select(x => x.Trim()).ToList();
                var categories = model.LeaveTyp?.Split(',').Select(x => x.Trim()).ToList();

                // How many days in this request are marked "Compensation"?
                int compensationDayCount = categories?.Count(c => c == "Compensation") ?? 0;

                if (compensationDayCount > 1)
                    return BadRequest("Only one Compensation day is allowed per leave request");

                ExtraWork? matchedExtraWork = null;

                if (compensationDayCount == 1)
                {
                    if (model.CompensationExtraWorkId == null)
                        return BadRequest("Please select a compensation day");

                    matchedExtraWork = await _context.ExtraWork.FirstOrDefaultAsync(e =>
                        e.Id == model.CompensationExtraWorkId &&
                        e.UserId == model.SenderId &&
                        e.Status == "Approved" &&
                        !e.IsCompensationUsed);

                    if (matchedExtraWork == null)
                        return BadRequest("Selected compensation day is invalid or already used");
                }

                DateTime currentDate = model.FromDate;
                int index = 0;

                var leaveList = new List<LeaveForm>();
                LeaveForm? compensationLeaveRow = null;

                while (currentDate <= model.ToDate)
                {
                    string type = "Full Day";
                    if (types != null && index < types.Count)
                        type = types[index];

                    string category = "CL";
                    if (categories != null && index < categories.Count && !string.IsNullOrWhiteSpace(categories[index]))
                        category = categories[index];

                    decimal dayValue = type.ToLower().Contains("half") ? 0.5m : 1m;

                    var leaveRow = new LeaveForm
                    {
                        SenderId = model.SenderId,
                        ReceiverId = manager.UserId,
                        Name = model.Name,
                        Designation = model.Designation,
                        Reason = model.Reason,
                        FromDate = currentDate,
                        ToDate = currentDate,
                        LeaveTyp = category,
                        LeaveType = type,
                        TotalDays = dayValue,
                        ContactNumber = model.ContactNumber,
                        Status = "Pending",
                        SubmittedDate = DateTime.Now,
                        ApprovedDate = null,
                        RejectionReason = null
                    };

                    leaveList.Add(leaveRow);

                    if (category == "Compensation")
                        compensationLeaveRow = leaveRow;

                    currentDate = currentDate.AddDays(1);
                    index++;
                }

                await _context.LeaveForm.AddRangeAsync(leaveList);
                await _context.SaveChangesAsync(); // leaveList rows now have real Ids
            
                if (matchedExtraWork != null && compensationLeaveRow != null)
                {
                    compensationLeaveRow.CompensationExtraWorkId = matchedExtraWork.Id;
                    matchedExtraWork.IsCompensationUsed = true;
                    await _context.SaveChangesAsync();
                }

                var receiverId = manager.UserId;
                //_context.Notifications.Add(new Notification
                //{
                 
                //    Title = "Leave Request",
                //    Message = $"You received a leave request from {model.Name}",
                //    SenderId = model.SenderId,
                //    ReceiverId = (long)receiverId,
                   
                //    RelatedId = null,
                //    IsRead = false,
                 
                //});

                //await _context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(manager.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            manager.FcmToken,
                            "Leave Request",
                            $"You received a leave request from {model.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FCM Error: {ex.Message}");
                    }
                }

                return Ok(new
                {
                    message = "Leave applied (split per day)",
                    data = leaveList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [Authorize]
        [HttpGet("get-leaves")]
        public async Task<IActionResult> GetLeaves()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var roleClaim = User.Claims.FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim);
            string role = roleClaim;
            IQueryable<LeaveForm> query = _context.LeaveForm
     .Where(l => l.LeaveType != "Holiday"); 

            if (role == "1")
            {
            }
            else
            {    
                query = query.Where(l => l.SenderId == userId);
            }

            var result = await query
                .OrderByDescending(l => l.SubmittedDate)
                .ToListAsync();

            return Ok(result);
        }


        [Authorize]
        [HttpGet("get-department-leaves")]
        public async Task<IActionResult> GetDepartmentLeaves()
        {
            var userIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == "UserId")?.Value;

            var roleClaim = User.Claims
                .FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            if (roleClaim != "2" && roleClaim != "1")
                return Forbid("Access denied");

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return NotFound("User not found");

            // =====================================================
            // GET DEPARTMENT LEAVES
            // =====================================================

            var leaves = await _context.LeaveForm
                .Where(l =>
                    _context.Users.Any(u =>
                        u.UserId == l.SenderId &&
                        u.Department == user.Department
                    )
                    &&
                    (
                        l.LeaveType == null ||
                        !l.LeaveType.Trim().ToLower().Equals("holiday")
                    )
                )
                .OrderByDescending(l => l.SubmittedDate)
                .ToListAsync();

            // =====================================================
            // GET COMPENSATION EXTRA WORK IDs
            // =====================================================

            var compensationIds = leaves
                .Where(l => l.CompensationExtraWorkId.HasValue)
                .Select(l => l.CompensationExtraWorkId!.Value)
                .Distinct()
                .ToList();

            var extraWorks = await _context.ExtraWork
                .Where(e => compensationIds.Contains(e.Id))
                .ToDictionaryAsync(
                    e => e.Id,
                    e => e.WorkedDate
                );

            // =====================================================
            // RETURN RESULT
            // =====================================================

            var result = leaves.Select(l => new
            {
                l.Id,
                l.SenderId,
                l.ReceiverId,
                l.Name,
                l.Designation,
                l.Reason,
                l.FromDate,
                l.ToDate,
                l.LeaveType,
                l.TotalDays,
                l.ContactNumber,
                l.Status,
                l.ApprovedDate,
                l.RejectionReason,
                l.SubmittedDate,
                l.ApplicationSource,
                l.LeaveTyp,
                l.CompensationExtraWorkId,

                // Compensation worked date
                CompensationWorkedDate =
                    l.CompensationExtraWorkId.HasValue &&
                    extraWorks.ContainsKey(l.CompensationExtraWorkId.Value)
                        ? extraWorks[l.CompensationExtraWorkId.Value]
                        : (DateTime?)null
            });

            return Ok(result);
        }

        [HttpPost("update-leave-status")]
        public async Task<IActionResult> UpdateLeaveStatus(
      [FromQuery] int id,
      [FromQuery] string status,
      [FromQuery] string? reason)
        {
            try
            {
                var leave = await _context.LeaveForm
                    .FirstOrDefaultAsync(l => l.Id == id);

                if (leave == null)
                    return NotFound("Leave not found");

                if (leave.Status != "Pending")
                    return BadRequest("Already processed");

                if (status == "Approved")
                {
                    leave.Status = "Approved";
                    leave.ApprovedDate = DateTime.Now;
                    leave.RejectionReason = null;
                }
                else if (status == "Rejected")
                {
                    if (string.IsNullOrEmpty(reason))
                        return BadRequest("Rejection reason required");

                    leave.Status = "Rejected";
                    leave.RejectionReason = reason;
                    leave.ApprovedDate = null;
                }
                else
                {
                    return BadRequest("Invalid status");
                }

                await _context.SaveChangesAsync();
                // ✅ Send notification to employee (who applied leave)
                var receiverId = leave.SenderId;

                // Get manager name (optional but better message)
                var manager = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == leave.ReceiverId);

                string managerName = manager?.Name ?? "Manager";

                string message = status == "Approved"
                    ? $"Your leave request has been approved by {managerName}"
                    : $"Your leave request has been rejected by {managerName}";

                //_context.Notifications.Add(new Notification
                //{
                   
                //    Title = "Leave Status",
                //    Message = message,
                //    SenderId = leave.ReceiverId, // manager
                //    ReceiverId = (long)receiverId, // employee
                   
                //    RelatedId = leave.Id.ToString(),
                //    IsRead = false,
                   
                //});

               // await _context.SaveChangesAsync();

                return Ok(new { message = "Updated successfully", data = leave });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        [Authorize]
        [HttpDelete("delete-leave/{id}")]
        public async Task<IActionResult> DeleteLeave(int id)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim);

            var leave = await _context.LeaveForm
                .FirstOrDefaultAsync(l => l.Id == id);

            if (leave == null)
                return NotFound("Leave not found");

            if (leave.SenderId != userId)
                return Forbid("You can delete only your own leave");
            DateTime appliedDate = leave.SubmittedDate?.Date ?? DateTime.MinValue;

            if (DateTime.Today > appliedDate)
                return BadRequest("Cannot delete after applied date");

            if (leave.Status == "Approved")
                return BadRequest("Approved leave cannot be deleted");

            _context.LeaveForm.Remove(leave);
            await _context.SaveChangesAsync();

            return Ok("Leave day deleted successfully");
        }

        [Authorize]
        [HttpPost("extra-work")]
        public async Task<IActionResult> AddExtraWork(
      [FromBody] CreateExtraWorkDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim.Value);

            // Validate date
            if (dto.WorkedDate.Date > DateTime.Now.Date)
                return BadRequest("Worked date cannot be a future date.");

            // Validate time
            if (dto.EndTime <= dto.StartTime)
                return BadRequest("End time must be greater than start time.");

            // Prevent duplicate application for same date
            var existing = await _context.ExtraWork
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.WorkedDate.Date == dto.WorkedDate.Date &&
                    x.Status != "Rejected");

            if (existing != null)
            {
                return BadRequest(
                    "You have already applied for extra work on this date.");
            }

            // Calculate total hours
            var duration = dto.EndTime - dto.StartTime;

            decimal totalHours = (decimal)duration.TotalHours;

            var extraWork = new ExtraWork
            {
                UserId = userId,
                WorkedDate = dto.WorkedDate.Date,

                WorkType = dto.WorkType,

                StartTime = dto.StartTime,
                EndTime = dto.EndTime,

                TotalHours = totalHours,

                Reason = dto.Reason,

                Status = "Pending",

                ApprovedBy = null,
                IsCompensationUsed = false,
                
            };

            _context.ExtraWork.Add(extraWork);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Extra work application submitted successfully.",
          
            });
        }


        [Authorize]
        [HttpGet("get-department-extra-work")]
        public async Task<IActionResult> GetDepartmentExtraWork()
        {
            var userIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == "UserId")?.Value;

            var roleClaim = User.Claims
                .FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            // Role 1 = Admin
            // Role 2 = Manager
            if (roleClaim != "1" && roleClaim != "2")
                return Forbid("Access denied");

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid UserId");

            // Get logged-in user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return NotFound("User not found");

            // Get extra work from same department
            var result = await _context.ExtraWork
                .Where(e =>
                    _context.Users.Any(u =>
                        u.UserId == e.UserId &&
                        u.Department == user.Department
                    )
                )
                .OrderByDescending(e => e.WorkedDate)
                .ToListAsync();

            return Ok(result);
        }

        [HttpGet("available-compensation/{userId}")]
        public async Task<IActionResult> GetAvailableCompensation(int userId)
        {
            var list = await _context.ExtraWork
                .Where(e => e.UserId == userId
                         && e.Status == "Approved"
                         && !e.IsCompensationUsed)
                .OrderBy(e => e.WorkedDate)
                .Select(e => new
                {
                    e.Id,
                    e.WorkedDate 
                })
                .ToListAsync();

            return Ok(list);
        }


        [Authorize]
        [HttpGet("get-extra-work")]
        public async Task<IActionResult> GetExtraWork()
        {
            var userIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == "UserId")?.Value;

            var roleClaim = User.Claims
                .FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid UserId");

            IQueryable<ExtraWork> query = _context.ExtraWork;

            // Admin / Manager
            if (roleClaim == "1")
            {
                // They can see all extra work
            }
            else
            {
                // Employee sees only own
                query = query.Where(e => e.UserId == userId);
            }

            var result = await query
                .OrderByDescending(e => e.WorkedDate)
                .ToListAsync();

            return Ok(result);
        }

        [Authorize]
        [HttpPut("approve-extra-work/{id}")]
        public async Task<IActionResult> ApproveExtraWork(int id)
        {
            var managerIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == "UserId")?.Value;

            var roleClaim = User.Claims
                .FirstOrDefault(c => c.Type == "Role")?.Value;

            if (managerIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            if (roleClaim != "1" && roleClaim != "2")
                return Forbid("Access denied");

            int managerId = int.Parse(managerIdClaim);

            // Get manager
            var manager = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == managerId);

            if (manager == null)
                return NotFound("Manager not found");

            // Get extra work
            var extraWork = await _context.ExtraWork
                .FirstOrDefaultAsync(e => e.Id == id);

            if (extraWork == null)
                return NotFound("Extra work not found");

            // Get employee
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == extraWork.UserId);

            if (employee == null)
                return NotFound("Employee not found");

            // Make sure employee belongs to manager's department
            if (manager.Department != employee.Department)
                return Forbid("Employee does not belong to your department");

            // Already processed
            if (extraWork.Status != "Pending")
            {
                return BadRequest(
                    $"Extra work is already {extraWork.Status}.");
            }

            // Approve
            extraWork.Status = "Approved";
            extraWork.ApprovedBy = managerId;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Extra work approved successfully.",
                extraWorkId = extraWork.Id,
                approvedBy = managerId,
                status = extraWork.Status
            });
        }
      

        [Authorize]
        [HttpPost("apply-permission")]
        public async Task<IActionResult> ApplyPermission([FromBody] PermissionForm model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // 1. Get employee ID from JWT
                var userIdClaim = User.FindFirst("UserId")?.Value;

                if (userIdClaim == null)
                    return Unauthorized("Invalid token");

                int senderId = int.Parse(userIdClaim);

                // 2. Get employee
                var sender = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == senderId);

                if (sender == null)
                    return NotFound("User not found");

                // 3. Validate time
                if (model.ToTime <= model.FromTime)
                    return BadRequest("Invalid time range");

                // 4. Calculate requested permission minutes
                var requestedMinutes =
                    (int)(model.ToTime - model.FromTime).TotalMinutes;

                if (requestedMinutes <= 0)
                    return BadRequest("Invalid permission duration");

           

                var monthStart = new DateTime(
                    model.Date.Year,
                    model.Date.Month,
                    1
                );

                var nextMonth = monthStart.AddMonths(1);

                // Approved + Pending permissions count
                // Rejected permissions do NOT count
                var usedMinutes = await _context.PermissionForm
                    .Where(p =>
                        p.SenderId == senderId &&
                        p.Date >= monthStart &&
                        p.Date < nextMonth &&
                        p.Status != "Rejected")
                    .SumAsync(p => (decimal?)p.TotalHours * 60) ?? 0;

                // Convert decimal to int
                int existingPermissionMinutes =
                    (int)Math.Round(usedMinutes);

                int monthlyLimit = 60;

                int totalAfterRequest =
                    existingPermissionMinutes + requestedMinutes;

             

                var manager = await (
      from u in _context.Users
      join r in _context.Roles
          on u.Role equals r.Id.ToString()
      where u.Department == sender.Department
            && r.RoleName == "Manager"
            && r.Status == true
      select u
  ).FirstOrDefaultAsync();

                if (manager == null)
                    return BadRequest("Manager not found");

                if (totalAfterRequest > monthlyLimit)
                {
                    
                    var leave = new LeaveForm
                    {
                        SenderId = senderId,
                        ReceiverId = manager.UserId,

                        Name = model.Name,
                        Designation = model.Designation,
                        Reason = model.Reason,

                        FromDate = model.Date,
                        ToDate = model.Date,

                        LeaveType = "Full Day",
                        LeaveTyp = "LOP",
                        ApplicationSource = "PermissionExceeded",
                        TotalDays = 1,

                        Status = "Pending",
                        SubmittedDate = DateTime.Now,

                        ApprovedDate = null,
                        RejectionReason = null,

                        ContactNumber = null
                    };
                    _context.LeaveForm.Add(leave);

                    await _context.SaveChangesAsync();

                    // Notification to manager
                    //_context.Notifications.Add(new Notification
                    //{

                    //    Title = "Leave Request",
                    //    Message =
                    //        $"Permission limit exceeded. Leave request received from {model.Name}",

                    //    SenderId = senderId,
                    //    ReceiverId = (long)manager.UserId,


                    //    RelatedId = null,
                    //    IsRead = false,

                    //});

                    //await _context.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(manager.FcmToken))
                    {
                        try
                        {
                            await _firebaseNotificationService.SendNotificationAsync(
                                manager.FcmToken,
                                "Permission Request",
                                $"You received a Permission request from {model.Name}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"FCM Error: {ex}");
                        }
                    }
                    //.

                    return Ok(new
                    {
                        message =
                            "Monthly permission limit exceeded. Leave request created.",
                        applicationType = "Leave",
                        requestedMinutes = requestedMinutes,
                        usedMinutes = existingPermissionMinutes,
                        totalMinutes = totalAfterRequest,
                        monthlyLimit = monthlyLimit,
                        data = leave
                    });
                }

                // =====================================================
                // 9. LIMIT NOT EXCEEDED → NORMAL PERMISSION
                // =====================================================

                var minutes = (model.ToTime - model.FromTime).TotalMinutes;

                var hours = Math.Round(
                    (decimal)(minutes / 60),
                    2
                );

                var permission = new PermissionForm
                {
                    SenderId = senderId,
                    ReceiverId = manager.UserId,

                    Name = model.Name,
                    Designation = model.Designation,
                    Reason = model.Reason,

                    Date = model.Date,
                    FromTime = model.FromTime,
                    ToTime = model.ToTime,

                    TotalHours = hours,

                    Status = "Pending",
                    SubmittedDate = DateTime.Now
                };

                _context.PermissionForm.Add(permission);

                await _context.SaveChangesAsync();

                // Notification
                //_context.Notifications.Add(new Notification
                //{

                //    Title = "Permission Request",

                //    Message =
                //        $"You received a Permission request from {model.Name}",

                //    SenderId = senderId,
                //    ReceiverId = (long)manager.UserId,


                //    RelatedId = null,
                //    IsRead = false,

                //});

                //await _context.SaveChangesAsync();
                if (!string.IsNullOrWhiteSpace(manager.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            manager.FcmToken,
                            "Permission Request",
                            $"You received a permission request from {model.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FCM Error: {ex}");
                    }
                }
                return Ok(new
                {
                    message = "Permission applied successfully",

                    applicationType = "Permission",

                    requestedMinutes = requestedMinutes,
                    usedMinutes = existingPermissionMinutes,
                    totalMinutes = totalAfterRequest,
                    remainingMinutes =
                        monthlyLimit - totalAfterRequest,

                    data = permission
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=================================");
                Console.WriteLine("APPLY PERMISSION ERROR");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("INNER EXCEPTION:");
                Console.WriteLine(ex.InnerException?.ToString());
                Console.WriteLine("=================================");

                return StatusCode(500, new
                {
                    message = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        [Authorize]
        [HttpGet("get-permissions")]
        public async Task<IActionResult> GetPermissions()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var roleClaim = User.Claims.FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim);
            string role = roleClaim;

            IQueryable<PermissionForm> query = _context.PermissionForm;

            if (role == "1")
            {
                // Director sees all permissions
            }
            else
            {
                // Staff / others → only their permissions
                query = query.Where(p => p.SenderId == userId);
            }

            var result = await query
                .OrderByDescending(p => p.SubmittedDate)
                .ToListAsync();

            return Ok(result);
        }


        [Authorize]
        [HttpGet("get-department-permissions")]
        public async Task<IActionResult> GetDepartmentPermissions()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var roleClaim = User.Claims.FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            if (roleClaim != "2" && roleClaim != "1")
                return Forbid("Access denied");

            int userId = int.Parse(userIdClaim);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return NotFound("User not found");

            var result = await _context.PermissionForm
                .Where(p => _context.Users.Any(u =>
                    u.UserId == p.SenderId &&
                    u.Department == user.Department))
                .OrderByDescending(p => p.SubmittedDate)
                .ToListAsync();

            return Ok(result);
        }


        [HttpPost("update-permission-status")]
        public async Task<IActionResult> UpdatePermissionStatus( [FromQuery] int id,string status)
        {
            try
            {
                var permission = await _context.PermissionForm
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (permission == null)
                    return NotFound("Permission not found");

                if (permission.Status != "Pending")
                    return BadRequest("Already processed");

                if (status == "Approved")
                {
                    permission.Status = "Approved";
                }
                else if (status == "Rejected")
                {
                    permission.Status = "Rejected";
                }
                else
                {
                    return BadRequest("Invalid status");
                }

                await _context.SaveChangesAsync();

                // Send notification to employee
                var manager = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == permission.ReceiverId);

                string managerName = manager?.Name ?? "Manager";

                string message = status == "Approved"
                    ? $"Your permission request has been approved by {managerName}"
                    : $"Your permission request has been rejected by {managerName}";

                //_context.Notifications.Add(new Notification
                //{
                    
                //    Title = "Permission Status",
                //    Message = message,
                //    SenderId = permission.ReceiverId,
                //    ReceiverId = permission.SenderId,
                   
                //    RelatedId = permission.Id.ToString(),
                //    IsRead = false,
                  
                //});

                //await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Permission status updated successfully",
                    data = permission
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("delete-permission/{id}")]
        public async Task<IActionResult> DeletePermission(int id)
        {
            try
            {
                var permission = await _context.PermissionForm
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (permission == null)
                    return NotFound("Permission not found");

                if (permission.Status != "Pending")
                    return BadRequest("Only pending permissions can be deleted");

                _context.PermissionForm.Remove(permission);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Permission deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        } 

        [HttpPost("check-monthly")]
        public async Task<IActionResult> CheckMonthly(int staffId, int month, int year)
        {
            try
            {
                await _service.CalculateMonthly(staffId, month, year);
                return Ok("Calculation Done");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }


        [HttpPost("overtime-entry")]
        public async Task<IActionResult> AddOverTime([FromBody] CreateOverTimeDto dto)
        {
            var fromTime = TimeSpan.Parse(dto.FromTime);
            var toTime = TimeSpan.Parse(dto.ToTime);

            var totalHours = decimal.Round((decimal)(toTime - fromTime).TotalHours,2);

            var overtime = new OverTime
            {
                Uid = dto.Uid,
                Dept = dto.Dept,
                Date = dto.Date,

                FromTime = fromTime,
                ToTime = toTime,
                Reason=dto.Reason,
                TotalHours = totalHours,
                isApprov = false,
                Approved_by = ""
            };

            _context.OverTime.Add(overtime);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Overtime request submitted",
                overtime.Id
            });
        }

        [Authorize]
        [HttpPut("overtime-approve/{id}")]
        public async Task<IActionResult> ApproveOverTime(int id,ApproveOverTimeDto dto)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (currentUser == null)
                return Unauthorized();

            if (currentUser.Role != "2")
                return Forbid();

            var overtime = await _context.OverTime
                .FirstOrDefaultAsync(x => x.Id == id);

            if (overtime == null)
                return NotFound("Overtime not found");

            overtime.isApprov = dto.IsApproved;
            overtime.Approved_by = currentUser.Name;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = dto.IsApproved
                    ? "Overtime Approved"
                    : "Overtime Rejected"
            });
        }

        [Authorize]
        [HttpGet("my-overtimes")]
        public async Task<IActionResult> MyOverTimes()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized();

            int userId = int.Parse(userIdClaim);

            var data = await (
                from ot in _context.OverTime
                join u in _context.Users
                    on ot.Uid equals u.UserId
                where ot.Uid == userId
                orderby ot.Date descending
                select new
                {
                    ot.Id,
                    ot.Uid,
                    Name = u.Name,
                    ot.Dept,
                    ot.Date,
                    ot.FromTime,
                    ot.ToTime,
                    ot.TotalHours,
                    ot.Reason,
                    ot.Approved_by,
                    ot.isApprov
                }
            ).ToListAsync();

            return Ok(data);
        }

        [Authorize]
        [HttpGet("department-overtimes")]
        public async Task<IActionResult> DepartmentOverTimes()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized();

            int userId = int.Parse(userIdClaim);

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (currentUser == null)
                return Unauthorized();

            var data = await (
                from ot in _context.OverTime
                join u in _context.Users
                    on ot.Uid equals u.UserId
                where u.Department == currentUser.Department
                orderby ot.Date descending
                select new
                {
                    ot.Id,
                    ot.Uid,
                    Name = u.Name,
                    ot.Dept,
                    ot.Date,
                    ot.FromTime,
                    ot.ToTime,
                    ot.TotalHours,
                    ot.Reason,
                    ot.Approved_by,
                    ot.isApprov
                }
            ).ToListAsync();

            return Ok(data);
        }


        [HttpGet("getovertimes")]
        public async Task<IActionResult> GetOverTimes()
        {
            var overtimes = await _context.OverTime
                .Where(ot => ot.isApprov == true)
                .Join(
                    _context.Users,
                    ot => ot.Uid,
                    u => u.UserId,
                    (ot, u) => new
                    {
                        ot.Id,
                        ot.Uid,
                        name = u.Name,
                        ot.Dept,
                        ot.Date,
                        ot.Approved_by,
                        ot.FromTime,
                        ot.ToTime,
                        ot.TotalHours,
                        ot.Reason,
                        ot.isApprov
                    }
                )
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            return Ok(overtimes);
        }

        [Authorize]
        [HttpPost("punch-correction")]
        public async Task<IActionResult> CreatePunchCorrection(
    [FromBody] PunchCorrectionDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim.Value);

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (user == null)
                return NotFound("User not found");

       
            // Don't allow duplicate request for same employee/date/type
            var existing = await _context.PunchCorrection
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.Date.Date == dto.Date.Date &&
                    x.CorrectionType == dto.CorrectionType &&
                    x.Status != "Rejected");

            if (existing != null)
            {
                return BadRequest(
                    "A punch correction already exists for this date.");
            }

            var correction = new PunchCorrection
            {
                UserId = userId,
                Date = dto.Date.Date,
                CorrectionType = dto.CorrectionType,
                PunchTime = dto.PunchTime,
                Reason = dto.Reason,
                Status = "Pending",
            };

            _context.PunchCorrection.Add(correction);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Punch correction request submitted successfully",
                id = correction.Id,
                status = correction.Status
            });
        }


        [Authorize]
        [HttpPut("punch-correction/{id}")]
        public async Task<IActionResult> ManagerPunchCorrection(int id,[FromBody] PunchCorrectionActionDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int managerId = int.Parse(userIdClaim.Value);

            var correction = await _context.PunchCorrection
                .FirstOrDefaultAsync(x => x.Id == id);

            if (correction == null)
                return NotFound("Punch correction not found");

            if (correction.Status != "Pending")
                return BadRequest("This request has already been processed.");

            correction.ApprovedById = managerId;

            correction.Status = dto.Approved
                ? "Approved"
                : "Rejected";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = dto.Approved
                    ? "Punch correction approved"
                    : "Punch correction rejected",

                status = correction.Status
            });
        }


        [Authorize]
        [HttpGet("department-punch-corrections")]
        public async Task<IActionResult> GetDepartmentPunchCorrections()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim.Value, out int loggedInUserId))
                return Unauthorized("Invalid UserId");

            // Get logged-in user
            var loggedInUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == loggedInUserId);

            if (loggedInUser == null)
                return NotFound("User not found");

            // Only Role 2 can access department list
            if (loggedInUser.Role != "2")
                return Forbid();

            // Get punch corrections of all users
            // belonging to the logged-in user's department
            var corrections = await (
                from correction in _context.PunchCorrection
                join user in _context.Users
                    on correction.UserId equals user.UserId
                where user.Department == loggedInUser.Department
                orderby correction.Date descending
                select new
                {
                    correction.Id,

                    correction.UserId,

                    // Get from Users table
                    EmployeeName = user.Name,
                    Department = user.Department,

                    correction.Date,
                    correction.CorrectionType,
                    correction.PunchTime,
                    correction.Reason,
                    correction.Status,

                    correction.ApprovedById,
                  
                }
            ).ToListAsync();

            return Ok(new
            {
                department = loggedInUser.Department,
                count = corrections.Count,
                data = corrections
            });
        }


        [Authorize]
        [HttpGet("my-punch-corrections")]
        public async Task<IActionResult> GetMyPunchCorrections()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized("Invalid UserId");

            var corrections = await (
                from correction in _context.PunchCorrection
                join user in _context.Users
                    on correction.UserId equals user.UserId
                where correction.UserId == userId
                orderby correction.Date descending
                select new
                {
                    correction.Id,

                    correction.UserId,

                    // From Users table
                    EmployeeName = user.Name,
                    Department = user.Department,

                    correction.Date,
                    correction.CorrectionType,
                    correction.PunchTime,
                    correction.Reason,
                    correction.Status,
                }
            ).ToListAsync();

            return Ok(new
            {
                count = corrections.Count,
                data = corrections
            });
        }


        [HttpPost("add-attitude-behaviour-score")]
        public async Task<IActionResult> AddAttitudeBehaviourScore(
    [FromBody] AttitudeBehaviourScoreDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Validate scores
                if (model.Communication < 1 || model.Communication > 5)
                    return BadRequest("Communication score must be between 1 and 5.");

                if (model.Punctuality < 1 || model.Punctuality > 5)
                    return BadRequest("Punctuality score must be between 1 and 5.");

                if (model.Integrity < 1 || model.Integrity > 5)
                    return BadRequest("Integrity score must be between 1 and 5.");

                // Find staff
                var staff = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == model.StaffId);

                if (staff == null)
                    return NotFound("Staff not found.");

                // Normalize date to month
                var scoreMonth = new DateTime(
                    model.Date.Year,
                    model.Date.Month,
                    1
                );

                // Check duplicate score for same staff/month
                var existingScore = await _context.AttitudeBehaviourScore
                    .FirstOrDefaultAsync(x =>
                        x.StaffId == model.StaffId &&
                        x.Date.Year == scoreMonth.Year &&
                        x.Date.Month == scoreMonth.Month);

                if (existingScore != null)
                {
                    return Conflict(
                        "Attitude & Behaviour score already exists for this staff for this month."
                    );
                }

                // Calculate total
                int total =
                    model.Communication +
                    model.Punctuality +
                    model.Integrity;

                var score = new AttitudeBehaviourScore
                {
                    StaffId = model.StaffId,

                    Department = staff.Department ?? "",

                    Communication = model.Communication,

                    Punctuality = model.Punctuality,

                    Integrity = model.Integrity,

                    Total = total,

                    Date = scoreMonth
                };

                _context.AttitudeBehaviourScore.Add(score);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Attitude & Behaviour score added successfully.",
                    data = new
                    {
                        score.Id,
                        score.StaffId,
                        staff.Name,
                        score.Department,
                        score.Communication,
                        score.Punctuality,
                        score.Integrity,
                        score.Total,
                        score.Date
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Error while adding attitude & behaviour score.",
                        error = ex.Message
                    }
                );
            }
        }


        [Authorize]
        [HttpGet("department-attitude-behaviour-scores")]
        public async Task<IActionResult> GetDepartmentAttitudeBehaviourScores()
        {
            try
            {
                // Get logged-in user ID from JWT
                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int userId))
                    return Unauthorized("Invalid User ID.");

                // Get logged-in user
                var loggedInUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (loggedInUser == null)
                    return NotFound("Logged-in user not found.");

                if (string.IsNullOrWhiteSpace(loggedInUser.Department))
                    return BadRequest("User department not found.");

                // Get scores of users in the same department
                var scores = await (
                    from score in _context.AttitudeBehaviourScore
                    join user in _context.Users
                        on score.StaffId equals user.UserId
                    where user.Department == loggedInUser.Department
                    orderby user.Name
                    select new
                    {
                        score.Id,
                        StaffId = user.UserId,
                        StaffName = user.Name,
                        Department = user.Department,

                        score.Communication,
                        score.Punctuality,
                        score.Integrity,
                        score.Total,
                        score.Date
                    }
                ).ToListAsync();

                return Ok(new
                {
                    department = loggedInUser.Department,
                    count = scores.Count,
                    scores = scores
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Error while getting department attitude & behaviour scores.",
                        error = ex.Message
                    }
                );
            }
        }

        private int CalculateTaskScore(DateTime dueDate, DateTime? completedDate, string priority)
        {
            if (completedDate == null)
                return 0;

            int timeScore;

            int lateDays = (completedDate.Value.Date - dueDate.Date).Days;

            if (lateDays < 0)
                timeScore = 100;
            else if (lateDays == 0)
                timeScore = 95;
            else if (lateDays == 1)
                timeScore = 90;
            else if (lateDays == 2)
                timeScore = 85;
            else if (lateDays == 3)
                timeScore = 80;
            else if (lateDays == 4)
                timeScore = 75;
            else if (lateDays == 5)
                timeScore = 70;
            else if (lateDays == 6)
                timeScore = 65;
            else if (lateDays == 7)
                timeScore = 60;
            else if (lateDays == 8)
                timeScore = 55;
            else
                timeScore = 50;

            int priorityBonus = 0;

            switch (priority?.ToLower())
            {
                case "high":
                    priorityBonus = 5;
                    break;
                case "medium":
                    priorityBonus = 3;
                    break;
            }

            int finalScore = timeScore + priorityBonus;

            return Math.Clamp(finalScore, 0, 100);
        }

       
        private int CalculateGoalPoints(
    List<int> taskAveragePoints,
    string goalPriority,
    DateTime? dueDate)
        {
            if (taskAveragePoints == null ||
                taskAveragePoints.Count == 0)
                return 0;

            // Average of TASK scores
            double goalPoints = taskAveragePoints.Average();

            // Priority bonus
            switch (goalPriority?.Trim().ToLower())
            {
                case "high":
                    goalPoints += 5;
                    break;

                case "medium":
                    goalPoints += 3;
                    break;
            }

            // Due date bonus
            if (dueDate.HasValue)
            {
                if (DateTime.Now.Date <= dueDate.Value.Date)
                    goalPoints += 5;
                else
                    goalPoints -= 5;
            }

            return (int)Math.Clamp(
                Math.Round(goalPoints),
                0,
                100
            );
        }
    }
}
