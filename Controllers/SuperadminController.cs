using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff;
using staff.Services;
using staff_work_tracking.Data;
using System.Data;

namespace staff_work_tracking.Controllers
{
    [Route("api/Director")]
    [ApiController]
    public class SuperadminController : ControllerBase
    {

        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private NotificationService _notific;

        public SuperadminController(AppDbContext context, IConfiguration config, NotificationService notificationService)
        {
            _context = context;
            _config = config;
            _notific = notificationService;
        }


        [HttpGet("getallusers")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Department,
                    u.Role,
                    u.Status,
                    u.Created_by,
                    u.wasEdited
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount = users.Count,
                users
            });
        }



        [HttpGet("getAllManager")]
        public async Task<IActionResult> GetAllAdmins()
        {
            var admins = await _context.Users
                .Where(u => u.Role == "2")
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Department,
                    u.Status,
                    u.Created_by
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount = admins.Count,
                admins
            });
        }



        [HttpGet("getallstaff")]
        public async Task<IActionResult> GetAllEmployees()
        {
            var employees = await _context.Users
                //.Where(u => u.Role == "Staff")
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    u.Email,
                    u.Department,
                    u.Status,
                    u.Created_by,
                    u.wasEdited
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount = employees.Count,
                employees
            });
        }



        [HttpGet("usersdetails/{adminId}")]
        public async Task<IActionResult> GetAdminDetails(int adminId)
        {
          
            var admin = await _context.Users
                .Where(u => u.UserId == adminId)
                .Select(a => new
                {
                    a.UserId,
                    a.Name,
                    a.Email,
                    a.Role,
                    a.Department,
                    a.Created_by,
                    a.Status,
                    a.wasEdited
                })
                .FirstOrDefaultAsync();

            if (admin == null)
                return NotFound("Manager not found");

            var totalEmployees = await _context.Users.CountAsync(u =>
                u.Department == admin.Department &&
                u.Role == "Staff"   
            );

          
            var totalTasksAssignedTo = await _context.TaskMembers.CountAsync(tm =>
                tm.Assign_To.StartsWith(adminId + "-")
            );

          
            var totalTasksAssignedBy = await _context.TaskMembers.CountAsync(tm =>
                tm.Assign_By.StartsWith(adminId + "-")
            );

            return Ok(new
            {
                admin,
                totalEmployees,
                totalTasksAssignedTo,
                totalTasksAssignedBy
            });
        }

        [Authorize]
        [HttpPost("Task-assign")]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            int assignedById = int.Parse(userIdClaim.Value);

            var assignedByUser = await _context.Users.FindAsync(assignedById);
            if (assignedByUser == null)
                return BadRequest("Assigned By user not found");

            var assignedToUsers = await _context.Users
                .Where(u => dto.AssignedToIds.Contains(u.UserId))
                .ToListAsync();

            if (assignedToUsers.Count != dto.AssignedToIds.Count)
                return BadRequest("One or more Assigned To users not found");

          
            int nextTaskId = (await _context.Tasks.MaxAsync(t => (int?)t.Id) ?? 0) + 1;
            var goal = await _context.Goal
    .FirstOrDefaultAsync(g => g.GoalCode == dto.GoalCode);
            var task = new TaskTable
            {
                TaskCode = "T" + nextTaskId, 
                Task = dto.Task,
                GoalCode = dto.GoalCode,
                Description = dto.Description,
                Priority = dto.Priority,
                Status = "Not Started",
                Created_At = dto.Start_date,
                Due_Date = dto.Due_Date,
              
                Members = assignedToUsers.Count,
                    PerformanceType = dto.PerformanceType,
                Quantity = dto.Quantity,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

         
            int lastTMId = (await _context.TaskMembers.MaxAsync(tm => (int?)tm.Id) ?? 0);
            int tmCounter = lastTMId + 1;

            foreach (var user in assignedToUsers)
            {
                var member = new TaskMember
                {
                    TMCode = "TM" + tmCounter++,   
                    TaskCode = task.TaskCode,
                    Assign_To = $"{user.UserId}-{user.Name}",
                    Assign_By = $"{assignedByUser.UserId}-{assignedByUser.Name}",
                    UserStatus = "Not Started",
                    Assigned_At = DateTime.Now
                };

                _context.TaskMembers.Add(member);
            }
            if (!string.IsNullOrEmpty(task.GoalCode))
            {
                var goalData = await _context.Goal
                    .FirstOrDefaultAsync(g => g.GoalCode == task.GoalCode);

                if (goalData != null)
                {
                    var goalTasks = await _context.Tasks
                        .Where(t => t.GoalCode == goalData.GoalCode)
                        .ToListAsync();

                    int total = goalTasks.Count;

                    int completed = goalTasks.Count(t =>
                        !string.IsNullOrEmpty(t.Status) &&
                        t.Status.Trim().ToLower() == "completed"
                    );

                    int notStarted = goalTasks.Count(t =>
                        !string.IsNullOrEmpty(t.Status) &&
                        t.Status.Trim().ToLower() == "not started"
                    );

                    // ✅ FIX: Update Status ALSO
                    if (completed == total)
                    {
                        goalData.Status = "completed";
                        goalData.Completed_Date = DateTime.Now;
                    }
                    else if (notStarted == total)
                    {
                        goalData.Status = "not started";
                        goalData.Completed_Date = null;
                    }
                    else
                    {
                        goalData.Status = "inprogress";
                        goalData.Completed_Date = null;
                    }

                    goalData.Progress = total == 0 ? 0 : (int)(((double)completed / total) * 100);

                    await _context.SaveChangesAsync();
                }
            }
          
            await _context.SaveChangesAsync();
            await _notific.SendTaskGoalNotification(
     "Created",
     "Task",
     task.TaskCode,
     task.Task,
     assignedByUser.UserId,
     assignedByUser.Name,
     assignedByUser.Role,
     assignedByUser.Department,
     null, 
     dto.AssignedToIds 
 );
            await _context.SaveChangesAsync();
            return Ok(new
            {
                message = "Task created successfully",
                taskCode = task.TaskCode
            });
        }



        [Authorize]
        [HttpPost("CreateGoal")]
        public async Task<IActionResult> CreateGoal([FromBody] Goal model)
        {
            if (model == null)
                return BadRequest("Invalid data");

            var lastGoal = await _context.Goal
                .OrderByDescending(g => g.Id)
                .FirstOrDefaultAsync();

            int nextNumber = 1;

            if (lastGoal != null && !string.IsNullOrEmpty(lastGoal.GoalCode))
            {
                nextNumber = int.Parse(lastGoal.GoalCode.Substring(1)) + 1;
            }

            // ✅ Get creator id from token
            var assignBy = User.FindFirst("UserId")?.Value;

            if (assignBy == null)
                return Unauthorized("Invalid token");

            // ✅ Convert to int
            if (!int.TryParse(assignBy, out int creatorId))
                return BadRequest("Invalid creator id");

            // ✅ Get creator details
            var creator = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == creatorId);

            if (creator == null)
                return BadRequest("Creator not found");

            // ❗ FIX: Assign_To is "2-Abi" format
            var idPart = model.Assign_To.Split('-')[0];

            if (!int.TryParse(idPart, out int assignToId))
                return BadRequest("Invalid Assign_To");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == assignToId);

            if (user == null)
                return BadRequest("Assigned user not found");

            var goal = new Goal
            {
                GoalCode = $"G{nextNumber}",
                Title = model.Title,
                Priority = model.Priority,
                StartDate = model.StartDate,
                DueDate = model.DueDate,
                Assign_To = model.Assign_To,
                Assign_By = assignBy,
                Department = user.Department,
                Progress = 0,
                Goalpoints = 0,
                Status = "Not Started"
            };

            _context.Goal.Add(goal);
            await _context.SaveChangesAsync();

            
            await _notific.SendTaskGoalNotification(
                "Created",
                "Goal",
                goal.GoalCode,
                goal.Title,
                creator.UserId, 
                creator.Name,         
                creator.Role,            
                creator.Department,     
                goal.Assign_To           
            );

            return Ok(new
            {
                message = "Goal created successfully",
                goalCode = goal.GoalCode
            });
        }

        [Authorize]
        [HttpPut("UpdateGoal/{code}")]
        public async Task<IActionResult> UpdateGoal(string code, [FromBody] UpdateGoalDto model)
        {
            if (model == null)
                return BadRequest("Invalid data");

            // ✅ Get logged-in user
            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null)
                return Unauthorized();

            int editorId = int.Parse(userIdClaim.Value);
            string editorRole = roleClaim?.Value ?? "Unknown";

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null)
                return BadRequest("Editor not found");

            // ✅ Find goal
            var goal = await _context.Goal
                .FirstOrDefaultAsync(g => g.GoalCode == code);

            if (goal == null)
                return NotFound("Goal not found");

            // ✅ Track old values
            var oldTitle = goal.Title;
            var oldPriority = goal.Priority;
            var oldDueDate = goal.DueDate;

            // ✅ Update only allowed fields
            if (!string.IsNullOrWhiteSpace(model.Title))
                goal.Title = model.Title;

            if (!string.IsNullOrWhiteSpace(model.Priority))
                goal.Priority = model.Priority;

            if (model.DueDate.HasValue)
                goal.DueDate = model.DueDate.Value;


            // ✅ Audit log (simple)
            _context.Auditlog.Add(new Auditlog
            {
                EntityId = goal.GoalCode,
                EntityType = "Goal",
                Action = "Edit",
                Fieldchanged = "Goal",
                Oldvalue = oldTitle,
                Newvalue = goal.Title,
                EditedUid = editor.UserId.ToString(),
                EditedRole = editorRole,
                ChangeDateandTime = DateTime.Now
            });

            await _context.SaveChangesAsync();

            await _notific.SendTaskGoalNotification(
      "Edited",
      "Goal",
      goal.GoalCode,
      goal.Title,
      editor.UserId,
      editor.Name,
      editorRole,
      editor.Department,
      goal.Assign_To 
  );

            return Ok(new
            {
                message = "Goal updated successfully"
            });
        }


        [Authorize]
        [HttpDelete("DeleteGoal/{code}")]
        public async Task<IActionResult> DeleteGoal(string code)
        {
            // ✅ Get logged-in user
            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null)
                return Unauthorized();

            int editorId = int.Parse(userIdClaim.Value);
            string editorRole = roleClaim?.Value ?? "Unknown";

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null)
                return BadRequest("Editor not found");

            // ✅ Get Goal
            var goal = await _context.Goal
                .FirstOrDefaultAsync(g => g.GoalCode == code);

            if (goal == null)
                return NotFound("Goal not found");

            // ✅ Fix message
            if (goal.Status == "completed")
                return BadRequest("Cannot delete completed goal");

            var goalName = goal.Title;

            // ✅ Get all tasks under this goal
            var tasks = await _context.Tasks
                .Where(t => t.GoalCode == code)
                .ToListAsync();

            // ✅ Audit log for Goal
            _context.Auditlog.Add(new Auditlog
            {
                EntityId = goal.GoalCode,
                EntityType = "Goal",
                Action = "Delete",
                Fieldchanged = "Goal",
                Oldvalue = goalName,
                Newvalue = "Deleted",
                EditedUid = editor.UserId.ToString(),
                EditedRole = editorRole,
                ChangeDateandTime = DateTime.Now
            });

            // ✅ OPTIONAL: Audit logs for each Task (recommended)
            //foreach (var task in tasks)
            //{
            //    _context.Auditlog.Add(new Auditlog
            //    {
            //        EntityId = task.TaskCode,
            //        EntityType = "Task",
            //        Action = "Delete",
            //        Fieldchanged = "Task",
            //        Oldvalue = task.Task,
            //        Newvalue = "Deleted",
            //        EditedUid = editor.UserId.ToString(),
            //        EditedRole = editorRole,
            //        ChangeDateandTime = DateTime.Now
            //    });
            //}

            // ✅ Delete all tasks first
            if (tasks.Any())
            {
                _context.Tasks.RemoveRange(tasks);
            }

            // ✅ Delete Goal
            _context.Goal.Remove(goal);

            // ✅ Send notification
            await _notific.SendTaskGoalNotification(
                "Deleted",
                "Goal",
                goal.GoalCode,
                goal.Title,
                editor.UserId,
                editor.Name,
                editorRole,
                editor.Department,
                goal.Assign_To
            );

            // ✅ Save changes
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Goal and its tasks deleted successfully"
            });
        }

        [Authorize]
        [HttpGet("GetGoals")]
        public async Task<IActionResult> GetGoals()
        {
            var userId = int.Parse(User.FindFirst("UserId")!.Value);
            var role = User.FindFirst("Role")!.Value;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            IQueryable<Goal> query = _context.Goal;

            if (role == "2")
            {
                query = query.Where(g =>
                    g.Assign_To == userId.ToString() ||
                    g.Department == user.Department
                );
            }
            else if (role != "1")
            {
                query = query.Where(g =>
                    g.Assign_To == userId.ToString()
                );
            }

            var goals = await query
                .Select(g => new
                {
                    g.GoalCode,
                    g.Title,
                    g.Priority,
                    g.Status,
                    g.Progress,
                    g.StartDate,
                    g.DueDate,
                    g.Department,
                })
                .ToListAsync();

            return Ok(goals);
        }


        [Authorize]
        [HttpGet("GetGoalsWithTasks")]
        public async Task<IActionResult> GetGoalsWithTasks()
        {
            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim.Value);
            string role = roleClaim.Value;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return BadRequest("User not found");

            IQueryable<Goal> goalQuery = _context.Goal;

            if (role != "1")
                goalQuery = goalQuery.Where(g => g.Assign_To == userId.ToString());

            var goals = await goalQuery
                .OrderByDescending(g => g.Id)
                .ToListAsync();

            var goalCodes = goals.Select(g => g.GoalCode).ToList();

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
                        var assignerMember = taskMembers
                            .Where(tm => tm.TaskCode == t.TaskCode && !string.IsNullOrEmpty(tm.Assign_By))
                            .Select(tm =>
                            {
                                var uId = int.Parse(tm.Assign_By.Split('-')[0]);
                                var u = users.FirstOrDefault(x => x.UserId == uId);

                                return u == null ? null : new
                                {
                                    Name = u.Name,
                                    Role = u.Role,
                                    Department = u.Department
                                };
                            })
                            .FirstOrDefault();

                        var assignedToUsers = taskMembers
                            .Where(tm => tm.TaskCode == t.TaskCode && !string.IsNullOrEmpty(tm.Assign_To))
                            .Select(tm =>
                            {
                                var uId = int.Parse(tm.Assign_To.Split('-')[0]);
                                var u = users.FirstOrDefault(x => x.UserId == uId);

                                return u == null ? null : new
                                {
                                    userId = u.UserId,
                                    name = u.Name,
                                    department = u.Department,
                                    role = u.Role
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
                            assignedBy = assignerMember?.Name ?? "N/A",
                            assignerRole = assignerMember?.Role ?? "N/A",
                            assignerDepartment = assignerMember?.Department ?? "N/A",
                            assignedTo = assignedToUsers,
                       
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
                    g.StartDate,
                    g.DueDate,
                    g.Department,

                    g.Goalpoints,

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


        [HttpGet("tasks")]
        public async Task<IActionResult> GetAllTasks()
        {
            var tasks = await _context.Tasks
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();

            var taskMembers = await _context.TaskMembers.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var result = tasks.Select(t =>
            {

                var assignerMember = taskMembers
                    .Where(tm => tm.TaskCode == t.TaskCode && !string.IsNullOrEmpty(tm.Assign_By))
                    .Select(tm =>
                    {
                        var userId = int.Parse(tm.Assign_By.Split('-')[0]);
                        var user = users.FirstOrDefault(u => u.UserId == userId);
                        return user == null ? null : new
                        {
                            Name = user.Name,
                            Role = user.Role,
                            Department = user.Department
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
                    
                    assignedBy = assignerMember?.Name ?? "N/A",
                    assignerRole = assignerMember?.Role ?? "N/A",
                    assignerDepartment = assignerMember?.Department ?? "N/A",

                    assignedTo = taskMembers
                    .Where(tm => tm.TaskCode == t.TaskCode && !string.IsNullOrEmpty(tm.Assign_To))
                    .Select(tm =>
                    {
                        var userId = int.Parse(tm.Assign_To.Split('-')[0]);
                        var user = users.FirstOrDefault(u => u.UserId == userId);

                        return user == null ? null : new
                        {
                            userId = user.UserId,
                            name = user.Name,
                            department = user.Department,
                            role = user.Role
                        };
                    })
                    .Where(x => x != null)
                    .ToList()
                };
            });

            var totalTasks = await _context.Tasks.CountAsync();
            var pendingCount = await _context.Tasks.CountAsync(t => t.Status == "Pending");
            var inProgressCount = await _context.Tasks.CountAsync(t => t.Status == "In Progress");
            var completedCount = await _context.Tasks.CountAsync(t => t.Status == "Completed");

            return Ok(new
            {
                totalTasks,
                pendingCount,
                inProgressCount,
                completedCount,
                result
            });
        }


        [HttpGet("taskbyid/{taskCode}")]
        public async Task<IActionResult> GetTaskByCode(string taskCode)
        {
            var task = await _context.Tasks
                .Where(t => t.TaskCode == taskCode)
                .FirstOrDefaultAsync();

            if (task == null)
                return NotFound("Task not found");

            var taskMembers = await _context.TaskMembers
                .Where(tm => tm.TaskCode == taskCode)
                .ToListAsync();

            var users = await _context.Users.ToListAsync();

            var assignedByString = taskMembers
        .Select(tm => tm.Assign_By)
        .FirstOrDefault();

            string assignedByDepartment = null;
            if (!string.IsNullOrEmpty(assignedByString))
            {
                var assignById = int.Parse(assignedByString.Split('-')[0]);
                var user = users.FirstOrDefault(u => u.UserId == assignById);
                assignedByDepartment = user?.Department ?? "N/A";
            }

            var result = new
            {
                taskCode = task.TaskCode,
                task = task.Task,
                description = task.Description,
                priority = task.Priority,
                status = task.Status,
                createdAt = task.Created_At,
                dueDate = task.Due_Date,
                totalMembers = task.Members,
                wasEdited = task.wasEdited,
                completed_date = task.Completed_Date,
                performanceType = task.PerformanceType,
                quantity = task.Quantity,
                startTime = task.StartTime,
                endTime = task.EndTime,
                assignedBy = taskMembers
                    .Select(tm => tm.Assign_By)
                    .FirstOrDefault(),
                assignerDepartment = assignedByDepartment,
                assignedTo = taskMembers
                    .Where(tm => !string.IsNullOrEmpty(tm.Assign_To))
                    .Select(tm =>
                    {
                        var userId = int.Parse(tm.Assign_To.Split('-')[0]);
                        var user = users.FirstOrDefault(u => u.UserId == userId);

                        return user == null ? null : new
                        {
                            userId = user.UserId,
                            name = user.Name,
                            department = user.Department,
                            role = user.Role
                        };
                    })
                    .Where(x => x != null)
                    .ToList()
            };

            return Ok(result);
        }


      [Authorize]
      [HttpPut("update-usersstatus/{userid}")]
    public async Task<IActionResult> UpdateAdminStatus(int userid,[FromBody] StatusUpdateDto dto)
    {
        var user = await _context.Users.FindAsync(userid);
        if (user == null)
            return NotFound();

        // Get logged-in user's ID from JWT
        var userIdClaim = User.FindFirst("UserId")?.Value;

        if (string.IsNullOrEmpty(userIdClaim))
            return Unauthorized("UserId claim not found.");

        int editorId = int.Parse(userIdClaim);

        // Get role from Users table
        var editor = await _context.Users
            .FirstOrDefaultAsync(u => u.UserId == editorId);

        if (editor == null)
            return Unauthorized("Editor not found.");

        string oldStatus = user.Status;

        user.Status = dto.Status;

        _context.Auditlog.Add(new Auditlog
        {
            EntityId = user.UserId.ToString(),   // User whose status changed
            EntityType = "User",
            Action = "Status Update",
            Fieldchanged = "Status",
            Oldvalue = oldStatus,
            Newvalue = dto.Status,

            EditedUid = editor.UserId.ToString(), // Who edited
            EditedRole = editor.Role,             // Editor's role

            ChangeDateandTime = DateTime.Now
        });

        await _context.SaveChangesAsync();

        return Ok("Status updated");
    }

    [Authorize]
        [HttpPut("Task-edit")]
        public async Task<IActionResult> EditTask([FromBody] EditTaskDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null) return Unauthorized();

            int editorId = int.Parse(userIdClaim.Value);
            string editorRole = roleClaim?.Value ?? "Unknown";

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null) return BadRequest("Editor not found");

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskCode == dto.TaskCode);

            if (task == null) return NotFound("Task not found");

            // ✅ Audit helper
            void AddAudit(string field, string oldVal, string newVal)
            {
                _context.Auditlog.Add(new Auditlog
                {
                    EntityId = task.TaskCode,
                    EntityType = "Task",
                    Action = "Edit",
                    Fieldchanged = field,
                    Oldvalue = oldVal,
                    Newvalue = newVal,
                    EditedUid = editor.UserId.ToString(),
                    EditedRole = editorRole,
                    ChangeDateandTime = DateTime.Now
                });
            }

            bool isChanged = false;

            void UpdateField<T>(string field, T oldVal, T newVal, Action setValue)
            {
                string oldString = oldVal?.ToString()?.Trim() ?? "";
                string newString = newVal?.ToString()?.Trim() ?? "";

                if (oldString != newString)
                {
                    AddAudit(field, oldString, newString);
                    setValue();
                    isChanged = true;
                }
            }

            UpdateField("Task", task.Task, dto.Task, () => task.Task = dto.Task);
            UpdateField("Description", task.Description, dto.Description, () => task.Description = dto.Description);
            UpdateField("Priority", task.Priority, dto.Priority, () => task.Priority = dto.Priority);
            UpdateField("Due_Date", task.Due_Date, dto.Due_Date, () => task.Due_Date = dto.Due_Date);
            UpdateField(
    "Quantity",
    task.Quantity,
    dto.Quantity,
    () => task.Quantity = dto.Quantity
);

            UpdateField(
                "StartTime",
                task.StartTime,
                dto.StartTime,
                () => task.StartTime = dto.StartTime
            );

            UpdateField(
                "EndTime",
                task.EndTime,
                dto.EndTime,
                () => task.EndTime = dto.EndTime
            );

            // ✅ Members handling
            var existingMembers = await _context.TaskMembers
                .Where(tm => tm.TaskCode == dto.TaskCode)
                .ToListAsync();

            var existingUserIds = existingMembers
                .Select(tm => int.Parse(tm.Assign_To.Split('-')[0]))
                .ToList();

            var newUserIds = dto.AssignedToIds;

            // ➕ Add users
            var usersToAdd = await _context.Users
                .Where(u => newUserIds.Contains(u.UserId) && !existingUserIds.Contains(u.UserId))
                .ToListAsync();

            int tmCounter = (await _context.TaskMembers.MaxAsync(x => (int?)x.Id) ?? 0) + 1;

            foreach (var user in usersToAdd)
            {
                _context.TaskMembers.Add(new TaskMember
                {
                    TMCode = "TM" + tmCounter++,
                    TaskCode = task.TaskCode,
                    Assign_To = $"{user.UserId}-{user.Name}",
                    Assign_By = $"{editor.UserId}-{editor.Name}",
                    UserStatus = "Not Started",
                    Assigned_At = DateTime.Now
                });

                AddAudit("Assigned User", "None", $"{user.UserId}-{user.Name}");
            }

            // ➖ Remove users
            var membersToRemove = existingMembers
                .Where(tm => !newUserIds.Contains(int.Parse(tm.Assign_To.Split('-')[0])))
                .ToList();


            foreach (var member in membersToRemove)
            {
                int removedUserId =
                    int.Parse(member.Assign_To.Split('-')[0]);

                var removedInfo = dto.RemovedMembers
                    .FirstOrDefault(x => x.UserId == removedUserId);

                _context.TaskMemberRemoval.Add(
                    new TaskMemberRemoval
                    {
                        TaskCode = task.TaskCode,
                        UserId = removedUserId,
                        RemovedBy = editor.UserId,
                        RemovedDate = DateTime.Now,
                        Reason = removedInfo?.Reason ?? "",
                        IsPenaltyApplied = false,
                        PenaltyPoints = 0
                    });

                _context.TaskMembers.Remove(member);
            }

            task.Members = newUserIds.Count;

            bool hasMemberChanges = usersToAdd.Any() || membersToRemove.Any();

            if (isChanged || hasMemberChanges)
            {
                task.wasEdited = true;
            }

            await _context.SaveChangesAsync();
        
            // ✅ Notification
            if (isChanged || hasMemberChanges)
            {
                var finalNotifyUsers = existingUserIds
                    .Union(usersToAdd.Select(u => u.UserId))
                    .ToList();

                await _notific.SendTaskGoalNotification(
                    "Edited",
                    "Task",
                    task.TaskCode,
                    task.Task,
                    editor.UserId,
                    editor.Name,
                    editorRole,
                    editor.Department,
                    null,
                    finalNotifyUsers
                );
            }

            return Ok(new
            {
                message = "Task updated successfully",
                taskCode = task.TaskCode,
                wasEdited = task.wasEdited
            });
        }


        [Authorize]
        [HttpDelete("Task-delete/{taskCode}")]
        public async Task<IActionResult> DeleteTask(string taskCode)
        {
            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null) return Unauthorized();

            int editorId = int.Parse(userIdClaim.Value);
            string editorRole = roleClaim?.Value ?? "";

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null) return BadRequest("Editor not found");

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskCode == taskCode);

            if (task == null) return NotFound("Task not found");

            var taskName = task.Task;

            // ✅ Get members BEFORE deleting
            var members = await _context.TaskMembers
                .Where(tm => tm.TaskCode == taskCode)
                .ToListAsync();

            var assignedUserIds = members
                .Select(tm => int.Parse(tm.Assign_To.Split('-')[0]))
                .ToList();

            // ✅ Remove members ONCE
            _context.TaskMembers.RemoveRange(members);

            // ✅ Add audit ONLY ONCE
            _context.Auditlog.Add(new Auditlog
            {
                EntityId = task.TaskCode,
                EntityType = "Task",
                Action = "Delete",
                Fieldchanged = "Task",
                Oldvalue = taskName,
                Newvalue = "Deleted",
                EditedUid = editor.UserId.ToString(),
                EditedRole = editorRole,
                ChangeDateandTime = DateTime.Now
            });

            // ✅ Remove task
            _context.Tasks.Remove(task);

            await _context.SaveChangesAsync();

            // ✅ Notification after save
            await _notific.SendTaskGoalNotification(
                "Deleted",
                "Task",
                task.TaskCode,
                taskName,
                editor.UserId,
                editor.Name,
                editorRole,
                editor.Department,
                null,
                assignedUserIds
            );

            return Ok(new
            {
                message = $"Task '{taskName}' deleted successfully"
            });
        }

        [Authorize]
        [HttpGet("task-member-removals")]
        public async Task<IActionResult> GetTaskMemberRemovals()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int currentUserId = int.Parse(userIdClaim.Value);

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == currentUserId);

            if (currentUser == null)
                return NotFound("User not found");

            var department = currentUser.Department;

            var removals = await (
                from r in _context.TaskMemberRemoval

                join removedUser in _context.Users
                    on r.UserId equals removedUser.UserId

                join removedByUser in _context.Users
                    on r.RemovedBy equals removedByUser.UserId

                join task in _context.Tasks
                    on r.TaskCode equals task.TaskCode

                where removedUser.Department == department
                where removedUser.Department == department
       && r.IsPenaltyApplied == false  
                orderby r.RemovedDate descending

                select new
                {
                    r.Id,
                    TaskCode = r.TaskCode,
                    TaskName = task.Task,
                    UserId = removedUser.UserId,
                    UserName = removedUser.Name,
                    RemovedById = removedByUser.UserId,
                    RemovedByName = removedByUser.Name,
                    r.Reason,
                    r.RemovedDate,
                    r.IsPenaltyApplied,
                    r.PenaltyPoints
                }
            ).ToListAsync();

            return Ok(removals);
        }


        [HttpPost("penalty-points")]
        public async Task<IActionResult> ProcessRemovalRequest(string taskCode,int userId,bool applyPenalty)
        {
            var removal = await _context.TaskMemberRemoval
                .FirstOrDefaultAsync(x =>
                    x.TaskCode == taskCode &&
                    x.UserId == userId);

            if (removal == null)
                return NotFound("Request not found");

            // No penalty
            if (!applyPenalty)
            {
                _context.TaskMemberRemoval.Remove(removal);

                await _context.SaveChangesAsync();

                return Ok("Request closed without penalty");
            }

            var task = await _context.Tasks
                .FirstOrDefaultAsync(x => x.TaskCode == taskCode);

            if (task == null)
                return NotFound("Task not found");

            int points = task.Priority switch
            {
                "High" => 10,
                "Medium" => 5,
                "Normal" => 3,
                _ => 0
            };

            removal.IsPenaltyApplied = true;
            removal.PenaltyPoints = points;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Penalty applied successfully",
                Points = points
            });
        }


        [Authorize]
        [HttpDelete("user-delete/{userId}")]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            var editorIdClaim = User.FindFirst("UserId")?.Value;
            var editorRole = User.FindFirst("Role")?.Value;

            if (editorIdClaim == null)
                return Unauthorized();

            int editorId = int.Parse(editorIdClaim);

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null)
                return BadRequest("Editor not found");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found");

            string assignToKey = $"{user.UserId}-{user.Name}";
            var userName = user.Name;

            // ✅ Get task members
            var taskMembers = await _context.TaskMembers
                .Where(tm => tm.Assign_To == assignToKey)
                .ToListAsync();

            var taskCodes = taskMembers.Select(tm => tm.TaskCode).Distinct().ToList();

            var tasks = await _context.Tasks
                .Where(t => taskCodes.Contains(t.TaskCode))
                .ToListAsync();

            // ✅ Update task members count
            foreach (var task in tasks)
            {
                if (task.Members > 0)
                    task.Members -= 1;
            }

            // ✅ Remove task members
            _context.TaskMembers.RemoveRange(taskMembers);

            // ✅ Audit (single clean entry for user delete)
            _context.Auditlog.Add(new Auditlog
            {
                EntityId = user.UserId.ToString(),
                EntityType = "User",
                Action = "Delete",
                Fieldchanged = "User",
                Oldvalue = userName,
                Newvalue = "Deleted",
                EditedUid = editor.UserId.ToString(),
                EditedRole = editorRole,
                ChangeDateandTime = DateTime.Now
            });

            // ✅ Remove user
            _context.Users.Remove(user);

            await _context.SaveChangesAsync();

            // ✅ Notification
            await _notific.SendUserCrudNotificationToDirector(
                "Deleted",
                editor.UserId,
                editor.Name,
                editor.Role,
                editor.Department,
                userName
            );

            return Ok(new
            {
                message = $"User '{userName}' deleted successfully"
            });
        }

        [Authorize]
        [HttpPut("user-edit/{userId}")]
        public async Task<IActionResult> EditUser(int userId, UpdateUserDto dto)
        {
            var editorIdClaim = User.FindFirst("UserId")?.Value;
            var editorRole = User.FindFirst("Role")?.Value;

            if (editorIdClaim == null) return Unauthorized();

            int editorId = int.Parse(editorIdClaim);

            var editor = await _context.Users.FindAsync(editorId);
            if (editor == null)
                return BadRequest("Editor not found");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found");

            bool userChanged = false;

            void AddAudit(string field, string oldVal, string newVal)
            {
                _context.Auditlog.Add(new Auditlog
                {
                    EntityId = user.UserId.ToString(),
                    EntityType = "User",
                    Action = "Edit",
                    Fieldchanged = field,
                    Oldvalue = oldVal,
                    Newvalue = newVal,
                    EditedUid = editorId.ToString(),
                    EditedRole = editorRole,
                    ChangeDateandTime = DateTime.Now
                });

                userChanged = true;
            }

            // ✅ Compare & log
            if (user.Name != dto.Name)
                AddAudit("Name", user.Name, dto.Name);

            if (user.Email != dto.Email)
                AddAudit("Email", user.Email, dto.Email);

            if (user.Department != dto.Department)
                AddAudit("Department", user.Department, dto.Department);

            // ✅ Update
            user.Name = dto.Name;
            user.Email = dto.Email;
            user.Department = dto.Department;

            if (userChanged)
                user.wasEdited = true;

            await _context.SaveChangesAsync();

            // ✅ Notification
            if (userChanged)
            {
                await _notific.SendUserCrudNotificationToDirector(
                    "Edit",
                    editor.UserId,
                    editor.Name,
                    editor.Role,
                    editor.Department,
                    user.Name
                );
            }

            return Ok(new { message = "User updated successfully" });
        }


        [Authorize]
        [HttpGet("auditlog")]
        public async Task<IActionResult> GetAuditLogs()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            var role = User.FindFirst("Role")?.Value;

            if (userIdClaim == null || role == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim);

            // Get logged-in user details
            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (currentUser == null)
                return Unauthorized();

            // ================= BASE QUERY =================

            var query =
                from audit in _context.Auditlog

                join user in _context.Users
                    on audit.EditedUid equals user.UserId.ToString()
                    into userJoin
                from editor in userJoin.DefaultIfEmpty()

                join task in _context.Tasks
                    on audit.EntityId equals task.TaskCode
                    into taskJoin
                from taskData in taskJoin.DefaultIfEmpty()

                    // JOIN ROLE TABLE
                join roleData in _context.Roles
                    on audit.EditedRole equals roleData.Id.ToString()
                    into roleJoin
                from editorRole in roleJoin.DefaultIfEmpty()

                select new
                {
                    audit,
                    editor,
                    taskData,
                    editorRole
                };

            // ================= ROLE FILTER =================

            if (role != "1")
            {
                // Manager → filter by department
                query = query.Where(x =>
                    x.editor != null &&
                    x.editor.Department == currentUser.Department
                );
            }

            // ================= FINAL SELECT =================

            var auditLogs = await query
                .OrderByDescending(x => x.audit.ChangeDateandTime)
                .Select(x => new
                {
                    auditId = x.audit.Id,

                    entityType = x.audit.EntityType,

                    entityId = x.audit.EntityId,

                    action = x.audit.Action,

                    fieldChanged = x.audit.Fieldchanged,

                    oldValue = x.audit.Oldvalue,

                    newValue = x.audit.Newvalue,

                    editedById = x.audit.EditedUid,

                    editedByName = x.editor != null
                        ? x.editor.Name
                        : "System",

                    // RETURN ROLE NAME
                    editedRole = x.editorRole != null
                        ? x.editorRole.RoleName
                        : "System",

                    taskCode = x.taskData != null
                        ? x.taskData.TaskCode
                        : null,

                    taskName = x.taskData != null
                        ? x.taskData.Task
                        : null,

                    changeDateTime = x.audit.ChangeDateandTime
                })
                .ToListAsync();

            return Ok(auditLogs);
        }

        [HttpGet("getdepartments")]
        public async Task<IActionResult> GetDepartments()
        {
            var departments = await _context.Departments.ToListAsync();
            return Ok(departments);
        }


        [HttpPost("adddepartment")]
        public async Task<IActionResult> AddDepartment([FromBody] Department department)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Check if department already exists
            var existingDepartment = await _context.Departments
                .FirstOrDefaultAsync(d =>
                    d.DepartmentName.ToLower() == department.DepartmentName.ToLower());

            if (existingDepartment != null)
            {
                return Conflict(new
                {
                    message = "Department already registered"
                });
            }

            _context.Departments.Add(department);

            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetDepartments),
                new { id = department.Id },
                department
            );
        }
      
        
        // GET: api/roles
        [HttpGet("getall-roles")]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _context.Roles
                .OrderBy(r => r.Id)
                .ToListAsync();

            return Ok(roles);
        }

   
        [HttpPost("addnewrole")]
        public async Task<IActionResult> AddRole([FromBody] Roles role)
        {
            if (string.IsNullOrWhiteSpace(role.RoleName))
            {
                return BadRequest(new
                {
                    message = "Role name is required"
                });
            }

            // Check duplicate role name
            var exists = await _context.Roles
                .AnyAsync(r =>
                    r.RoleName.ToLower() == role.RoleName.Trim().ToLower());

            if (exists)
            {
                return Conflict(new
                {
                    message = "Role already exists"
                });
            }

            // Validate position
            if (role.Position < 1)
            {
                return BadRequest(new
                {
                    message = "Position must be greater than 0"
                });
            }

            // Check maximum position
            var maxPosition = await _context.Roles
                .MaxAsync(r => (int?)r.Position) ?? 0;

            if (role.Position > maxPosition + 1)
            {
                return BadRequest(new
                {
                    message = $"Position must be between 1 and {maxPosition + 1}"
                });
            }

            // Move existing roles down
            var rolesToMove = await _context.Roles
                .Where(r => r.Position >= role.Position)
                .OrderByDescending(r => r.Position)
                .ToListAsync();

            foreach (var existingRole in rolesToMove)
            {
                existingRole.Position++;
            }

            role.RoleName = role.RoleName.Trim();
            role.Status = true;

            _context.Roles.Add(role);

            await _context.SaveChangesAsync();

            return Ok(role);
        }
      
        [HttpPut("editrole/{id}")]
        public async Task<IActionResult> EditRole(int id,[FromBody] Roles updatedRole)
        {
            var role = await _context.Roles.FindAsync(id);

            if (role == null)
            {
                return NotFound(new
                {
                    message = "Role not found"
                });
            }

            if (string.IsNullOrWhiteSpace(updatedRole.RoleName))
            {
                return BadRequest(new
                {
                    message = "Role name is required"
                });
            }

            if (updatedRole.Position < 1)
            {
                return BadRequest(new
                {
                    message = "Position must be greater than 0"
                });
            }

            // Check duplicate name
            var duplicate = await _context.Roles
                .AnyAsync(r =>
                    r.Id != id &&
                    r.RoleName.ToLower() ==
                    updatedRole.RoleName.Trim().ToLower());

            if (duplicate)
            {
                return Conflict(new
                {
                    message = "Another role with this name already exists"
                });
            }

            int oldPosition = role.Position;
            int newPosition = updatedRole.Position;

            // Find maximum position excluding current role
            var maxPosition = await _context.Roles
                .Where(r => r.Id != id)
                .MaxAsync(r => (int?)r.Position) ?? 0;

            if (newPosition > maxPosition + 1)
            {
                return BadRequest(new
                {
                    message = $"Position must be between 1 and {maxPosition + 1}"
                });
            }

            // Position changed
            if (oldPosition != newPosition)
            {
                if (newPosition < oldPosition)
                {
                    // Moving UP
                    // Example:
                    // 4 → 2
                    //
                    // 2 → 3
                    // 3 → 4

                    var rolesToMove = await _context.Roles
                        .Where(r =>
                            r.Id != id &&
                            r.Position >= newPosition &&
                            r.Position < oldPosition)
                        .OrderByDescending(r => r.Position)
                        .ToListAsync();

                    foreach (var existingRole in rolesToMove)
                    {
                        existingRole.Position++;
                    }
                }
                else
                {
                    // Moving DOWN
                    // Example:
                    // 2 → 4
                    //
                    // 3 → 2
                    // 4 → 3

                    var rolesToMove = await _context.Roles
                        .Where(r =>
                            r.Id != id &&
                            r.Position > oldPosition &&
                            r.Position <= newPosition)
                        .OrderBy(r => r.Position)
                        .ToListAsync();

                    foreach (var existingRole in rolesToMove)
                    {
                        existingRole.Position--;
                    }
                }

                role.Position = newPosition;
            }

            role.RoleName = updatedRole.RoleName.Trim();
            role.Status = updatedRole.Status;

            await _context.SaveChangesAsync();

            return Ok(role);
        }
        // DELETE: api/roles/1
        [HttpDelete("deleterole/{id}")]
        public async Task<IActionResult> DeleteRole(int id)
        {
            var role = await _context.Roles.FindAsync(id);

            if (role == null)
            {
                return NotFound(new
                {
                    message = "Role not found"
                });
            }

            _context.Roles.Remove(role);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Role deleted successfully"
            });
        }



    }

}
