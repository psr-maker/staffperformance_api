using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using staff.Services;
using staff_work_tracking.Data;
using StaffWork_Track.Services;
using System.Net.NetworkInformation;
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

        

        [Authorize]
        [HttpGet("allStaffGoals/{department}")]
        public async Task<IActionResult> GetGoalsByDepartment(string department)
        {
            // =========================================================
            // 1. GET USERS IN DEPARTMENT
            // =========================================================

            var departmentUsers = await _context.Users
                .Where(u => u.Department == department)
                .ToListAsync();

            if (!departmentUsers.Any())
            {
                return Ok(new
                {
                    department,
                    totalGoals = 0,
                    goals = new List<object>()
                });
            }

            var departmentUserIds = departmentUsers
                .Select(u => u.UserId)
                .ToList();

            // =========================================================
            // 2. GET GOAL IDS ASSIGNED TO DEPARTMENT USERS
            // =========================================================

            var assignedGoalIds = await _context.GoalAssignment
                .Where(a => departmentUserIds.Contains(a.UserId))
                .Select(a => a.GoalId)
                .Distinct()
                .ToListAsync();

            if (!assignedGoalIds.Any())
            {
                return Ok(new
                {
                    department,
                    totalGoals = 0,
                    goals = new List<object>()
                });
            }

            // =========================================================
            // 3. GET GOALS
            // =========================================================

            var goals = await _context.Goal
                .Where(g => assignedGoalIds.Contains(g.Id))
                .OrderByDescending(g => g.Id)
                .ToListAsync();

            // =========================================================
            // 4. INCLUDE YEARLY PARENT GOALS
            // =========================================================

            var parentGoalIds = goals
                .Where(g => g.ParentGoalId.HasValue)
                .Select(g => g.ParentGoalId!.Value)
                .Distinct()
                .ToList();

            if (parentGoalIds.Any())
            {
                var parentGoals = await _context.Goal
                    .Where(g => parentGoalIds.Contains(g.Id))
                    .ToListAsync();

                goals.AddRange(parentGoals);
            }

            // Remove duplicates
            goals = goals
                .GroupBy(g => g.Id)
                .Select(g => g.First())
                .OrderByDescending(g => g.Id)
                .ToList();

            // =========================================================
            // 5. GET GOAL CODES
            // =========================================================

            var goalCodes = goals
                .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
                .Select(g => g.GoalCode!)
                .ToList();

            // =========================================================
            // 6. GET TASKS
            // =========================================================

            var tasks = await _context.Tasks
                .Where(t => goalCodes.Contains(t.GoalCode))
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            // =========================================================
            // 7. GET TASK MEMBERS
            // =========================================================

            var taskCodes = tasks
                .Select(t => t.TaskCode)
                .Distinct()
                .ToList();

            var taskMembers = await _context.TaskMembers
                .Where(tm => taskCodes.Contains(tm.TaskCode))
                .ToListAsync();

            // =========================================================
            // 8. GET ALL USERS
            // =========================================================

            var allUsers = await _context.Users
                .ToListAsync();

            // =========================================================
            // 9. BUILD RESULT
            // =========================================================

            var result = new List<object>();

            foreach (var goal in goals)
            {
                // =====================================================
                // ASSIGNED USERS FOR THIS GOAL
                // =====================================================

                var goalAssignments = await _context.GoalAssignment
                    .Where(a => a.GoalId == goal.Id)
                    .ToListAsync();

                var assignedUsers = goalAssignments
                    .Where(a => departmentUserIds.Contains(a.UserId))
                    .Select(a =>
                    {
                        var assignedUser = departmentUsers
                            .FirstOrDefault(u => u.UserId == a.UserId);

                        if (assignedUser == null)
                            return null;

                        return new
                        {
                            userId = assignedUser.UserId,
                            name = assignedUser.Name,
                            email = assignedUser.Email,
                            department = assignedUser.Department,
                            role = assignedUser.Role
                        };
                    })
                    .Where(x => x != null)
                    .ToList();

                // =====================================================
                // TASKS FOR THIS GOAL
                // =====================================================

                var goalTasks = tasks
                    .Where(t => t.GoalCode == goal.GoalCode)
                    .ToList();

                var taskResult = new List<object>();

                foreach (var task in goalTasks)
                {
                    // =================================================
                    // TASK ASSIGNED TO
                    // =================================================

                    var assignedToUsers = new List<object>();

                    var membersForTask = taskMembers
                        .Where(tm => tm.TaskCode == task.TaskCode)
                        .ToList();

                    foreach (var member in membersForTask)
                    {
                        if (string.IsNullOrWhiteSpace(member.Assign_To))
                            continue;

                        if (!member.Assign_To.Contains("-"))
                            continue;

                        var parts = member.Assign_To.Split('-');

                        if (!int.TryParse(parts[0], out int assignedUserId))
                            continue;

                        var assignedUser = allUsers
                            .FirstOrDefault(u => u.UserId == assignedUserId);

                        if (assignedUser == null)
                            continue;

                        if (!departmentUserIds.Contains(assignedUser.UserId))
                            continue;

                        assignedToUsers.Add(new
                        {
                            userId = assignedUser.UserId,
                            name = assignedUser.Name,
                            department = assignedUser.Department,
                            role = assignedUser.Role
                        });
                    }

                    // Remove duplicate task members
                    var uniqueAssignedToUsers = assignedToUsers
                        .GroupBy(x => x.GetType().GetProperty("userId")!.GetValue(x))
                        .Select(x => x.First())
                        .ToList();

                    // =================================================
                    // ASSIGNED BY
                    // =================================================

                    string assignerName = "N/A";
                    string assignerRole = "N/A";
                    string assignerDepartment = "N/A";

                    var assignerMember = membersForTask
                        .FirstOrDefault(tm =>
                            !string.IsNullOrWhiteSpace(tm.Assign_By) &&
                            tm.Assign_By.Contains("-"));

                    if (assignerMember != null)
                    {
                        var parts = assignerMember.Assign_By!.Split('-');

                        if (int.TryParse(parts[0], out int assignerId))
                        {
                            var assigner = allUsers
                                .FirstOrDefault(u => u.UserId == assignerId);

                            if (assigner != null)
                            {
                                assignerName = assigner.Name;
                                assignerRole = assigner.Role;
                                assignerDepartment = assigner.Department;
                            }
                        }
                    }

                    // =================================================
                    // ADD TASK
                    // =================================================

                    taskResult.Add(new
                    {
                        taskCode = task.TaskCode,
                        task = task.Task,
                        description = task.Description,
                        priority = task.Priority,
                        status = task.Status,
                        createdAt = task.Created_At,
                        dueDate = task.Due_Date,
                        totalMembers = task.Members,

                        assignedBy = assignerName,
                        assignerRole = assignerRole,
                        assignerDepartment = assignerDepartment,

                        assignedTo = uniqueAssignedToUsers
                    });
                }

                // =====================================================
                // CREATOR
                // =====================================================

                var creator = allUsers
                    .FirstOrDefault(u => u.UserId == goal.CreatedBy);

                // =====================================================
                // PARENT GOAL
                // =====================================================

                string? parentGoalCode = null;
                string? parentGoalTitle = null;

                if (goal.ParentGoalId.HasValue)
                {
                    var parentGoal = goals
                        .FirstOrDefault(g => g.Id == goal.ParentGoalId.Value);

                    if (parentGoal != null)
                    {
                        parentGoalCode = parentGoal.GoalCode;
                        parentGoalTitle = parentGoal.Title;
                    }
                }

                // =====================================================
                // ADD GOAL
                // =====================================================

                result.Add(new
                {
                    id = goal.Id,

                    goalCode = goal.GoalCode,
                    goalType = goal.GoalType,

                    parentGoalId = goal.ParentGoalId,
                    parentGoalCode = parentGoalCode,
                    parentGoalTitle = parentGoalTitle,

                    title = goal.Title,
                    priority = goal.Priority,

                    startDate = goal.StartDate,
                    dueDate = goal.DueDate,
                    completedDate = goal.Completed_Date,

                    status = goal.Status,
                    progress = goal.Progress,
                    goalpoints = goal.Goalpoints,

                    createdBy = goal.CreatedBy,
                    createdByName = creator?.Name ?? "N/A",
                    createdByDepartment = creator?.Department ?? "N/A",

                    assignedUsers = assignedUsers,

                    taskCount = taskResult.Count,
                    tasks = taskResult
                });
            }

            // =========================================================
            // 10. RESPONSE
            // =========================================================

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
            try
            {
                // =========================================================
                // 1. Check user exists
                // =========================================================

                var admin = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == adminId);

                if (admin == null)
                {
                    return NotFound(new
                    {
                        message = "User not found."
                    });
                }


                // =========================================================
                // 2. Get Goal IDs assigned to this user
                //    New structure:
                //
                //    GoalAssignments
                //    GoalId
                //    UserId
                // =========================================================

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => a.UserId == adminId)
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();


                if (!assignedGoalIds.Any())
                {
                    return Ok(new List<object>());
                }


                // =========================================================
                // 3. Get assigned goals
                // =========================================================

                var goals = await _context.Goal
                    .Where(g => assignedGoalIds.Contains(g.Id))
                    .OrderByDescending(g => g.Id)
                    .ToListAsync();


                // =========================================================
                // 4. Include parent Yearly goals
                //    If assigned goal is Monthly, also include its Yearly goal
                // =========================================================

                var parentGoalIds = goals
                    .Where(g => g.ParentGoalId.HasValue)
                    .Select(g => g.ParentGoalId!.Value)
                    .Distinct()
                    .ToList();

                if (parentGoalIds.Any())
                {
                    var parentGoals = await _context.Goal
                        .Where(g => parentGoalIds.Contains(g.Id))
                        .ToListAsync();

                    goals = goals
                        .Concat(parentGoals)
                        .GroupBy(g => g.Id)
                        .Select(g => g.First())
                        .OrderByDescending(g => g.Id)
                        .ToList();
                }


                // =========================================================
                // 5. Get goal codes
                // =========================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrEmpty(g.GoalCode))
                    .Select(g => g.GoalCode!)
                    .ToList();


                // =========================================================
                // 6. Get tasks under these goals
                // =========================================================

                var tasks = await _context.Tasks
                    .Where(t =>
                        t.GoalCode != null &&
                        goalCodes.Contains(t.GoalCode))
                    .OrderByDescending(t => t.Created_At)
                    .ToListAsync();


                // =========================================================
                // 7. Get task members and users
                // =========================================================

                var taskMembers = await _context.TaskMembers
                    .ToListAsync();

                var users = await _context.Users
                    .ToListAsync();


                // =========================================================
                // 8. Build response
                // =========================================================

                var result = goals.Select(g =>
                {
                    // -----------------------------------------------------
                    // Tasks belonging to this goal
                    // -----------------------------------------------------

                    var tasksForGoal = tasks
                        .Where(t => t.GoalCode == g.GoalCode)

                        // -------------------------------------------------
                        // Only tasks where this admin/user is assigned
                        // -------------------------------------------------

                        .Where(t =>
                            taskMembers.Any(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_To) &&
                                tm.Assign_To
                                    .Split('-')[0]
                                    .Trim() == adminId.ToString()
                            )
                        )

                        .Select(t =>
                        {
                            // =================================================
                            // Find task assigner
                            // =================================================

                            var assigner = taskMembers
                                .Where(tm =>
                                    tm.TaskCode == t.TaskCode &&
                                    !string.IsNullOrEmpty(tm.Assign_By))
                                .Select(tm =>
                                {
                                    var parts = tm.Assign_By!
                                        .Split('-', 2);

                                    if (!int.TryParse(parts[0], out int userId))
                                        return null;

                                    return users.FirstOrDefault(
                                        u => u.UserId == userId
                                    );
                                })
                                .FirstOrDefault(u => u != null);


                            // =================================================
                            // Find all users assigned to task
                            // =================================================

                            var assignedUsers = taskMembers
                                .Where(tm =>
                                    tm.TaskCode == t.TaskCode &&
                                    !string.IsNullOrEmpty(tm.Assign_To))
                                .SelectMany(tm =>
                                {
                                    var parts = tm.Assign_To!
                                        .Split('-', 2);

                                    if (!int.TryParse(parts[0], out int userId))
                                        return Enumerable.Empty<int>();

                                    return new[] { userId };
                                })
                                .Distinct()
                                .Select(userId =>
                                    users.FirstOrDefault(
                                        u => u.UserId == userId
                                    )
                                )
                                .Where(u => u != null)
                                .Select(u => new
                                {
                                    UserId = u!.UserId,
                                    Name = u.Name,
                                    Role = u.Role,
                                    Department = u.Department
                                })
                                .ToList();


                            // =================================================
                            // Task response
                            // =================================================

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
                                assignerDepartment =
                                    assigner?.Department ?? "N/A",

                                assignedTo = assignedUsers
                            };
                        })
                        .ToList();


                    // =====================================================
                    // Get goal creator
                    // =====================================================

                    var creator = users.FirstOrDefault(
                        u => u.UserId == g.CreatedBy
                    );


                    // =====================================================
                    // Get assigned users for this goal
                    // =====================================================

                    var goalAssignedUsers = _context.GoalAssignment
                        .Where(a => a.GoalId == g.Id)
                        .AsEnumerable()
                        .Select(a =>
                            users.FirstOrDefault(
                                u => u.UserId == a.UserId
                            )
                        )
                        .Where(u => u != null)
                        .Select(u => new
                        {
                            UserId = u!.UserId,
                            Name = u.Name,
                            Role = u.Role,
                            Department = u.Department
                        })
                        .ToList();


                    // =====================================================
                    // Goal response
                    // =====================================================

                    return new
                    {
                        id = g.Id,
                        goalCode = g.GoalCode,
                        goalType = g.GoalType,
                        parentGoalId = g.ParentGoalId,

                        title = g.Title,
                        priority = g.Priority,

                        startDate = g.StartDate,
                        dueDate = g.DueDate,
                        completedDate = g.Completed_Date,

                        status = g.Status,
                        progress = g.Progress,
                        goalpoints = g.Goalpoints,

                        createdBy = g.CreatedBy,
                        createdByName = creator?.Name ?? "N/A",
                        createdByDepartment =
                            creator?.Department ?? "N/A",

                        assignedUsers = goalAssignedUsers,

                        taskCount = tasksForGoal.Count,
                        tasks = tasksForGoal
                    };
                }).ToList();


                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while getting assigned goals.",
                    error = ex.Message
                });
            }
        }


        [HttpGet("Managergoalsassigned/{adminId}")]
        public async Task<IActionResult> GetTasksAssignedByAdmin(int adminId)
        {
            try
            {
                // =========================================================
                // 1. Validate admin/manager
                // =========================================================

                var admin = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == adminId);

                if (admin == null)
                    return NotFound(new
                    {
                        message = "Admin/Manager not found."
                    });


                // =========================================================
                // 2. Get goals CREATED BY this admin/manager
                //    New Goal table uses CreatedBy instead of Assign_By
                // =========================================================

                var goals = await _context.Goal
                    .Where(g => g.CreatedBy == adminId)
                    .OrderByDescending(g => g.Id)
                    .ToListAsync();

                if (!goals.Any())
                {
                    return Ok(new List<object>());
                }


                // =========================================================
                // 3. Get goal codes
                // =========================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrEmpty(g.GoalCode))
                    .Select(g => g.GoalCode!)
                    .ToList();


                // =========================================================
                // 4. Get tasks under these goals
                // =========================================================

                var tasks = await _context.Tasks
                    .Where(t => t.GoalCode != null &&
                                goalCodes.Contains(t.GoalCode))
                    .OrderByDescending(t => t.Created_At)
                    .ToListAsync();


                // =========================================================
                // 5. Get TaskMembers
                // =========================================================

                var taskMembers = await _context.TaskMembers
                    .ToListAsync();


                // =========================================================
                // 6. Get users
                // =========================================================

                var users = await _context.Users
                    .ToListAsync();


                // =========================================================
                // 7. Build response
                // =========================================================

                var result = goals.Select(g =>
                {
                    // -----------------------------------------------------
                    // Tasks belonging to this goal
                    // -----------------------------------------------------

                    var tasksForGoal = tasks
                        .Where(t => t.GoalCode == g.GoalCode)

                        // Only tasks assigned BY this admin/manager
                        .Where(t =>
                            taskMembers.Any(tm =>
                                tm.TaskCode == t.TaskCode &&
                                !string.IsNullOrEmpty(tm.Assign_By) &&
                                tm.Assign_By
                                    .Split('-')[0]
                                    .Trim() == adminId.ToString()
                            )
                        )

                        .Select(t =>
                        {
                            // =================================================
                            // Find who assigned this task
                            // =================================================

                            var assigner = taskMembers
                                .Where(tm =>
                                    tm.TaskCode == t.TaskCode &&
                                    !string.IsNullOrEmpty(tm.Assign_By))
                                .Select(tm =>
                                {
                                    var parts = tm.Assign_By!
                                        .Split('-', 2);

                                    if (!int.TryParse(parts[0], out int userId))
                                        return null;

                                    return users.FirstOrDefault(
                                        u => u.UserId == userId
                                    );
                                })
                                .FirstOrDefault(u => u != null);


                            // =================================================
                            // Find users assigned to this task
                            // =================================================

                            var assignedTo = taskMembers
                                .Where(tm =>
                                    tm.TaskCode == t.TaskCode &&
                                    !string.IsNullOrEmpty(tm.Assign_To))
                                .SelectMany(tm =>
                                {
                                    var parts = tm.Assign_To!
                                        .Split('-', 2);

                                    if (!int.TryParse(parts[0], out int userId))
                                        return Enumerable.Empty<int>();

                                    return new[] { userId };
                                })
                                .Distinct()
                                .Select(userId =>
                                    users.FirstOrDefault(
                                        u => u.UserId == userId
                                    )
                                )
                                .Where(u => u != null)
                                .Select(u => new
                                {
                                    UserId = u!.UserId,
                                    Name = u.Name,
                                    Role = u.Role,
                                    Department = u.Department
                                })
                                .ToList();


                            // =================================================
                            // Task response
                            // =================================================

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


                    // =====================================================
                    // Goal response
                    // =====================================================

                    return new
                    {
                        id = g.Id,
                        goalCode = g.GoalCode,
                        goalType = g.GoalType,
                        parentGoalId = g.ParentGoalId,

                        title = g.Title,
                        priority = g.Priority,

                        startDate = g.StartDate,
                        dueDate = g.DueDate,
                        completedDate = g.Completed_Date,

                        status = g.Status,
                        progress = g.Progress,
                        goalpoints = g.Goalpoints,

                        // New Goal table
                        createdBy = g.CreatedBy,

                        createdByName = admin.Name,
                        createdByDepartment = admin.Department,

                        taskCount = tasksForGoal.Count,
                        tasks = tasksForGoal
                    };
                }).ToList();


                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while getting manager goals and tasks.",
                    error = ex.Message
                });
            }
        }


        [Authorize]
        [HttpPut("update-task-status")]
        public async Task<IActionResult> UpdateTaskStatus(
            [FromBody] UpdateTaskStatusDto dto)
        {
            // =====================================================
            // 1. GET LOGGED-IN USER
            // =====================================================

            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized("Invalid UserId in token");


            // =====================================================
            // 2. GET USER
            // =====================================================

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return NotFound("User not found");


            // =====================================================
            // 3. GET TASK
            // =====================================================

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskCode == dto.TaskCode);

            if (task == null)
                return NotFound("Task not found");


            // =====================================================
            // 4. GET CURRENT USER'S TASK MEMBER
            // =====================================================

            var taskMember = await _context.TaskMembers
                .FirstOrDefaultAsync(tm =>
                    tm.TaskCode == dto.TaskCode &&
                    !string.IsNullOrWhiteSpace(tm.Assign_To) &&
                    tm.Assign_To.StartsWith(userId + "-"));

            if (taskMember == null)
                return BadRequest("User is not assigned to this task");


            // =====================================================
            // 5. VALIDATE STATUS
            // =====================================================

            if (string.IsNullOrWhiteSpace(dto.Status))
                return BadRequest("Status is required.");

            var requestedStatus = dto.Status.Trim().ToLower();


            // =====================================================
            // 6. QUANTITY TASK
            // =====================================================

            if (task.Quantity.HasValue)
            {
                // -------------------------------------------------
                // Completed quantity is required
                // -------------------------------------------------

                if (!dto.CompletedQuantity.HasValue)
                {
                    return BadRequest(new
                    {
                        message = "Completed quantity is required for this task.",
                        quantity = task.Quantity,
                        completedQuantity = task.CompletedQuantity
                    });
                }


                // -------------------------------------------------
                // Quantity cannot be negative
                // -------------------------------------------------

                if (dto.CompletedQuantity.Value < 0)
                {
                    return BadRequest(
                        "Completed quantity cannot be negative.");
                }


                // =================================================
                // GET ALL SPLITS FOR THIS TASK
                // =================================================

                var shares = await _context.TaskQuantitySplit
                    .Where(s => s.TaskCode == task.TaskCode)
                    .ToListAsync();


                // =================================================
                // SPLIT QUANTITY TASK
                // =================================================

                if (shares.Any())
                {
                    // -------------------------------------------------
                    // Current member must have SplitId
                    // -------------------------------------------------

                    if (!taskMember.SplitId.HasValue)
                    {
                        return BadRequest(
                            "This member is not assigned to a quantity share.");
                    }


                    // -------------------------------------------------
                    // Find current member's split
                    // -------------------------------------------------

                    var currentSplit = shares.FirstOrDefault(s =>
                        s.Id == taskMember.SplitId.Value);

                    if (currentSplit == null)
                    {
                        return BadRequest(
                            "Quantity share not found for this member.");
                    }


                    // -------------------------------------------------
                    // Validate completed quantity against share
                    // -------------------------------------------------

                    if (dto.CompletedQuantity.Value > currentSplit.Quantity)
                    {
                        return BadRequest(new
                        {
                            message =
                                "Completed quantity cannot exceed your assigned share.",

                            shareId = currentSplit.Id,

                            shareQuantity = currentSplit.Quantity,

                            requestedCompletedQuantity =
                                dto.CompletedQuantity.Value
                        });
                    }


                    // =================================================
                    // UPDATE CURRENT MEMBER'S STATUS
                    // =================================================

                    taskMember.UserStatus = requestedStatus;


                    // =================================================
                    // UPDATE CURRENT SHARE COMPLETED QUANTITY
                    // =================================================

                    currentSplit.CompletedQuantity =
                        dto.CompletedQuantity.Value;


                    // =================================================
                    // GET ALL MEMBERS FOR THIS TASK
                    // =================================================

                    var allTaskMembers = await _context.TaskMembers
                        .Where(m => m.TaskCode == task.TaskCode)
                        .ToListAsync();


                    // =================================================
                    // CHECK EVERY SPLIT
                    // =================================================

                    bool allSplitsCompleted = true;


                    foreach (var share in shares)
                    {
                        // ---------------------------------------------
                        // Members belonging to this split
                        // ---------------------------------------------

                        var shareMembers = allTaskMembers
                            .Where(m => m.SplitId == share.Id)
                            .ToList();


                        // ---------------------------------------------
                        // Safety check
                        // ---------------------------------------------

                        if (!shareMembers.Any())
                        {
                            allSplitsCompleted = false;
                            break;
                        }


                        // ---------------------------------------------
                        // Check ALL members of this split
                        // ---------------------------------------------

                        bool allMembersCompleted = shareMembers.All(m =>
                            !string.IsNullOrWhiteSpace(m.UserStatus) &&
                            m.UserStatus.Trim().Equals(
                                "completed",
                                StringComparison.OrdinalIgnoreCase));


                        // ---------------------------------------------
                        // Check quantity of this split
                        // ---------------------------------------------

                        bool quantityCompleted =
                            share.CompletedQuantity >= share.Quantity;


                        // ---------------------------------------------
                        // BOTH must be completed
                        // ---------------------------------------------

                        if (!allMembersCompleted || !quantityCompleted)
                        {
                            allSplitsCompleted = false;
                            break;
                        }
                    }


                    // =================================================
                    // CALCULATE MAIN TASK COMPLETED QUANTITY
                    // =================================================

                    task.CompletedQuantity = shares
                        .Sum(s => s.CompletedQuantity);


                    // =================================================
                    // UPDATE MAIN TASK STATUS
                    // =================================================

                    if (allSplitsCompleted)
                    {
                        task.Status = "completed";
                    }
                    else
                    {
                        task.Status = "inprogress";
                    }
                }
                else
                {
                    // =================================================
                    // NORMAL QUANTITY TASK WITHOUT SPLITS
                    // =================================================

                    taskMember.UserStatus = requestedStatus;

                    task.CompletedQuantity =
                        dto.CompletedQuantity.Value;

                    task.Status = requestedStatus;
                }
            }
            else
            {
                // =====================================================
                // NORMAL TASK WITHOUT QUANTITY
                // =====================================================

                taskMember.UserStatus = requestedStatus;

                task.CompletedQuantity = null;

                task.Status = requestedStatus;
            }


            // =====================================================
            // 7. COMPLETED DATE
            // =====================================================

            if (!string.IsNullOrWhiteSpace(task.Status) &&
                task.Status.Trim().Equals(
                    "completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                var indiaTimeZone =
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

                task.Completed_Date =
                    TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.UtcNow,
                        indiaTimeZone);
            }
            else
            {
                task.Completed_Date = default(DateTime);
            }


            // =====================================================
            // 8. SAVE TASK + MEMBER + SPLIT
            // =====================================================

            await _context.SaveChangesAsync();


            // =====================================================
            // 9. UPDATE GOAL
            // =====================================================

            if (!string.IsNullOrWhiteSpace(task.GoalCode))
            {
                var goal = await _context.Goal
                    .FirstOrDefaultAsync(g =>
                        g.GoalCode == task.GoalCode);

                if (goal != null)
                {
                    // =================================================
                    // GET ALL TASKS FOR GOAL
                    // =================================================

                    var goalTasks = await _context.Tasks
                        .Where(t => t.GoalCode == goal.GoalCode)
                        .ToListAsync();


                    // =================================================
                    // TOTAL TASKS
                    // =================================================

                    int total = goalTasks.Count;


                    // =================================================
                    // COMPLETED TASKS
                    // =================================================

                    int completed = goalTasks.Count(t =>
                        !string.IsNullOrWhiteSpace(t.Status) &&
                        t.Status.Trim().Equals(
                            "completed",
                            StringComparison.OrdinalIgnoreCase));


                    // =================================================
                    // NOT STARTED TASKS
                    // =================================================

                    int notStarted = goalTasks.Count(t =>
                        !string.IsNullOrWhiteSpace(t.Status) &&
                        (
                            t.Status.Trim().Equals(
                                "not started",
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            t.Status.Trim().Equals(
                                "notstarted",
                                StringComparison.OrdinalIgnoreCase)
                        ));


                    // =================================================
                    // UPDATE GOAL STATUS
                    // =================================================

                    if (total > 0 && completed == total)
                    {
                        goal.Status = "completed";

                        var indiaTimeZone =
                            TimeZoneInfo.FindSystemTimeZoneById(
                                "Asia/Kolkata");

                        goal.Completed_Date =
                            TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.UtcNow,
                                indiaTimeZone);
                    }
                    else if (total > 0 && notStarted == total)
                    {
                        goal.Status = "not started";

                        goal.Completed_Date = null;
                    }
                    else
                    {
                        goal.Status = "inprogress";

                        goal.Completed_Date = null;
                    }


                    // =================================================
                    // UPDATE GOAL PROGRESS
                    // =================================================

                    goal.Progress =
                        total == 0
                            ? 0
                            : (int)(((double)completed / total) * 100);


                    // =================================================
                    // UPDATE GOAL COMPLETED QUANTITY
                    // =================================================

                    if (goal.TargetQuantity.HasValue)
                    {
                        int completedQuantity = goalTasks
                            .Where(t => t.CompletedQuantity.HasValue)
                            .Sum(t => t.CompletedQuantity!.Value);

                        goal.CompletedQuantity =
                            completedQuantity;
                    }
                    else
                    {
                        goal.CompletedQuantity = null;
                    }


                    // =================================================
                    // SAVE GOAL
                    // =================================================

                    await _context.SaveChangesAsync();
                }
            }


            // =====================================================
            // 10. RESPONSE
            // =====================================================

            return Ok(new
            {
                message = "Task status updated successfully",

                taskCode = task.TaskCode,

                userId = userId,

                status = task.Status,

                quantity = task.Quantity,

                completedQuantity =
                    task.CompletedQuantity,

                pendingQuantity =
                    task.Quantity.HasValue
                        ? Math.Max(
                            0,
                            task.Quantity.Value -
                            (task.CompletedQuantity ?? 0))
                        : (int?)null,

                completedDate =
                    task.Completed_Date == default(DateTime)
                        ? (DateTime?)null
                        : task.Completed_Date
            });
        }

        [Authorize]
        [HttpPost("review-task")]
        public async Task<IActionResult> SubmitReview([FromBody] ReviewTaskDto dto)
        {

            if (dto == null)
                return BadRequest("Review data is required.");

            if (string.IsNullOrWhiteSpace(dto.TaskCode))
                return BadRequest("TaskCode is required.");

            if (dto.StaffId <= 0)
                return BadRequest("StaffId is required.");

            // If delay is justified, reason is mandatory
            if (dto.IsDelayJustified &&
                string.IsNullOrWhiteSpace(dto.DelayReason))
            {
                return BadRequest(
                    "Delay reason is required when delay is justified.");
            }

            if (dto.IsDelayJustified &&
                !dto.ManagerPoints.HasValue)
            {
                return BadRequest(
                    "ManagerPoints is required when delay is justified.");
            }

            if (dto.ManagerPoints.HasValue &&
                (dto.ManagerPoints.Value < 0 ||
                 dto.ManagerPoints.Value > 100))
            {
                return BadRequest(
                    "ManagerPoints must be between 0 and 100.");
            }

            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrWhiteSpace(userIdClaim))
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim, out int reviewerId))
                return Unauthorized("Invalid User ID in token.");

            var reviewer = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == reviewerId);

            if (reviewer == null)
                return BadRequest("Reviewer not found.");

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t =>
                    t.TaskCode == dto.TaskCode);

            if (task == null)
                return NotFound("Task not found.");

            if (!string.Equals(
                    task.Status,
                    "completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "Task is not completed yet.");
            }

            var taskMember = await _context.TaskMembers
                .FirstOrDefaultAsync(tm =>
                    tm.TaskCode == dto.TaskCode &&
                    tm.Assign_To != null &&
                    tm.Assign_To.StartsWith(
                        dto.StaffId.ToString() + "-"));

            if (taskMember == null)
            {
                return BadRequest(
                    "This staff member is not assigned to this task.");
            }

            var alreadyReviewed = await _context.TaskReview
                .AnyAsync(r =>
                    r.TaskCode == dto.TaskCode &&
                    r.StaffId == dto.StaffId);

            if (alreadyReviewed)
            {
                return BadRequest(
                    "This task has already been reviewed for this staff member.");
            }

            int systemPoints = CalculateTaskScore(
     task.Due_Date,
     task.Completed_Date,
     task.Priority,
     task.EndTime,
     task.Created_At
 );

            // System Points = maximum 90
            systemPoints = Math.Clamp(systemPoints, 0, 90);


            // Final Points
            // Normally = System Points
            // Justified delay = Manager Points (maximum 100)

            int finalPoints = systemPoints;

            if (dto.IsDelayJustified)
            {
                finalPoints = dto.ManagerPoints!.Value;

                // Manager can give up to 100
                finalPoints = Math.Clamp(finalPoints, 0, 100);
            }
            var review = new TaskReview
            {
                TaskCode = dto.TaskCode,

                StaffId = dto.StaffId,

                ReviewedById =
                    $"{reviewer.UserId}-{reviewer.Name}",

                SystemPoints = systemPoints,

                FinalPoints = finalPoints,

                IsDelayJustified = dto.IsDelayJustified,

                DelayReason = dto.IsDelayJustified
                    ? dto.DelayReason?.Trim()
                    : null,

                Comment = string.IsNullOrWhiteSpace(dto.Comment)
                    ? null
                    : dto.Comment.Trim(),

                ReviewedAt = DateTime.Now
            };

            _context.TaskReview.Add(review);

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(task.GoalCode))
            {
                var goal = await _context.Goal
                    .FirstOrDefaultAsync(g =>
                        g.GoalCode == task.GoalCode);

                if (goal != null)
                {

                    var goalTasks = await _context.Tasks
                        .Where(t =>
                            t.GoalCode == goal.GoalCode)
                        .ToListAsync();

                    var goalTaskCodes = goalTasks
                        .Select(t => t.TaskCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .ToList();

                    var reviews = await _context.TaskReview
                        .Where(r =>
                            r.TaskCode != null &&
                            goalTaskCodes.Contains(r.TaskCode))
                        .ToListAsync();

                    bool allTasksReviewed = true;

                    foreach (var goalTask in goalTasks)
                    {
                        var members = await _context.TaskMembers
                            .Where(tm =>
                                tm.TaskCode == goalTask.TaskCode)
                            .ToListAsync();

                        foreach (var member in members)
                        {
                            if (string.IsNullOrWhiteSpace(
                                member.Assign_To))
                            {
                                continue;
                            }
                            var parts = member.Assign_To
                                .Split('-', 3);

                            if (parts.Length == 0)
                                continue;


                            if (!int.TryParse(
                                parts[0],
                                out int memberStaffId))
                            {
                                continue;
                            }

                            bool reviewed = reviews.Any(r =>
                                r.TaskCode == goalTask.TaskCode &&
                                r.StaffId == memberStaffId);

                            if (!reviewed)
                            {
                                allTasksReviewed = false;
                                break;
                            }
                        }

                        if (!allTasksReviewed)
                            break;
                    }


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
                                (int)Math.Round(taskAverage));
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

            return Ok(new
            {
                message = "Review submitted successfully.",

                taskCode = dto.TaskCode,

                staffId = dto.StaffId,

                systemPoints = systemPoints,

                finalPoints = finalPoints,

                isDelayJustified = dto.IsDelayJustified,

                delayReason = dto.IsDelayJustified
                    ? dto.DelayReason
                    : null,

                comment = dto.Comment,

                reviewedBy = $"{reviewer.UserId}-{reviewer.Name}"
            });
        }


        [Authorize]
        [HttpGet("completed-task")]
        public async Task<IActionResult> GetCompletedTaskPoints()
        {
            // =====================================================
            // 1. GET LOGGED-IN USER
            // =====================================================

            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrWhiteSpace(userIdClaim))
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim, out int loggedInUserId))
                return Unauthorized("Invalid UserId in token");


            // =====================================================
            // 2. GET LOGGED-IN USER
            // =====================================================

            var loggedInUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == loggedInUserId);

            if (loggedInUser == null)
                return NotFound("User not found");


            string loggedInRole = loggedInUser.Role?.Trim() ?? "";


            // =====================================================
            // 3. GET ALL DEPARTMENTS
            // =====================================================

            var departments = await _context.Departments
                .ToListAsync();


            // =====================================================
            // 4. GET DEPARTMENT ACCESS
            //
            // DepartmentAccess:
            //
            // UserId           = Division Head UserId
            // RoleId           = Division Head Role
            // HeadDepartmentId = Head department
            // SubDepartmentId  = Division/sub-department
            // =====================================================

            var departmentAccess = await _context.DepartmentAccess
                .ToListAsync();


            // =====================================================
            // 5. GET ALL USERS
            // =====================================================

            var allUsers = await _context.Users
                .ToListAsync();


            // =====================================================
            // 6. DETERMINE ALLOWED STAFF
            // =====================================================

            var allowedUserIds = new HashSet<int>();


            // =====================================================
            // DIRECTOR
            //
            // Director can see:
            //
            // 1. Division Head's division tasks
            // 2. Managers who do NOT belong to a Division Head
            //
            // Managers already belonging to a Division Head
            // are not separately shown as standalone managers.
            // =====================================================

            if (loggedInRole == "1")
            {
                // -------------------------------------------------
                // 6A. GET ALL DIVISION HEADS
                // -------------------------------------------------

                var divisionHeadIds = departmentAccess
                    .Where(x => x.RoleId == 2)
                    .Select(x => x.UserId)
                    .Distinct()
                    .ToHashSet();


                // -------------------------------------------------
                // 6B. GET ALL SUB-DEPARTMENTS BELONGING
                //     TO DIVISION HEADS
                // -------------------------------------------------

                var divisionSubDepartmentIds = departmentAccess
                    .Where(x =>
                        x.RoleId == 2 &&
                        divisionHeadIds.Contains(x.UserId))
                    .Select(x => x.SubDepartmentId)
                    .Distinct()
                    .ToHashSet();


                // -------------------------------------------------
                // 6C. CONVERT SUB-DEPARTMENT IDS TO DEPARTMENT NAMES
                // -------------------------------------------------

                var divisionDepartmentNames = departments
                    .Where(d =>
                        divisionSubDepartmentIds.Contains(d.Id))
                    .Select(d => d.DepartmentName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);


                // -------------------------------------------------
                // 6D. ADD DIVISION HEADS THEMSELVES
                // -------------------------------------------------

                foreach (var divisionHeadId in divisionHeadIds)
                {
                    allowedUserIds.Add(divisionHeadId);
                }


                // -------------------------------------------------
                // 6E. ADD USERS BELONGING TO DIVISION DEPARTMENTS
                // -------------------------------------------------

                foreach (var user in allUsers)
                {
                    if (string.IsNullOrWhiteSpace(user.Department))
                        continue;

                    if (divisionDepartmentNames.Contains(
                            user.Department.Trim()))
                    {
                        allowedUserIds.Add(user.UserId);
                    }
                }


                // -------------------------------------------------
                // 6F. GET MANAGERS WHO DO NOT BELONG
                //     TO ANY DIVISION HEAD
                // -------------------------------------------------

                var allDivisionSubDepartmentIds =
                    departmentAccess
                        .Where(x =>
                            x.RoleId == 2 &&
                            x.SubDepartmentId > 0)
                        .Select(x => x.SubDepartmentId)
                        .Distinct()
                        .ToHashSet();


                var standaloneManagers = allUsers
                    .Where(u =>
                        u.Role == "3" &&
                        !string.IsNullOrWhiteSpace(u.Department))
                    .Where(manager =>
                    {
                        // Find manager's department
                        var managerDepartment =
                            departments.FirstOrDefault(d =>
                                !string.IsNullOrWhiteSpace(d.DepartmentName) &&
                                d.DepartmentName.Trim()
                                    .Equals(
                                        manager.Department.Trim(),
                                        StringComparison.OrdinalIgnoreCase));

                        if (managerDepartment == null)
                            return true;

                        // If this department is NOT mapped
                        // to a Division Head, manager is standalone.
                        return !allDivisionSubDepartmentIds.Contains(
                            managerDepartment.Id);
                    })
                    .ToList();


                // -------------------------------------------------
                // 6G. ADD STANDALONE MANAGER + THEIR DEPARTMENT
                // -------------------------------------------------

                foreach (var manager in standaloneManagers)
                {
                    allowedUserIds.Add(manager.UserId);

                    // Also include staff belonging to this manager's
                    // own department.
                    foreach (var staff in allUsers)
                    {
                        if (string.IsNullOrWhiteSpace(staff.Department))
                            continue;

                        if (staff.Department.Trim().Equals(
                                manager.Department.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            allowedUserIds.Add(staff.UserId);
                        }
                    }
                }
            }


            // =====================================================
            // DIVISION HEAD
            //
            // Division Head can see completed tasks belonging
            // to departments assigned to that Division Head.
            // =====================================================

            else if (loggedInRole == "2")
            {
                // -------------------------------------------------
                // GET SUB-DEPARTMENTS ASSIGNED TO THIS
                // DIVISION HEAD
                // -------------------------------------------------

                var mySubDepartmentIds = departmentAccess
                    .Where(x =>
                        x.UserId == loggedInUserId &&
                        x.RoleId == 2)
                    .Select(x => x.SubDepartmentId)
                    .Distinct()
                    .ToHashSet();


                // -------------------------------------------------
                // GET DEPARTMENT NAMES
                // -------------------------------------------------

                var myDepartmentNames = departments
                    .Where(d =>
                        mySubDepartmentIds.Contains(d.Id))
                    .Select(d => d.DepartmentName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);


                // -------------------------------------------------
                // ADD USERS BELONGING TO THIS DIVISION
                // -------------------------------------------------

                foreach (var user in allUsers)
                {
                    if (string.IsNullOrWhiteSpace(user.Department))
                        continue;

                    if (myDepartmentNames.Contains(
                            user.Department.Trim()))
                    {
                        allowedUserIds.Add(user.UserId);
                    }
                }


                // -------------------------------------------------
                // INCLUDE DIVISION HEAD HIMSELF
                // -------------------------------------------------

                allowedUserIds.Add(loggedInUserId);
            }


            // =====================================================
            // NORMAL MANAGER
            //
            // Manager can see completed tasks from their
            // own department.
            //
            // If that manager belongs to a Division Head,
            // the manager still sees only their own department
            // when logged in directly.
            // =====================================================

            else if (loggedInRole == "3")
            {
                foreach (var user in allUsers)
                {
                    if (string.IsNullOrWhiteSpace(user.Department))
                        continue;

                    if (user.Department.Trim().Equals(
                            loggedInUser.Department?.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        allowedUserIds.Add(user.UserId);
                    }
                }
            }


            // =====================================================
            // OTHER ROLES
            // =====================================================

            else
            {
                return Forbid();
            }


            // =====================================================
            // 7. GET COMPLETED TASKS
            // =====================================================

            var tasks = await _context.Tasks
                .Where(t =>
                    t.Status != null &&
                    t.Status.ToLower() == "completed")
                .OrderByDescending(t => t.Completed_Date)
                .ToListAsync();


            // =====================================================
            // 8. GET TASK MEMBERS
            // =====================================================

            var taskMembers = await _context.TaskMembers
                .ToListAsync();


            // =====================================================
            // 9. GET REVIEWS
            // =====================================================

            var reviews = await _context.TaskReview
                .ToListAsync();


            // =====================================================
            // 10. BUILD RESULT
            // =====================================================

            var result = new List<object>();


            foreach (var task in tasks)
            {
                // -------------------------------------------------
                // GET MEMBERS FOR TASK
                // -------------------------------------------------

                var members = taskMembers
                    .Where(tm =>
                        tm.TaskCode == task.TaskCode)
                    .ToList();


                foreach (var member in members)
                {
                    // -------------------------------------------------
                    // VALIDATE ASSIGNED USER
                    // -------------------------------------------------

                    if (string.IsNullOrWhiteSpace(member.Assign_To))
                        continue;


                    var parts = member.Assign_To.Split(
                        '-',
                        StringSplitOptions.RemoveEmptyEntries);


                    if (parts.Length == 0)
                        continue;


                    if (!int.TryParse(
                            parts[0].Trim(),
                            out int staffId))
                    {
                        continue;
                    }


                    // -------------------------------------------------
                    // ACCESS CHECK
                    // -------------------------------------------------

                    if (!allowedUserIds.Contains(staffId))
                        continue;


                    // -------------------------------------------------
                    // GET STAFF USER
                    // -------------------------------------------------

                    var user = allUsers.FirstOrDefault(
                        u => u.UserId == staffId);


                    if (user == null)
                        continue;


                    // -------------------------------------------------
                    // GET REVIEW FOR THIS STAFF
                    // -------------------------------------------------

                    var review = reviews.FirstOrDefault(r =>
                        r.TaskCode == task.TaskCode &&
                        r.StaffId == staffId);


                    // -------------------------------------------------
                    // CALCULATE SYSTEM POINTS
                    //
                    // Quantity is included:
                    //
                    // Target 30
                    // Completed 25
                    //
                    // Normal score 90
                    //
                    // 90 * (25 / 30)
                    // = 75
                    // -------------------------------------------------

                    int systemPoints = CalculateTaskScore(
                        task.Due_Date,
                        task.Completed_Date,
                        task.Priority,
                        task.EndTime,
                        task.Created_At,
                        task.Quantity,
                        task.CompletedQuantity
                    );


                    // -------------------------------------------------
                    // ADD RESULT
                    // -------------------------------------------------

                    result.Add(new
                    {
                        taskCode = task.TaskCode,

                        task = task.Task,

                        description = task.Description,

                        priority = task.Priority,

                        status = task.Status,

                        createdAt = task.Created_At,

                        dueDate = task.Due_Date,

                        completedDate =
                            task.Completed_Date == default(DateTime)
                                ? (DateTime?)null
                                : task.Completed_Date,


                        // =============================================
                        // STAFF
                        // =============================================

                        staffId = user.UserId,

                        staffName = user.Name,

                        staffEmail = user.Email,

                        staffDepartment = user.Department,

                        staffRole = user.Role,


                        // =============================================
                        // ASSIGNMENT
                        // =============================================

                        totalMembers = task.Members,

                        assignedTo = member.Assign_To,

                        taskMemberCode = member.TMCode,

                        userStatus = member.UserStatus,

                        splitId = member.SplitId,


                        // =============================================
                        // QUANTITY
                        // =============================================

                        quantity = task.Quantity,

                        completedQuantity =
                            task.CompletedQuantity,

                        pendingQuantity =
                            task.Quantity.HasValue
                                ? Math.Max(
                                    0,
                                    task.Quantity.Value -
                                    (task.CompletedQuantity ?? 0))
                                : (int?)null,


                        // =============================================
                        // PERFORMANCE
                        // =============================================

                        performanceType =
                            task.PerformanceType,

                        systemPoints = systemPoints,


                        // =============================================
                        // REVIEW
                        // =============================================

                        reviewed = review != null,

                        finalPoints =
                            review?.FinalPoints,

                        isDelayJustified =
                            review?.IsDelayJustified ?? false,

                        delayReason =
                            review?.DelayReason,

                        comment =
                            review?.Comment,

                        reviewedAt =
                            review?.ReviewedAt
                    });
                }
            }


            // =====================================================
            // 11. RETURN
            // =====================================================

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


        [Authorize]
        [HttpPost("fiveSpoints")]
        public async Task<IActionResult> SaveWeekly5S([FromBody] FiveSPoints model)
        {
            

            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized(new
                {
                    message = "User ID not found in token."
                });
            }

            if (!int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new
                {
                    message = "Invalid User ID in token."
                });
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return Unauthorized(new
                {
                    message = "User not found."
                });
            }

            var roleName = user.Role?.Trim();

            if (string.IsNullOrEmpty(roleName))
            {
                return Forbid();
            }

            var role = await _context.Roles
                .FirstOrDefaultAsync(r =>
                    r.RoleName.ToLower() == roleName.ToLower() &&
                    r.Status == true);

            if (role == null)
            {
                return Forbid();
            }


            if (!string.Equals(
                    role.RoleName,
                    "Auditor",
                    StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new
                {
                    message = "Only Auditor can save 5S points."
                });
            }

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

            return Ok(new
            {
                message = "5S points saved successfully."
            });
        }


        [Authorize]
        [HttpPost("apply-leave")]
        public async Task<IActionResult> ApplyLeave([FromBody] LeaveForm model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            try
            {
                // ==============================
                // 1. Get logged-in user from JWT
                // ==============================
                var userIdClaim = User.FindFirst("UserId");
                var roleClaim = User.FindFirst("Role");
                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");
                if (!int.TryParse(userIdClaim.Value, out int loggedInUserId))
                    return Unauthorized("Invalid User ID.");
                if (roleClaim == null)
                    return Unauthorized("Role not found in token.");
                string loggedInRole = roleClaim.Value;
                // ==============================
                // 2. Get sender
                // ==============================
                var sender = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == model.SenderId);

                if (sender == null)
                    return NotFound("Sender not found");

                string senderRole = sender.Role;

                // ==============================
                // 4. Determine receiver
                // ==============================

                int receiverId;

                var senderDepartment = await _context.Departments
                    .FirstOrDefaultAsync(d => d.DepartmentName == sender.Department);

                if (senderDepartment == null)
                    return BadRequest($"Department '{sender.Department}' not found.");

                if (senderRole == "2")
                {
                    var director = await _context.Users
                        .FirstOrDefaultAsync(u => u.Role == "1");
                    if (director == null)
                        return BadRequest("Director not found.");
                    receiverId = director.UserId;
                }
                else if (senderRole == "3")
                {
                    var divisionHeadAccess = await _context.DepartmentAccess
                        .FirstOrDefaultAsync(x => x.SubDepartmentId == senderDepartment.Id);

                    if (divisionHeadAccess != null)
                    {
                        var divisionHead = await _context.Users
                            .FirstOrDefaultAsync(u =>
                                u.UserId == divisionHeadAccess.UserId &&
                                u.Role == divisionHeadAccess.RoleId.ToString());
                        if (divisionHead == null)
                            return BadRequest("Division Head assigned to this department was not found.");
                        receiverId = divisionHead.UserId;
                    }
                    else
                    {
                        var director = await _context.Users
                            .FirstOrDefaultAsync(u => u.Role == "1");
                        if (director == null)
                            return BadRequest("Director not found.");
                        receiverId = director.UserId;
                    }
                }
                else
                {
                    var manager = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.Department == sender.Department &&
                            u.Role == "3");
                    if (manager == null)
                        return BadRequest("Manager not found for this department.");
                    receiverId = manager.UserId;
                }

                // ==============================
                // 5. Leave types/categories
                // ==============================
                var types = model.LeaveType?
                    .Split(',')
                    .Select(x => x.Trim())
                    .ToList();
                var categories = model.LeaveTyp?
                    .Split(',')
                    .Select(x => x.Trim())
                    .ToList();
                int compensationDayCount =
                    categories?.Count(c => c == "Compensation") ?? 0;
                if (compensationDayCount > 1)
                    return BadRequest(
                        "Only one Compensation day is allowed per leave request"
                    );
                // ==============================
                // 6. Compensation validation
                // ==============================
                ExtraWork? matchedExtraWork = null;
                if (compensationDayCount == 1)
                {
                    if (model.CompensationExtraWorkId == null)
                        return BadRequest("Please select a compensation day");
                    matchedExtraWork = await _context.ExtraWork
                        .FirstOrDefaultAsync(e =>
                            e.Id == model.CompensationExtraWorkId &&
                            e.StaffId == model.SenderId &&
                            e.Status == "Approved" &&
                            !e.IsCompensationUsed);
                    if (matchedExtraWork == null)
                        return BadRequest(
                            "Selected compensation day is invalid or already used"
                        );
                }
                // ==============================
                // 7. Create leave rows
                // ==============================
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
                    if (categories != null &&
                        index < categories.Count &&
                        !string.IsNullOrWhiteSpace(categories[index]))
                    {
                        category = categories[index];
                    }
                    decimal dayValue =
                        type.ToLower().Contains("half")
                            ? 0.5m
                            : 1m;
                    var leaveRow = new LeaveForm
                    {
                        SenderId = model.SenderId,
                        ReceiverId = receiverId,
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
                // ==============================
                // 8. Save leave
                // ==============================
                await _context.LeaveForm.AddRangeAsync(leaveList);
                await _context.SaveChangesAsync();
                // ==============================
                // 9. Mark compensation used
                // ==============================
                if (matchedExtraWork != null &&
                    compensationLeaveRow != null)
                {
                    compensationLeaveRow.CompensationExtraWorkId =
                        matchedExtraWork.Id;
                    matchedExtraWork.IsCompensationUsed = true;
                    await _context.SaveChangesAsync();
                }
                // ==============================
                // 10. Send notification
                // ==============================
                var receiver = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == receiverId);
                if (receiver != null &&
                    !string.IsNullOrWhiteSpace(receiver.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            receiver.FcmToken,
                            "Leave Request",
                            $"You received a leave request from {model.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FCM Error: {ex.Message}");
                    }
                }
                // ==============================
                // 11. Response
                // ==============================
                return Ok(new
                {
                    message = "Leave applied (split per day)",
                    receiverId = receiverId,
                    data = leaveList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }


        [Authorize]
        [HttpGet("leave-list")]
        public async Task<IActionResult> GetLeaveList()
        {
            try
            {

                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int loggedInUserId))
                    return Unauthorized("Invalid User ID.");
                var currentUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == loggedInUserId);

                if (currentUser == null)
                    return Unauthorized("User not found.");

                var leaves = await _context.LeaveForm
                    .Where(x => x.ReceiverId == loggedInUserId)
                    .OrderByDescending(x => x.SubmittedDate)
                    .ToListAsync();

                var senderIds = leaves
                    .Select(x => x.SenderId)
                    .Distinct()
                    .ToList();

                var users = await _context.Users
                    .Where(u => senderIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId);

                var result = leaves.Select(leave =>
                {
                    users.TryGetValue(leave.SenderId, out var sender);

                    return new
                    {
                        leave.Id,
                        leave.SenderId,
                        SenderName = sender?.Name ?? leave.Name,
                        SenderDepartment = sender?.Department,
                        leave.ReceiverId,
                        leave.Designation,
                        leave.Reason,
                        leave.FromDate,
                        leave.ToDate,
                        leave.LeaveTyp,
                        leave.LeaveType,
                        leave.TotalDays,
                        leave.ContactNumber,
                        leave.Status,
                        leave.SubmittedDate,
                        leave.ApprovedDate,
                        leave.RejectionReason,
                        leave.CompensationExtraWorkId
                    };
                }).ToList();

                return Ok(new
                {
                    message = "Leave list retrieved successfully.",
                    userId = loggedInUserId,
                    count = result.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error retrieving leave list.",
                    error = ex.Message
                });
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

            if (roleClaim != "3" && roleClaim != "1")
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
                    e => e.WorkDate
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
                var employee = await _context.Users
            .FirstOrDefaultAsync(u => u.UserId == leave.SenderId);

                if (employee != null &&
                    !string.IsNullOrWhiteSpace(employee.FcmToken))
                {
                    try
                    {
                       

                        string title = status == "Approved"
                            ? "Leave Approved"
                            : "Leave Rejected";

                        string messagee = status == "Approved"
                            ? $"Your leave request has been approved by {managerName}"
                            : $"Your leave request has been rejected by {managerName}";

                        await _firebaseNotificationService.SendNotificationAsync(
                            employee.FcmToken,
                            title,
                            message
                        );

                        Console.WriteLine(
                            $"Leave status notification sent to employee {employee.UserId}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FCM Error: {ex}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"Employee {leave.SenderId} does not have an FCM token."
                    );
                }

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


        [HttpGet("available-compensation/{userId}")]
        public async Task<IActionResult> GetAvailableCompensation(int userId)
        {
            var list = await _context.ExtraWork
                .Where(e => e.StaffId == userId
                         && e.Status == "Approved"
                         && !e.IsCompensationUsed) 
                .OrderBy(e => e.WorkDate)
                .Select(e => new
                {
                    e.Id,
                    e.WorkDate 
                })
                .ToListAsync();

            return Ok(list);
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

            if (roleClaim != "1" && roleClaim != "3")
                return Forbid("Access denied");

            int managerId = int.Parse(managerIdClaim);

            // Get manager
            var manager = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == managerId);

            if (manager == null)
                return NotFound("Manager not found");
            string managerName = manager?.Name ?? "Manager";
            // Get extra work
            var extraWork = await _context.ExtraWork
                .FirstOrDefaultAsync(e => e.Id == id);

            if (extraWork == null)
                return NotFound("Extra work not found");

            // Get employee
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == extraWork.StaffId);

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
            extraWork.VerifiedBy = managerId;

            await _context.SaveChangesAsync();


            if (employee != null &&
                !string.IsNullOrWhiteSpace(employee.FcmToken))
            {
                try
                {


                    if (manager != null)
                        managerName = manager.Name ?? "Manager";

                    string title = extraWork.Status == "Approved"
                        ? "Compensation Approved"
                        : "Compensation Rejected";

                    string message = extraWork.Status == "Approved"
                        ? $"Your Compensation request has been approved by {managerName}"
                        : $"Your Compensation request has been rejected by {managerName}";

                    await _firebaseNotificationService.SendNotificationAsync(
                        employee.FcmToken,
                        title,
                        message
                    );

                    Console.WriteLine(
                        $"Leave status notification sent to employee {employee.UserId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FCM Error: {ex}");
                }
            }

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
                // =====================================================
                // 1. GET LOGGED-IN USER FROM JWT
                // =====================================================

                var userIdClaim = User.FindFirst("UserId")?.Value;
                var roleClaim = User.FindFirst("Role")?.Value;

                if (string.IsNullOrWhiteSpace(userIdClaim))
                    return Unauthorized("Invalid token.");

                if (!int.TryParse(userIdClaim, out int loggedInUserId))
                    return Unauthorized("Invalid User ID.");

                if (string.IsNullOrWhiteSpace(roleClaim))
                    return Unauthorized("Role not found in token.");

                string loggedInRole = roleClaim;

                // =====================================================
                // 2. GET SENDER
                // =====================================================
                // IMPORTANT:
                // Use JWT UserId, not model.SenderId
                // =====================================================

                var sender = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == loggedInUserId);

                if (sender == null)
                    return NotFound("Sender not found.");

                string senderRole = sender.Role;

                // =====================================================
                // 3. VALIDATE TIME
                // =====================================================

                if (model.ToTime <= model.FromTime)
                {
                    return BadRequest(new
                    {
                        message =
                            "Invalid time range. ToTime must be greater than FromTime."
                    });
                }

                int requestedMinutes =
                    (int)(model.ToTime - model.FromTime).TotalMinutes;

                if (requestedMinutes <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Invalid permission duration."
                    });
                }

                // =====================================================
                // 4. DETERMINE RECEIVER
                // =====================================================

                int receiverId;

                var senderDepartment = await _context.Departments
                    .FirstOrDefaultAsync(d =>
                        d.DepartmentName == sender.Department);

                if (senderDepartment == null)
                {
                    return BadRequest(
                        $"Department '{sender.Department}' not found."
                    );
                }

                // =====================================================
                // ROLE 2 = DIVISION HEAD
                // Division Head -> Director
                // =====================================================

                if (senderRole == "2")
                {
                    var director = await _context.Users
                        .FirstOrDefaultAsync(u => u.Role == "1");

                    if (director == null)
                        return BadRequest("Director not found.");

                    receiverId = director.UserId;
                }

                // =====================================================
                // ROLE 3 = MANAGER
                // =====================================================

                else if (senderRole == "3")
                {
                    // Check whether this manager's department
                    // is assigned under a Division Head.
                    var divisionHeadAccess =
                        await _context.DepartmentAccess
                            .FirstOrDefaultAsync(x =>
                                x.SubDepartmentId == senderDepartment.Id);

                    // -------------------------------------------------
                    // Manager under Division Head
                    // -> Division Head
                    // -------------------------------------------------

                    if (divisionHeadAccess != null)
                    {
                        var divisionHead = await _context.Users
                            .FirstOrDefaultAsync(u =>
                                u.UserId == divisionHeadAccess.UserId &&
                                u.Role == "2");

                        if (divisionHead == null)
                        {
                            return BadRequest(
                                "Division Head assigned to this department was not found."
                            );
                        }

                        receiverId = divisionHead.UserId;
                    }

                    // -------------------------------------------------
                    // Manager not under Division Head
                    // -> Director
                    // -------------------------------------------------

                    else
                    {
                        var director = await _context.Users
                            .FirstOrDefaultAsync(u => u.Role == "1");

                        if (director == null)
                            return BadRequest("Director not found.");

                        receiverId = director.UserId;
                    }
                }

                // =====================================================
                // STAFF / OTHER ROLES
                // -> MANAGER OF THEIR OWN DEPARTMENT
                // =====================================================

                else
                {
                    var manager = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.Department == sender.Department &&
                            u.Role == "3");

                    if (manager == null)
                    {
                        return BadRequest(
                            "Manager not found for this department."
                        );
                    }

                    receiverId = manager.UserId;
                }

                // =====================================================
                // 5. MONTH CALCULATION
                // =====================================================

                var monthStart = new DateTime(
                    model.Date.Year,
                    model.Date.Month,
                    1
                );

                var nextMonth = monthStart.AddMonths(1);

                // =====================================================
                // 6. CALCULATE EXISTING PERMISSION
                // =====================================================

                var usedMinutesDecimal =
                    await _context.PermissionForm
                        .Where(p =>
                            p.SenderId == loggedInUserId &&
                            p.Date >= monthStart &&
                            p.Date < nextMonth &&
                            p.Status != "Rejected")
                        .SumAsync(p =>
                            (decimal?)p.TotalHours * 60m) ?? 0m;

                int existingPermissionMinutes =
                    (int)Math.Round(usedMinutesDecimal);

                int totalAfterRequest =
                    existingPermissionMinutes + requestedMinutes;

                // =====================================================
                // 7. PERMISSION RULES
                // =====================================================

                const int freePermissionMinutes = 60;
                const int halfDayBlockMinutes = 240;

                int requiredHalfDays = 0;

                if (totalAfterRequest > freePermissionMinutes)
                {
                    int excessMinutes =
                        totalAfterRequest - freePermissionMinutes;

                    requiredHalfDays =
                        (int)Math.Ceiling(
                            (double)excessMinutes /
                            halfDayBlockMinutes
                        );
                }

                // =====================================================
                // 8. EXISTING PERMISSION-EXCEEDED LEAVES
                // =====================================================

                int existingHalfDayLeaves =
                    await _context.LeaveForm
                        .Where(l =>
                            l.SenderId == loggedInUserId &&
                            l.FromDate >= monthStart &&
                            l.FromDate < nextMonth &&
                            l.ApplicationSource == "PermissionExceeded" &&
                            l.LeaveType == "First Half" &&
                            l.Status != "Rejected")
                        .CountAsync();

                bool createHalfDay =
                    requiredHalfDays > existingHalfDayLeaves;

                // =====================================================
                // 9. REQUESTED HOURS
                // =====================================================

                decimal requestedHours = Math.Round(
                    (decimal)requestedMinutes / 60m,
                    2
                );

                // =====================================================
                // 10. CREATE PERMISSION
                // =====================================================

                var permission = new PermissionForm
                {
                    // IMPORTANT
                    SenderId = loggedInUserId,

                    // Receiver determined above
                    ReceiverId = receiverId,

                    Name = model.Name,
                    Designation = model.Designation,
                    Reason = model.Reason,

                    Date = model.Date,

                    FromTime = model.FromTime,
                    ToTime = model.ToTime,

                    TotalHours = requestedHours,

                    Status = "Pending",

                    SubmittedDate = DateTime.Now
                };

                _context.PermissionForm.Add(permission);

                // =====================================================
                // 11. CREATE HALF DAY LOP IF REQUIRED
                // =====================================================

                LeaveForm? leave = null;

                if (createHalfDay)
                {
                    leave = new LeaveForm
                    {
                        SenderId = loggedInUserId,

                        // Same receiver as permission
                        ReceiverId = receiverId,

                        Name = model.Name,
                        Designation = model.Designation,
                        Reason = model.Reason,

                        FromDate = model.Date,
                        ToDate = model.Date,

                        LeaveType = "First Half",

                        TotalDays = 0.5m,

                        LeaveTyp = "LOP",

                        ApplicationSource = "PermissionExceeded",

                        Status = "Pending",

                        SubmittedDate = DateTime.Now,

                        ApprovedDate = null,
                        RejectionReason = null,

                        ContactNumber = null
                    };

                    _context.LeaveForm.Add(leave);
                }

                // =====================================================
                // 12. SAVE
                // =====================================================

                await _context.SaveChangesAsync();

                // =====================================================
                // 13. GET RECEIVER
                // =====================================================

                var receiver = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.UserId == receiverId);

                // =====================================================
                // 14. SEND NOTIFICATION
                // =====================================================

                if (receiver != null &&
                    !string.IsNullOrWhiteSpace(receiver.FcmToken))
                {
                    try
                    {
                        if (createHalfDay)
                        {
                            await _firebaseNotificationService
                                .SendNotificationAsync(
                                    receiver.FcmToken,
                                    "Permission Request",
                                    $"You received a permission request and Half Day LOP request from {model.Name}."
                                );
                        }
                        else
                        {
                            await _firebaseNotificationService
                                .SendNotificationAsync(
                                    receiver.FcmToken,
                                    "Permission Request",
                                    $"You received a permission request from {model.Name}."
                                );
                        }
                    }
                    catch (Exception ex)
                    {
                        // Notification failure should NOT fail request
                        Console.WriteLine(
                            $"FCM Error: {ex.Message}"
                        );
                    }
                }

                // =====================================================
                // 15. RESPONSE
                // =====================================================

                return Ok(new
                {
                    message = createHalfDay
                        ? "Permission applied successfully and Half Day LOP leave request created."
                        : "Permission applied successfully.",

                    applicationType = createHalfDay
                        ? "Permission + Leave"
                        : "Permission",

                    senderId = loggedInUserId,

                    receiverId = receiverId,

                    requestedMinutes = requestedMinutes,

                    requestedHours = requestedHours,

                    previousPermissionMinutes =
                        existingPermissionMinutes,

                    previousPermissionHours =
                        Math.Round(
                            (decimal)existingPermissionMinutes / 60m,
                            2
                        ),

                    totalPermissionMinutes =
                        totalAfterRequest,

                    totalPermissionHours =
                        Math.Round(
                            (decimal)totalAfterRequest / 60m,
                            2
                        ),

                    freePermissionMinutes =
                        freePermissionMinutes,

                    halfDayBlockMinutes =
                        halfDayBlockMinutes,

                    requiredHalfDays =
                        requiredHalfDays,

                    existingHalfDayLeaves =
                        existingHalfDayLeaves,

                    newHalfDayCreated =
                        createHalfDay,

                    data = new
                    {
                        permission,
                        leave
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"ApplyPermission Error: {ex}"
                );

                return StatusCode(500, new
                {
                    message = "Failed to apply permission.",
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }



        [Authorize]
        [HttpGet("my-received-permissions")]
        public async Task<IActionResult> GetMyReceivedPermissions()
        {
            try
            {
                // ============================================
                // 1. Get logged-in user ID from JWT
                // ============================================

                var userIdClaim = User.FindFirst("UserId")?.Value;

                if (string.IsNullOrWhiteSpace(userIdClaim))
                {
                    return Unauthorized("Invalid token.");
                }

                if (!int.TryParse(userIdClaim, out int receiverId))
                {
                    return Unauthorized("Invalid User ID.");
                }

                // ============================================
                // 2. Get permissions where
                //    logged-in user is Receiver
                // ============================================

                var permissions = await _context.PermissionForm
                    .Where(p => p.ReceiverId == receiverId)
                    .OrderByDescending(p => p.SubmittedDate)
                    .Select(p => new
                    {
                        p.Id,

                        p.SenderId,
                        p.ReceiverId,

                        p.Name,
                        p.Designation,
                        p.Reason,

                        p.Date,
                        p.FromTime,
                        p.ToTime,

                        p.TotalHours,

                        p.Status,
                        p.SubmittedDate
                    })
                    .ToListAsync();

                // ============================================
                // 3. Return response
                // ============================================

                return Ok(new
                {
                    message = "Received permission list fetched successfully.",
                    receiverId = receiverId,
                    count = permissions.Count,
                    permissions = permissions
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"GetMyReceivedPermissions Error: {ex}"
                );

                return StatusCode(500, new
                {
                    message = "Failed to fetch received permission list.",
                    error = ex.Message,
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

            if (roleClaim != "3" && roleClaim != "1")
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

                var employee = await _context.Users
         .FirstOrDefaultAsync(u => u.UserId == permission.SenderId);

                if (employee != null &&
                    !string.IsNullOrWhiteSpace(employee.FcmToken))
                {
                    try
                    {


                        if (manager != null)
                            managerName = manager.Name ?? "Manager";

                        string title = status == "Approved"
                            ? "Permission Approved"
                            : "Permission Rejected";

                      

                        await _firebaseNotificationService.SendNotificationAsync(
                            employee.FcmToken,
                            title,
                            message
                        );

                        Console.WriteLine(
                            $"Permission status notification sent to employee {employee.UserId}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FCM Error: {ex}");
                    }
                }
              

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


        [Authorize]
        [HttpPost("punch-correction")]
        public async Task<IActionResult> CreatePunchCorrection([FromBody] PunchCorrectionDto dto)
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

            var manager = await (
      from u in _context.Users
      join r in _context.Roles
          on u.Role equals r.Id.ToString()
      where u.Department == user.Department
            && r.RoleName == "Manager"
            && r.Status == true
      select u
  ).FirstOrDefaultAsync();

            // =====================================================
            // SEND NOTIFICATION TO MANAGER
            // =====================================================

            if (manager != null &&
                !string.IsNullOrWhiteSpace(manager.FcmToken))
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        manager.FcmToken,
                        "Punch Correction Request",
                        $"You received a punch correction request from {user.Name}"
                    );

                    Console.WriteLine(
                        $"Punch correction notification sent to manager {manager.UserId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FCM Error: {ex}");
                }
            }
            else
            {
                Console.WriteLine(
                    $"Manager not found or FCM token is empty for department {user.Department}"
                );
            }

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

            var employee = await _context.Users
.FirstOrDefaultAsync(u => u.UserId == correction.UserId);

            if (employee != null &&
                !string.IsNullOrWhiteSpace(employee.FcmToken))
            {
                try
                {

                    string title = correction.Status == "Approved"
                        ? "punch correction Approved"
                        : "punch correction Rejected";

                    string message = correction.Status == "Approved"
                        ? $"Your punch correction request has been approved by Manager"
                        : $"Your punch correction request has been rejected by Manager";

                    await _firebaseNotificationService.SendNotificationAsync(
                        employee.FcmToken,
                        title,
                        message
                    );

                    Console.WriteLine(
                        $"Leave status notification sent to employee {employee.UserId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FCM Error: {ex}");
                }
            }
          
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
            if (loggedInUser.Role != "3")
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
        public async Task<IActionResult> AddAttitudeBehaviourScore([FromBody] AttitudeBehaviourScoreDto model)
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
                // =========================================================
                // 1. GET LOGGED-IN USER
                // =========================================================

                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int userId))
                    return Unauthorized("Invalid User ID.");

                var loggedInUser = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (loggedInUser == null)
                    return NotFound("Logged-in user not found.");

                var role = loggedInUser.Role;

                // =========================================================
                // 2. USERS TO SHOW
                // =========================================================

                List<User> usersToShow = new List<User>();

                // =========================================================
                // ROLE 1 = DIRECTOR
                // =========================================================
                // Director can see:
                // Role 2 = Division Head
                // Role 3 = Manager
                // =========================================================

                if (role == "1")
                {
                    usersToShow = await _context.Users
                        .AsNoTracking()
                        .Where(u =>
                            u.Role == "2" ||
                            u.Role == "3")
                        .OrderBy(u => u.Name)
                        .ToListAsync();
                }

                // =========================================================
                // ROLE 2 = DIVISION HEAD
                // =========================================================
                // Get departments from DepartmentAccess
                //
                // DepartmentAccess:
                // UserId          = Division Head
                // HeadDepartmentId
                // SubDepartmentId
                //
                // Division Head can see Managers belonging to the
                // departments assigned through DepartmentAccess.
                // =========================================================

                else if (role == "2")
                {
                    var departmentAccess = await _context.DepartmentAccess
                        .AsNoTracking()
                        .Where(x => x.UserId == userId)
                        .ToListAsync();

                    if (!departmentAccess.Any())
                    {
                        return Ok(new
                        {
                            requestedBy = new
                            {
                                userId = loggedInUser.UserId,
                                name = loggedInUser.Name,
                                role = loggedInUser.Role,
                                department = loggedInUser.Department
                            },

                            count = 0,

                            scores = new List<object>()
                        });
                    }

                    // -----------------------------------------------------
                    // Get all department IDs accessible to this Division Head
                    // -----------------------------------------------------

                    var accessibleDepartmentIds =
                        departmentAccess
                            .SelectMany(x => new[]
                            {
                        x.HeadDepartmentId,
                        x.SubDepartmentId
                            })
                            .Where(x => x.HasValue)
                            .Select(x => x!.Value)
                            .Distinct()
                            .ToList();

                    // -----------------------------------------------------
                    // Get department names
                    // -----------------------------------------------------

                    var accessibleDepartments = await _context.Departments
                        .AsNoTracking()
                        .Where(d =>
                            accessibleDepartmentIds.Contains(d.Id))
                        .Select(d => d.DepartmentName)
                        .ToListAsync();

                    // -----------------------------------------------------
                    // Get Managers from those departments
                    // -----------------------------------------------------

                    usersToShow = await _context.Users
                        .AsNoTracking()
                        .Where(u =>
                            u.Role == "3" &&
                            u.Department != null &&
                            accessibleDepartments.Contains(u.Department))
                        .OrderBy(u => u.Department)
                        .ThenBy(u => u.Name)
                        .ToListAsync();
                }

                // =========================================================
                // ROLE 3 = MANAGER
                // =========================================================
                // Manager can see staff belonging to his/her department.
                // =========================================================

                else if (role == "3")
                {
                    if (string.IsNullOrWhiteSpace(loggedInUser.Department))
                    {
                        return BadRequest(
                            "Manager department not found.");
                    }

                    usersToShow = await _context.Users
                        .AsNoTracking()
                        .Where(u =>
                            u.Department == loggedInUser.Department &&
                            u.Role != "1" &&
                            u.Role != "2" &&
                            u.Role != "3")
                        .OrderBy(u => u.Name)
                        .ToListAsync();
                }

                // =========================================================
                // OTHER ROLES
                // =========================================================

                else
                {
                    return Forbid();
                }

                // =========================================================
                // 3. GET ATTITUDE & BEHAVIOUR SCORES
                // =========================================================

                var userIds = usersToShow
                    .Select(u => u.UserId)
                    .ToList();

                var scores = await (
                    from score in _context.AttitudeBehaviourScore.AsNoTracking()
                    join user in _context.Users.AsNoTracking()
                        on score.StaffId equals user.UserId
                    where userIds.Contains(user.UserId)
                    orderby user.Name, score.Date descending
                    select new
                    {
                        score.Id,

                        StaffId = user.UserId,
                        StaffName = user.Name,

                        Department = user.Department,
                        Role = user.Role,

                        Communication = score.Communication,
                        Punctuality = score.Punctuality,
                        Integrity = score.Integrity,
                        Total = score.Total,

                        Date = score.Date
                    }
                ).ToListAsync();

                // =========================================================
                // 4. RESPONSE
                // =========================================================

                return Ok(new
                {
                    requestedBy = new
                    {
                        userId = loggedInUser.UserId,
                        name = loggedInUser.Name,
                        role = loggedInUser.Role,
                        department = loggedInUser.Department
                    },

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
                        message =
                            "Error while getting hierarchical attitude & behaviour scores.",

                        error = ex.Message,

                        innerError =
                            ex.InnerException?.Message
                    });
            }
        }

        //[Authorize]
        //[HttpGet("department-attitude-behaviour-scores")]
        //public async Task<IActionResult> GetDepartmentAttitudeBehaviourScores()
        //{
        //    try
        //    {
        //        // Get logged-in user ID from JWT
        //        var userIdClaim = User.FindFirst("UserId");

        //        if (userIdClaim == null)
        //            return Unauthorized("User ID not found in token.");

        //        if (!int.TryParse(userIdClaim.Value, out int userId))
        //            return Unauthorized("Invalid User ID.");

        //        // Get logged-in user
        //        var loggedInUser = await _context.Users
        //            .FirstOrDefaultAsync(u => u.UserId == userId);

        //        if (loggedInUser == null)
        //            return NotFound("Logged-in user not found.");

        //        if (string.IsNullOrWhiteSpace(loggedInUser.Department))
        //            return BadRequest("User department not found.");

        //        // Get scores of users in the same department
        //        var scores = await (
        //            from score in _context.AttitudeBehaviourScore
        //            join user in _context.Users
        //                on score.StaffId equals user.UserId
        //            where user.Department == loggedInUser.Department
        //            orderby user.Name
        //            select new
        //            {
        //                score.Id,
        //                StaffId = user.UserId,
        //                StaffName = user.Name,
        //                Department = user.Department,

        //                score.Communication,
        //                score.Punctuality,
        //                score.Integrity,
        //                score.Total,
        //                score.Date
        //            }
        //        ).ToListAsync();

        //        return Ok(new
        //        {
        //            department = loggedInUser.Department,
        //            count = scores.Count,
        //            scores = scores
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(
        //            500,
        //            new
        //            {
        //                message = "Error while getting department attitude & behaviour scores.",
        //                error = ex.Message
        //            }
        //        );
        //    }
        //}


        //..............................................................................................




        //[Authorize]
        //[HttpGet("department-attitude-behaviour-scores")]
        //public async Task<IActionResult> GetDepartmentAttitudeBehaviourScores()
        //{
        //    try
        //    {
        //        // =========================================================
        //        // 1. GET LOGGED-IN USER
        //        // =========================================================

        //        var userIdClaim = User.FindFirst("UserId");

        //        if (userIdClaim == null)
        //            return Unauthorized("User ID not found in token.");

        //        if (!int.TryParse(userIdClaim.Value, out int userId))
        //            return Unauthorized("Invalid User ID.");

        //        var loggedInUser = await _context.Users
        //            .FirstOrDefaultAsync(u => u.UserId == userId);

        //        if (loggedInUser == null)
        //            return NotFound("Logged-in user not found.");

        //        var role = loggedInUser.Role;

        //        // =========================================================
        //        // 2. DIRECTOR
        //        // Role 1 = Director
        //        // See ALL Division Heads + Managers
        //        // =========================================================

        //        IQueryable<User> usersQuery = _context.Users;

        //        if (role == "1")
        //        {
        //            // Director can see:
        //            // Role 2 = Division Head
        //            // Role 3 = Manager

        //            usersQuery = usersQuery
        //                .Where(u => u.Role == "2" || u.Role == "3");
        //        }

        //        // =========================================================
        //        // 3. DIVISION HEAD
        //        // Role 2
        //        // See Managers in same department
        //        // =========================================================

        //        else if (role == "2")
        //        {
        //            if (string.IsNullOrWhiteSpace(loggedInUser.Department))
        //                return BadRequest("User department not found.");

        //            usersQuery = usersQuery
        //                .Where(u =>
        //                    u.Department == loggedInUser.Department &&
        //                    u.Role == "3");
        //        }

        //        // =========================================================
        //        // 4. MANAGER
        //        // Role 3
        //        // See users in same department
        //        // =========================================================

        //        else if (role == "3")
        //        {
        //            if (string.IsNullOrWhiteSpace(loggedInUser.Department))
        //                return BadRequest("User department not found.");

        //            usersQuery = usersQuery
        //                .Where(u =>
        //                    u.Department == loggedInUser.Department);
        //        }

        //        // =========================================================
        //        // 5. OTHER ROLES
        //        // =========================================================

        //        else
        //        {
        //            return Forbid();
        //        }

        //        // =========================================================
        //        // 6. GET ATTITUDE & BEHAVIOUR SCORES
        //        // =========================================================

        //        var scores = await (
        //            from score in _context.AttitudeBehaviourScore
        //            join user in usersQuery
        //                on score.StaffId equals user.UserId

        //            orderby user.Name, score.Date descending

        //            select new
        //            {
        //                score.Id,

        //                StaffId = user.UserId,
        //                StaffName = user.Name,

        //                Department = user.Department,
        //                Role = user.Role,

        //                Communication = score.Communication,
        //                Punctuality = score.Punctuality,
        //                Integrity = score.Integrity,
        //                Total = score.Total,

        //                Date = score.Date
        //            }
        //        ).ToListAsync();

        //        // =========================================================
        //        // 7. RESPONSE
        //        // =========================================================

        //        return Ok(new
        //        {
        //            requestedBy = new
        //            {
        //                userId = loggedInUser.UserId,
        //                name = loggedInUser.Name,
        //                role = loggedInUser.Role,
        //                department = loggedInUser.Department
        //            },

        //            count = scores.Count,

        //            scores = scores
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(
        //            500,
        //            new
        //            {
        //                message = "Error while getting department attitude & behaviour scores.",
        //                error = ex.Message
        //            }
        //        );
        //    }
        //}


        [Authorize]
       [HttpPost("create_overtime")]
       public async Task<IActionResult> CreateOvertime(CreateOvertimeDto dto)
        {
            // Get UserId from JWT token
            var managerIdClaim = User.FindFirst("UserId");

            if (managerIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(managerIdClaim.Value, out int managerId))
                return Unauthorized("Invalid User ID.");

            // Validate overtime time
            if (dto.ToTime <= dto.FromTime)
                return BadRequest("To time must be greater than From time.");

            // Calculate total overtime hours
            decimal totalHours =
                (decimal)(dto.ToTime - dto.FromTime).TotalHours;

            var overtime = new OverTime
            {
                // Staff receiving overtime request
                Uid = dto.Uid,

                // Staff department
                Dept = dto.Dept,

                Date = dto.Date.Date,

                FromTime = dto.FromTime,
                ToTime = dto.ToTime,

                TotalHours = totalHours,

                Reason = dto.Reason,

                // Staff must accept/reject first
                StaffStatus = "Pending",

                // Manager approval comes after staff acceptance
                ManagerStatus = "Pending",

                StaffResponseReason = null,
                ManagerResponseReason = null,

                // Manager who created the request
                RequestedBy = managerId,

                Approved_by = null
            };

            _context.OverTime.Add(overtime);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Overtime request sent successfully",
                overtimeId = overtime.Id
            });
        }


        [HttpPut("{id}/staff-response")]
        public async Task<IActionResult> StaffResponse(int id,StaffOvertimeResponseDto dto)
        {
            var uidClaim = User.FindFirst("UserId");

            if (uidClaim == null)
                return Unauthorized("User ID not found in token.");

            int uid = int.Parse(uidClaim.Value);

            var overtime = await _context.OverTime
                .FirstOrDefaultAsync(x => x.Id == id && x.Uid == uid);

            if (overtime == null)
                return NotFound("Overtime request not found.");

            if (overtime.StaffStatus != "Pending")
                return BadRequest("You have already responded to this overtime request.");

            if (dto.Status != "Accepted" &&
                dto.Status != "Rejected")
            {
                return BadRequest("Status must be Accepted or Rejected.");
            }

            if (dto.Status == "Rejected" &&
                string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest("Reason is required when rejecting overtime.");
            }

            overtime.StaffStatus = dto.Status;
            overtime.StaffResponseReason =
                dto.Status == "Rejected" ? dto.Reason : null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Overtime request {dto.Status.ToLower()} successfully"
            });
        }


        [HttpPut("{id}/manager-response")]
        public async Task<IActionResult> ManagerResponse(int id,ManagerOvertimeResponseDto dto)
        {
            var managerIdClaim = User.FindFirst("UserId");

            if (managerIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(managerIdClaim.Value, out int managerId))
                return Unauthorized("Invalid User ID in token.");

            var overtime = await _context.OverTime
                .FirstOrDefaultAsync(x => x.Id == id);

            if (overtime == null)
                return NotFound("Overtime request not found.");

            if (overtime.RequestedBy != managerId)
                return Forbid();

            if (overtime.StaffStatus != "Accepted")
                return BadRequest(
                    "Manager can respond only after staff accepts the overtime request.");

            if (overtime.ManagerStatus != "Pending")
                return BadRequest("Manager has already responded.");

            if (dto.Status != "Approved" &&
                dto.Status != "Rejected")
            {
                return BadRequest("Status must be Approved or Rejected.");
            }

            if (dto.Status == "Rejected" &&
                string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(
                    "Reason is required when rejecting overtime.");
            }

            overtime.ManagerStatus = dto.Status;

            overtime.ManagerResponseReason =
                dto.Status == "Rejected" ? dto.Reason : null;

            if (dto.Status == "Approved")
            {
                overtime.Approved_by = managerId;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Overtime request {dto.Status.ToLower()} successfully"
            });
        }


        [Authorize]
        [HttpGet("department")]
        public async Task<IActionResult> GetDepartmentOvertime()
        {
            var managerIdClaim = User.FindFirst("UserId");

            if (managerIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(managerIdClaim.Value, out int managerId))
                return Unauthorized("Invalid User ID.");

            var manager = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerId);

            if (manager == null)
                return NotFound("Manager not found.");

            var overtime = await (
                from ot in _context.OverTime
                join staff in _context.Users
                    on ot.Uid equals staff.UserId
                where ot.Dept == manager.Department
                orderby ot.Date descending
                select new
                {
                    ot.Id,
                    ot.Uid,

                    // Staff details
                    StaffName = staff.Name, // change Name if your column is DisplayName/UserName etc.

                    ot.Dept,
                    ot.Date,
                    ot.FromTime,
                    ot.ToTime,
                    ot.TotalHours,
                    ot.Reason,

                    ot.StaffStatus,
                    ot.StaffResponseReason,

                    ot.ManagerStatus,
                    ot.ManagerResponseReason,

                    ot.RequestedBy,
                    ot.Approved_by
                }
            ).ToListAsync();

            return Ok(overtime);
        }

        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyOvertime()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim.Value, out int uid))
                return Unauthorized("Invalid User ID.");

            var overtime = await _context.OverTime
                .Where(x => x.Uid == uid)
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,

                    x.Uid,

                    StaffName = _context.Users
                        .Where(u => u.UserId == x.Uid)
                        .Select(u => u.Name)
                        .FirstOrDefault(),

                    x.Dept,
                    x.Date,
                    x.FromTime,
                    x.ToTime,
                    x.TotalHours,
                    x.Reason,

                    x.StaffStatus,
                    x.StaffResponseReason,

                    x.ManagerStatus,
                    x.ManagerResponseReason,

                    x.RequestedBy,
                    x.Approved_by
                })
                .ToListAsync();

            return Ok(overtime);
        }

        [Authorize]
        [HttpPost("create-compensation")]
        public async Task<IActionResult> CreateExtraWork(
       [FromBody] CreateExtraWorkRequest request)
        {
            try
            {
                // Get logged-in manager ID from JWT
                var managerIdClaim =User.FindFirst("UserId");

                if (managerIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(managerIdClaim.Value, out int managerId))
                    return Unauthorized("Invalid User ID.");


                var manager = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == managerId);

                if (manager == null)
                {
                    return NotFound(new
                    {
                        message = "Manager not found."
                    });
                }

                if (request.WorkDate.Date < DateTime.Today)
                {
                    return BadRequest(new
                    {
                        message = "Work date cannot be in the past."
                    });
                }

                if (request.EndTime <= request.StartTime)
                {
                    return BadRequest(new
                    {
                        message = "End time must be after start time."
                    });
                }

                if (request.ExpectedHours <= 0)
                {
                    return BadRequest(new
                    {
                        message = "Expected hours must be greater than zero."
                    });
                }

                var extraWork = new ExtraWork
                {
                    ManagerId = managerId,

                    StaffId = request.StaffId,

                    TaskId = request.TaskId,

                    WorkType = request.WorkType,

                    WorkDate = request.WorkDate.Date,

                    ExpectedHours = request.ExpectedHours,

                    StartTime = request.StartTime,

                    EndTime = request.EndTime,

                    Reason = request.Reason,

                    Status = "Pending",

                    StaffRemarks = null,

                    ManagerRemarks = null,

                    VerifiedBy = null,

                    IsCompensationUsed = false
                };

                _context.ExtraWork.Add(extraWork);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Extra work request created successfully.",

                    extraWork = new
                    {
                        extraWork.Id,
                        extraWork.ManagerId,
                        extraWork.StaffId,
                        extraWork.TaskId,
                        extraWork.WorkType,
                        extraWork.WorkDate,
                        extraWork.ExpectedHours,
                        extraWork.StartTime,
                        extraWork.EndTime,
                        extraWork.Reason,
                        extraWork.Status,
                        extraWork.IsCompensationUsed
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to create extra work request.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpGet("my-extra-work")]
        public async Task<IActionResult> GetMyExtraWork()
        {
            try
            {
                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int userId))
                    return Unauthorized("Invalid User ID.");

                var extraWorks = await _context.ExtraWork
                    .Where(e => e.StaffId == userId)
                    .OrderByDescending(e => e.WorkDate)
                    .ThenByDescending(e => e.Id)
                    .Select(e => new
                    {
                        e.Id,
                        e.ManagerId,
                        e.StaffId,
                        e.TaskId,
                        e.WorkType,
                        e.WorkDate,
                        e.ExpectedHours,
                        e.StartTime,
                        e.EndTime,
                        e.Reason,
                        e.Status,
                        e.StaffRemarks,
                        e.ManagerRemarks,
                        e.VerifiedBy,
                        e.IsCompensationUsed,

                        StaffName = _context.Users
                            .Where(u => u.UserId == e.StaffId)
                            .Select(u => u.Name)
                            .FirstOrDefault(),

                        ManagerName = _context.Users
                            .Where(u => u.UserId == e.ManagerId)
                            .Select(u => u.Name)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Ok(extraWorks);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to get your extra work.",
                    error = ex.Message
                });
            }
        }
      
       [Authorize]
       [HttpGet("department-extra-work")]
       public async Task<IActionResult> GetDepartmentExtraWork()
        {
            try
            {
                // 1. Get authorized user ID from JWT
                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int userId))
                    return Unauthorized("Invalid User ID.");

                // 2. Get logged-in user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    return NotFound(new
                    {
                        message = "User not found."
                    });
                }

                // 3. Get manager department
                var department = user.Department;

                if (string.IsNullOrWhiteSpace(department))
                {
                    return BadRequest(new
                    {
                        message = "Manager department not found."
                    });
                }

                // 4. Get all staff in manager's department
                var staffIds = await _context.Users
                    .Where(u => u.Department == department)
                    .Select(u => u.UserId)
                    .ToListAsync();

                // 5. Get all ExtraWork belonging to those staff
                var extraWorks = await _context.ExtraWork
                    .Where(e => staffIds.Contains(e.StaffId))
                    .OrderByDescending(e => e.WorkDate)
                    .ThenByDescending(e => e.Id)
                    .Select(e => new
                    {
                        e.Id,
                        e.ManagerId,
                        e.StaffId,

                        // Original TaskId
                        e.TaskId,

                        // Get Task Name from Task table
                        TaskName = _context.Tasks
                            .Where(t => t.TaskCode == e.TaskId)
                            .Select(t => t.Task)
                            .FirstOrDefault(),

                        e.WorkType,
                        e.WorkDate,
                        e.ExpectedHours,
                        e.StartTime,
                        e.EndTime,
                        e.Reason,
                        e.Status,
                        e.StaffRemarks,
                        e.ManagerRemarks,
                        e.VerifiedBy,
                        e.IsCompensationUsed,

                        // Staff name
                        StaffName = _context.Users
                            .Where(u => u.UserId == e.StaffId)
                            .Select(u => u.Name)
                            .FirstOrDefault(),

                        // Manager name
                        ManagerName = _context.Users
                            .Where(u => u.UserId == e.ManagerId)
                            .Select(u => u.Name)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Ok(new
                {
                    department,
                    count = extraWorks.Count,
                    extraWorks
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to get department extra work.",
                    error = ex.Message
                });
            }
        }


        [HttpGet("departments-extra-work")]
        public async Task<IActionResult> GetExtraWorkByDepartments(
    [FromQuery] string departments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(departments))
                {
                    return BadRequest(new
                    {
                        message = "At least one department is required."
                    });
                }

                // Convert comma-separated departments into a list
                var departmentList = departments
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim())
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();

                if (departmentList.Count == 0)
                {
                    return BadRequest(new
                    {
                        message = "At least one valid department is required."
                    });
                }

                // Get users belonging to the requested departments
                var staffIds = await _context.Users
                    .Where(u => departmentList.Contains(u.Department))
                    .Select(u => u.UserId)
                    .ToListAsync();

                // Get ExtraWork for those users
                var extraWorks = await _context.ExtraWork
                    .Where(e => staffIds.Contains(e.StaffId))
                    .OrderByDescending(e => e.WorkDate)
                    .ThenByDescending(e => e.Id)
                    .Select(e => new
                    {
                        e.Id,
                        e.ManagerId,
                        e.StaffId,

                        e.TaskId,

                        TaskName = _context.Tasks
                            .Where(t => t.TaskCode == e.TaskId)
                            .Select(t => t.Task)
                            .FirstOrDefault(),

                        e.WorkType,
                        e.WorkDate,
                        e.ExpectedHours,
                        e.StartTime,
                        e.EndTime,
                        e.Reason,
                        e.Status,
                        e.StaffRemarks,
                        e.ManagerRemarks,
                        e.VerifiedBy,
                        e.IsCompensationUsed,

                        StaffName = _context.Users
                            .Where(u => u.UserId == e.StaffId)
                            .Select(u => u.Name)
                            .FirstOrDefault(),

                        ManagerName = _context.Users
                            .Where(u => u.UserId == e.ManagerId)
                            .Select(u => u.Name)
                            .FirstOrDefault(),

                        StaffDepartment = _context.Users
                            .Where(u => u.UserId == e.StaffId)
                            .Select(u => u.Department)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Ok(new
                {
                    departments = departmentList,
                    departmentCount = departmentList.Count,
                    count = extraWorks.Count,
                    extraWorks
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to get extra work for departments.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpPut("staff-response/{id}")]
        public async Task<IActionResult> StaffResponse(int id,[FromBody] StaffExtraWorkResponseDto request)
        {
            try
            {
                // Get logged-in user's ID from JWT
                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int userId))
                    return Unauthorized("Invalid User ID.");

                // Validate status
                if (string.IsNullOrWhiteSpace(request.Status))
                {
                    return BadRequest("Status is required.");
                }

                var status = request.Status.Trim();


                // If rejected, reason is required
                if (status == "StaffRejected" &&
                    string.IsNullOrWhiteSpace(request.StaffRemarks))
                {
                    return BadRequest(
                        "Please provide a reason for rejecting the extra work.");
                }

                // Find extra work belonging to this logged-in staff
                var extraWork = await _context.ExtraWork
                    .FirstOrDefaultAsync(e =>
                        e.Id == id &&
                        e.StaffId == userId);


                // Update
                extraWork.Status = status;
                extraWork.StaffRemarks = string.IsNullOrWhiteSpace(request.StaffRemarks)
                    ? null
                    : request.StaffRemarks.Trim();

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Extra work request {status.ToLower()} successfully.",
                    extraWork = new
                    {
                        extraWork.Id,
                        extraWork.Status,
        
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to update extra work status.",
                    error = ex.Message
                });
            }
        }

        [Authorize]
        [HttpPut("manager-response/{id}")]
        public async Task<IActionResult> ManagerResponse(int id,[FromBody] ManagerExtraWorkResponseRequest request)
        {
            try
            {
                // Get manager ID from JWT
                var userIdClaim = User.FindFirst("UserId");

                if (userIdClaim == null)
                    return Unauthorized("User ID not found in token.");

                if (!int.TryParse(userIdClaim.Value, out int managerId))
                    return Unauthorized("Invalid User ID.");

                // Validate status
                if (request.Status != "Approved" &&
                    request.Status != "ManagerRejected")
                {
                    return BadRequest(
                        "Status must be Approved or ManagerRejected.");
                }

                // If rejecting, reason is mandatory
                if (request.Status == "ManagerRejected" &&
                    string.IsNullOrWhiteSpace(request.ManagerRemarks))
                {
                    return BadRequest(
                        "Manager rejection reason is required.");
                }

                // Find the extra work belonging to this manager
                var extraWork = await _context.ExtraWork
                    .FirstOrDefaultAsync(e =>
                        e.Id == id &&
                        e.ManagerId == managerId);

                if (extraWork == null)
                {
                    return NotFound(
                        "Extra work request not found.");
                }

                // Manager can respond only after staff accepted
                if (extraWork.Status != "Accepted")
                {
                    return BadRequest(
                        $"Cannot process this request because its current status is '{extraWork.Status}'.");
                }

                // Update status
                extraWork.Status = request.Status;

                if (request.Status == "Approved")
                {
                    extraWork.ManagerRemarks = null;
                }
                else
                {
                    extraWork.ManagerRemarks =
                        request.ManagerRemarks?.Trim();
                }

                // Manager who verified/processed the request
                extraWork.VerifiedBy = managerId;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = request.Status == "Approved"
                        ? "Extra work approved successfully."
                        : "Extra work rejected successfully.",

                    id = extraWork.Id,
                    status = extraWork.Status,
                    managerRemarks = extraWork.ManagerRemarks,
                    verifiedBy = extraWork.VerifiedBy
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to process extra work request.",
                    error = ex.Message
                });
            }
        }


        [HttpGet("departments_overtime")]
        public async Task<IActionResult> GetOvertimeByDepartments(
    [FromQuery] string departments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(departments))
                {
                    return BadRequest(new
                    {
                        message = "At least one department is required."
                    });
                }

                // Convert comma-separated departments into a list
                var departmentList = departments
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim())
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();

                if (departmentList.Count == 0)
                {
                    return BadRequest(new
                    {
                        message = "At least one valid department is required."
                    });
                }

                // Get overtime for the selected departments
                var overtime = await (
                    from ot in _context.OverTime
                    join staff in _context.Users
                        on ot.Uid equals staff.UserId

                    where departmentList.Contains(ot.Dept)

                    orderby ot.Date descending

                    select new
                    {
                        ot.Id,
                        ot.Uid,

                        // Staff details
                        StaffName = staff.Name,

                        ot.Dept,
                        ot.Date,
                        ot.FromTime,
                        ot.ToTime,
                        ot.TotalHours,
                        ot.Reason,

                        ot.StaffStatus,
                        ot.StaffResponseReason,

                        ot.ManagerStatus,
                        ot.ManagerResponseReason,

                        ot.RequestedBy,
                        ot.Approved_by
                    }
                ).ToListAsync();

                return Ok(new
                {
                    departments = departmentList,
                    departmentCount = departmentList.Count,
                    count = overtime.Count,
                    overtime
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to get overtime details.",
                    error = ex.Message
                });
            }
        }

        //..............................................................................................


        private int CalculateTaskScore(
    DateTime dueDate,
    DateTime? completedDate,
    string priority,
    TimeSpan? endTime,
    DateTime startDate,
    int? targetQuantity = null,
    int? completedQuantity = null)
        {
            if (completedDate == null)
                return 0;

            // =====================================================
            // 1. EXISTING TIME SCORE
            // =====================================================

            int timeScore;

            if (startDate.Date == dueDate.Date &&
                completedDate.Value.Date == dueDate.Date)
            {
                if (endTime.HasValue)
                {
                    DateTime dueDateTime =
                        dueDate.Date.Add(endTime.Value);

                    TimeSpan difference =
                        completedDate.Value - dueDateTime;

                    if (difference < TimeSpan.Zero)
                    {
                        // Completed before EndTime
                        timeScore = 90;
                    }
                    else if (difference == TimeSpan.Zero)
                    {
                        // Completed exactly at EndTime
                        timeScore = 85;
                    }
                    else
                    {
                        // Completed after EndTime
                        int lateHours =
                            (int)Math.Ceiling(difference.TotalHours);

                        timeScore = 85 - (lateHours * 5);

                        timeScore = Math.Max(timeScore, 50);
                    }
                }
                else
                {
                    // Same day and no EndTime
                    timeScore = 90;
                }
            }
            else
            {
                if (endTime.HasValue)
                {
                    DateTime dueDateTime =
                        dueDate.Date.Add(endTime.Value);

                    TimeSpan difference =
                        completedDate.Value - dueDateTime;

                    if (difference < TimeSpan.Zero)
                    {
                        timeScore = 90;
                    }
                    else if (difference == TimeSpan.Zero)
                    {
                        timeScore = 85;
                    }
                    else
                    {
                        int lateHours =
                            (int)Math.Ceiling(difference.TotalHours);

                        timeScore = 85 - (lateHours * 5);

                        timeScore = Math.Max(timeScore, 50);
                    }
                }
                else
                {
                    int lateDays =
                        (completedDate.Value.Date - dueDate.Date).Days;

                    if (lateDays < 0)
                        timeScore = 90;
                    else if (lateDays == 0)
                        timeScore = 85;
                    else if (lateDays == 1)
                        timeScore = 80;
                    else if (lateDays == 2)
                        timeScore = 75;
                    else if (lateDays == 3)
                        timeScore = 70;
                    else if (lateDays == 4)
                        timeScore = 65;
                    else if (lateDays == 5)
                        timeScore = 60;
                    else if (lateDays == 6)
                        timeScore = 55;
                    else
                        timeScore = 50;
                }
            }


            // =====================================================
            // 2. PRIORITY BONUS
            // =====================================================

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


            // =====================================================
            // 3. NORMAL TASK SCORE
            // =====================================================

            int normalScore =
                timeScore + priorityBonus;

            // Maximum system points = 90
            normalScore =
                Math.Clamp(normalScore, 50, 90);


            // =====================================================
            // 4. QUANTITY CALCULATION
            // =====================================================

            // No quantity task
            if (!targetQuantity.HasValue)
            {
                return normalScore;
            }


            // Quantity task but completed quantity not available
            if (!completedQuantity.HasValue)
            {
                return 0;
            }


            int target = targetQuantity.Value;
            int completed = completedQuantity.Value;


            // Prevent invalid values
            if (target <= 0)
            {
                return normalScore;
            }

            if (completed < 0)
            {
                completed = 0;
            }


            // =====================================================
            // 5. COMPLETED >= TARGET
            // =====================================================

            if (completed >= target)
            {
                return normalScore;
            }


            // =====================================================
            // 6. PARTIAL QUANTITY
            // =====================================================

            double quantityPercentage =
                (double)completed / target;


            // Reduce normal score according to completion %
            int quantityScore =
                (int)Math.Round(
                    normalScore * quantityPercentage,
                    MidpointRounding.AwayFromZero);


            // =====================================================
            // 7. RETURN QUANTITY-ADJUSTED SCORE
            // =====================================================

            return Math.Clamp(quantityScore, 0, 90);
        }

        private int CalculateGoalPoints( List<int> taskAveragePoints,string goalPriority,DateTime? dueDate)
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
