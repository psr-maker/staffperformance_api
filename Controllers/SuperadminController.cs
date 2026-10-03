using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff;
using staff.Services;
using staff_work_tracking.Data;
using StaffWork_Track.Services;
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
        private readonly FirebaseNotificationService _firebaseNotificationService;

        public SuperadminController(AppDbContext context, IConfiguration config, NotificationService notificationService, FirebaseNotificationService firebaseNotificationService)
        {
            _context = context;
            _config = config;
            _notific = notificationService;
            _firebaseNotificationService = firebaseNotificationService;
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
                .Where(u => u.Role == "3")
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
                    u.Role,
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
                u.Department == admin.Department 
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
            // =====================================================
            // 1. GET LOGGED-IN USER
            // =====================================================

            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            if (!int.TryParse(userIdClaim.Value, out int assignedById))
                return Unauthorized("Invalid UserId in token");

            var assignedByUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == assignedById);

            if (assignedByUser == null)
                return BadRequest("Assigned By user not found");


            // =====================================================
            // 2. VALIDATE ASSIGNED USERS
            // =====================================================

            if (dto.AssignedToIds == null || !dto.AssignedToIds.Any())
                return BadRequest("At least one Assigned To user is required.");

            var assignedUserIds = dto.AssignedToIds
                .Distinct()
                .ToList();

            var assignedToUsers = await _context.Users
                .Where(u => assignedUserIds.Contains(u.UserId))
                .ToListAsync();

            if (assignedToUsers.Count != assignedUserIds.Count)
                return BadRequest("One or more Assigned To users not found.");


            // =====================================================
            // 3. VALIDATE QUANTITY SPLITS
            // =====================================================

            if (dto.QuantitySplits != null && dto.QuantitySplits.Any())
            {
                // Every split must have quantity > 0
                if (dto.QuantitySplits.Any(s => s.Quantity <= 0))
                    return BadRequest("Share quantity must be greater than 0.");

                // Every split must have at least one member
                if (dto.QuantitySplits.Any(s =>
                    s.MemberIds == null || !s.MemberIds.Any()))
                {
                    return BadRequest(
                        "Every quantity share must have at least one member.");
                }

                // All split member IDs must belong to this task
                var allSplitMemberIds = dto.QuantitySplits
                    .SelectMany(s => s.MemberIds)
                    .Distinct()
                    .ToList();

                if (allSplitMemberIds.Any(id => !assignedUserIds.Contains(id)))
                {
                    return BadRequest(
                        "Quantity split contains a member who is not assigned to this task.");
                }

           

                // Split total must equal task quantity
                var splitTotal = dto.QuantitySplits.Sum(s => s.Quantity);

                if (dto.Quantity.HasValue &&
                    splitTotal != dto.Quantity.Value)
                {
                    return BadRequest(
                        $"Share quantities must equal the task quantity ({dto.Quantity.Value}).");
                }
            }


            // =====================================================
            // 4. VALIDATE GOAL
            // =====================================================

            Goal? goal = null;

            if (!string.IsNullOrWhiteSpace(dto.GoalCode))
            {
                goal = await _context.Goal
                    .FirstOrDefaultAsync(g => g.GoalCode == dto.GoalCode);

                if (goal == null)
                    return BadRequest("Goal not found.");
            }


            // =====================================================
            // 5. GENERATE TASK CODE
            // =====================================================

            int nextTaskId =
                (await _context.Tasks.MaxAsync(t => (int?)t.Id) ?? 0) + 1;

            string taskCode = "T" + nextTaskId;


            // =====================================================
            // 6. CREATE TASK
            // =====================================================

            var task = new TaskTable
            {
                TaskCode = taskCode,

                Task = dto.Task,

                GoalCode = dto.GoalCode,

                Description = dto.Description,

                Priority = dto.Priority,

                Status = "Not Started",

                Created_At = dto.Start_date,

                Due_Date = dto.Due_Date,

                Members = assignedToUsers.Count,

                PerformanceType = dto.PerformanceType,

                // IMPORTANT:
                // This is the TOTAL task quantity.
                // Example: 90
                Quantity = dto.Quantity,

                // Starts from zero
                CompletedQuantity = 0,

                StartTime = dto.StartTime,

                EndTime = dto.EndTime
            };

            _context.Tasks.Add(task);

            await _context.SaveChangesAsync();


            // =====================================================
            // 7. CREATE TASK MEMBERS
            // =====================================================

            int tmCounter =
                (await _context.TaskMembers.MaxAsync(tm => (int?)tm.Id) ?? 0) + 1;

            var createdMembers = new List<TaskMember>();

            foreach (var user in assignedToUsers)
            {
                var member = new TaskMember
                {
                    TMCode = "TM" + tmCounter++,

                    TaskCode = task.TaskCode,

                    Assign_To =
                        $"{user.UserId}-{user.Name}",

                    Assign_By =
                        $"{assignedByUser.UserId}-{assignedByUser.Name}",

                    UserStatus = "Not Started",

                    Assigned_At = DateTime.Now,

                    // Will be assigned below
                    SplitId = null
                };

                _context.TaskMembers.Add(member);

                createdMembers.Add(member);
            }

            // Save members so they are tracked properly
            await _context.SaveChangesAsync();


            // =====================================================
            // 8. CREATE QUANTITY SHARES
            // =====================================================

            var splitResponse = new List<object>();

            if (dto.QuantitySplits != null &&
                dto.QuantitySplits.Any())
            {
                foreach (var split in dto.QuantitySplits)
                {
                    // ---------------------------------------------
                    // Create one share
                    // ---------------------------------------------

                    var row = new TaskQuantitySplit
                    {
                        TaskCode = task.TaskCode,

                        Quantity = split.Quantity,

                        CompletedQuantity = 0
                    };

                    _context.TaskQuantitySplit.Add(row);

                    // Need Id before assigning SplitId
                    await _context.SaveChangesAsync();


                    // ---------------------------------------------
                    // Attach members to this share
                    // ---------------------------------------------

                    foreach (var member in createdMembers)
                    {
                        var userIdText = member.Assign_To.Split('-')[0];

                        if (!int.TryParse(userIdText, out int memberUserId))
                            continue;

                        if (split.MemberIds.Contains(memberUserId))
                        {
                            member.SplitId = row.Id;
                        }
                    }


                    // ---------------------------------------------
                    // Response
                    // ---------------------------------------------

                    splitResponse.Add(new
                    {
                        id = row.Id,

                        quantity = row.Quantity,

                        completedQuantity = row.CompletedQuantity,

                        memberIds = split.MemberIds
                    });
                }

                // Save SplitId changes
                await _context.SaveChangesAsync();
            }


            // =====================================================
            // 9. UPDATE GOAL STATUS / PROGRESS
            // =====================================================

            if (goal != null)
            {
                var goalTasks = await _context.Tasks
                    .Where(t => t.GoalCode == goal.GoalCode)
                    .ToListAsync();

                int total = goalTasks.Count;

                int completed = goalTasks.Count(t =>
                    !string.IsNullOrWhiteSpace(t.Status) &&
                    t.Status.Trim().Equals(
                        "completed",
                        StringComparison.OrdinalIgnoreCase));

                int notStarted = goalTasks.Count(t =>
                    !string.IsNullOrWhiteSpace(t.Status) &&
                    t.Status.Trim().Equals(
                        "not started",
                        StringComparison.OrdinalIgnoreCase));

                if (total > 0 && completed == total)
                {
                    goal.Status = "completed";
                    goal.Completed_Date = DateTime.Now;
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

                goal.Progress =
                    total == 0
                        ? 0
                        : (int)(((double)completed / total) * 100);
            }

            await _context.SaveChangesAsync();


            // =====================================================
            // 10. SEND TASK ASSIGNED NOTIFICATION
            // =====================================================

            foreach (var user in assignedToUsers)
            {
                if (!string.IsNullOrWhiteSpace(user.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            user.FcmToken,
                            "New Task Assigned",
                            $"A new task '{task.Task}' has been assigned to you by {assignedByUser.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"FCM Error for user {user.UserId}: {ex}");
                    }
                }
            }


            // =====================================================
            // 11. NOTIFY IMMEDIATE HIGHER POSITION
            // =====================================================

            var creatorRoleInfo = await _context.Roles
                .FirstOrDefaultAsync(r =>
                    r.RoleName == assignedByUser.Role);

            if (creatorRoleInfo != null)
            {
                var upperRole = await _context.Roles
                    .Where(r =>
                        r.Status &&
                        r.Position < creatorRoleInfo.Position)
                    .OrderByDescending(r => r.Position)
                    .FirstOrDefaultAsync();

                if (upperRole != null)
                {
                    var upperUsers = await _context.Users
                        .Where(u =>
                            u.Department == assignedByUser.Department &&
                            u.Role == upperRole.RoleName &&
                            !string.IsNullOrWhiteSpace(u.FcmToken))
                        .ToListAsync();

                    foreach (var upperUser in upperUsers)
                    {
                        try
                        {
                            await _firebaseNotificationService
                                .SendNotificationAsync(
                                    upperUser.FcmToken!,
                                    "New Task Created",
                                    $"{assignedByUser.Name} created a new task '{task.Task}'"
                                );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Upper Position FCM Error ({upperUser.UserId}): {ex}");
                        }
                    }
                }
            }


            // =====================================================
            // 12. ASSIGNED USERS RESPONSE
            // =====================================================

            var assignedUsersResponse = assignedToUsers
                .Select(user => new
                {
                    userId = user.UserId,
                    name = user.Name,
                    department = user.Department,
                    role = user.Role
                })
                .ToList();


            // =====================================================
            // 13. FINAL RESPONSE
            // =====================================================

            return Ok(new
            {
                message = "Task created successfully",

                task = new
                {
                    id = task.Id,

                    taskCode = task.TaskCode,

                    task = task.Task,

                    goalCode = task.GoalCode,

                    description = task.Description,

                    priority = task.Priority,

                    status = task.Status,

                    startDate = task.Created_At,

                    dueDate = task.Due_Date,

                    performanceType = task.PerformanceType,

                    // IMPORTANT:
                    // Always the TOTAL task quantity.
                    quantity = task.Quantity,

                    completedQuantity = task.CompletedQuantity,

                    startTime = task.StartTime,

                    endTime = task.EndTime,

                    members = task.Members,

                    assignedBy = new
                    {
                        userId = assignedByUser.UserId,
                        name = assignedByUser.Name,
                        department = assignedByUser.Department,
                        role = assignedByUser.Role
                    },

                    assignedToIds = assignedUserIds,

                    assignedUsers = assignedUsersResponse,

                    // NEW
                    quantitySplits = splitResponse
                }
            });
        }


        [Authorize]
        [HttpPost("CreateGoal")]
        public async Task<IActionResult> CreateGoal([FromBody] CreateGoalRequest model)
        {
            if (model == null)
                return BadRequest("Invalid data.");

            // ============================================================
            // 1. GET LOGGED-IN USER
            // ============================================================

            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrWhiteSpace(userIdClaim))
                return Unauthorized("Invalid token.");

            if (!int.TryParse(userIdClaim, out int creatorId))
                return BadRequest("Invalid creator ID.");

            // ============================================================
            // 2. GET CREATOR
            // ============================================================

            var creator = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == creatorId);

            if (creator == null)
                return BadRequest("Creator not found.");

            // ============================================================
            // 3. COMMON VALIDATION
            // ============================================================

            if (string.IsNullOrWhiteSpace(model.GoalType))
                return BadRequest("GoalType is required.");

            if (string.IsNullOrWhiteSpace(model.Title))
                return BadRequest("Goal title is required.");

            if (model.StartDate > model.DueDate)
                return BadRequest("Start date cannot be after due date.");

            var goalType = model.GoalType.Trim();

            // ============================================================
            // 4. CREATE YEARLY GOAL
            // ============================================================

            if (goalType.Equals("Yearly", StringComparison.OrdinalIgnoreCase))
            {
                // --------------------------------------------------------
                // OPTIONAL YEARLY QUANTITY
                // --------------------------------------------------------

                if (model.TargetQuantity.HasValue &&
                    model.TargetQuantity.Value <= 0)
                {
                    return BadRequest(
                        "Yearly targetQuantity must be greater than 0 when quantity is provided.");
                }

                // --------------------------------------------------------
                // MONTHLY GOALS REQUIRED
                // --------------------------------------------------------

                if (model.MonthlyGoals == null ||
                    model.MonthlyGoals.Count == 0)
                {
                    return BadRequest(
                        "At least one monthly goal is required for a yearly goal.");
                }

                // --------------------------------------------------------
                // VALIDATE MONTHLY QUANTITIES
                // --------------------------------------------------------

                long monthlyTargetTotal = 0;

                foreach (var monthly in model.MonthlyGoals)
                {
                    if (string.IsNullOrWhiteSpace(monthly.Title))
                    {
                        return BadRequest(
                            "Monthly goal title cannot be empty.");
                    }

                    if (monthly.StartDate > monthly.DueDate)
                    {
                        return BadRequest(
                            $"Invalid dates for monthly goal: {monthly.Title}");
                    }

                    // Quantity is OPTIONAL
                    if (monthly.TargetQuantity.HasValue)
                    {
                        if (monthly.TargetQuantity.Value <= 0)
                        {
                            return BadRequest(
                                $"TargetQuantity for monthly goal '{monthly.Title}' must be greater than 0 when quantity is provided.");
                        }

                        monthlyTargetTotal += monthly.TargetQuantity.Value;
                    }

                    // Assignment is REQUIRED
                    if (monthly.AssignedUserIds == null ||
                        monthly.AssignedUserIds.Count == 0)
                    {
                        return BadRequest(
                            $"At least one user must be assigned to monthly goal: {monthly.Title}");
                    }
                }

                // --------------------------------------------------------
                // CHECK MONTHLY TOTAL AGAINST YEARLY TOTAL
                // ONLY WHEN YEARLY QUANTITY IS PROVIDED
                // --------------------------------------------------------

                if (model.TargetQuantity.HasValue &&
                    monthlyTargetTotal > model.TargetQuantity.Value)
                {
                    return BadRequest(
                        $"Total monthly target quantity ({monthlyTargetTotal}) cannot be greater than yearly target quantity ({model.TargetQuantity.Value}).");
                }

                // --------------------------------------------------------
                // GENERATE YEARLY GOAL CODE
                // --------------------------------------------------------

                var lastYearlyGoal = await _context.Goal
                    .Where(g => g.GoalType == "Yearly")
                    .OrderByDescending(g => g.Id)
                    .FirstOrDefaultAsync();

                int nextYearlyNumber = 1;

                if (lastYearlyGoal != null &&
                    !string.IsNullOrWhiteSpace(lastYearlyGoal.GoalCode))
                {
                    var code = lastYearlyGoal.GoalCode;

                    if (code.StartsWith("YG") &&
                        int.TryParse(code.Substring(2), out int lastNumber))
                    {
                        nextYearlyNumber = lastNumber + 1;
                    }
                }

                // --------------------------------------------------------
                // GENERATE MONTHLY GOAL START NUMBER
                // --------------------------------------------------------

                var lastMonthlyGoal = await _context.Goal
                    .Where(g => g.GoalType == "Monthly")
                    .OrderByDescending(g => g.Id)
                    .FirstOrDefaultAsync();

                int nextMonthlyNumber = 1;

                if (lastMonthlyGoal != null &&
                    !string.IsNullOrWhiteSpace(lastMonthlyGoal.GoalCode))
                {
                    var code = lastMonthlyGoal.GoalCode;

                    if (code.StartsWith("MG") &&
                        int.TryParse(code.Substring(2), out int lastNumber))
                    {
                        nextMonthlyNumber = lastNumber + 1;
                    }
                }

                // --------------------------------------------------------
                // GET ALL ASSIGNED USERS
                // --------------------------------------------------------

                var allAssignedUserIds = model.MonthlyGoals
                    .SelectMany(m => m.AssignedUserIds ?? new List<int>())
                    .Distinct()
                    .ToList();

                if (allAssignedUserIds.Count == 0)
                {
                    return BadRequest(
                        "At least one user must be assigned to the monthly goals.");
                }

                // --------------------------------------------------------
                // GET USERS
                // --------------------------------------------------------

                var assignedUsers = await _context.Users
                    .Where(u => allAssignedUserIds.Contains(u.UserId))
                    .ToListAsync();

                // --------------------------------------------------------
                // CHECK MISSING USERS
                // --------------------------------------------------------

                var missingUserIds = allAssignedUserIds
                    .Except(assignedUsers.Select(u => u.UserId))
                    .ToList();

                if (missingUserIds.Any())
                {
                    return BadRequest(new
                    {
                        message = "One or more assigned users were not found.",
                        userIds = missingUserIds
                    });
                }

                // --------------------------------------------------------
                // DEPARTMENT VALIDATION
                // --------------------------------------------------------

                foreach (var user in assignedUsers)
                {
                    if (!string.Equals(
                            user.Department,
                            creator.Department,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(
                            $"User {user.UserId} does not belong to the creator's department.");
                    }
                }

                // ========================================================
                // TRANSACTION
                // ========================================================

                using var transaction =
                    await _context.Database.BeginTransactionAsync();

                try
                {
                    // ====================================================
                    // CREATE YEARLY GOAL
                    // ====================================================

                    var yearlyGoal = new Goal
                    {
                        GoalCode = $"YG{nextYearlyNumber:D3}",
                        GoalType = "Yearly",

                        ParentGoalId = null,

                        Title = model.Title,
                        Priority = model.Priority,

                        StartDate = model.StartDate,
                        DueDate = model.DueDate,

                        Completed_Date = null,

                        Status = "Not Started",
                        Progress = 0,

                        Goalpoints = 0,

                        // OPTIONAL QUANTITY
                        TargetQuantity = model.TargetQuantity,
                        CompletedQuantity = model.TargetQuantity.HasValue
                            ? 0
                            : null,

                        CreatedBy = creatorId
                    };

                    _context.Goal.Add(yearlyGoal);

                    await _context.SaveChangesAsync();

                    // ====================================================
                    // CREATE MONTHLY GOALS
                    // ====================================================

                    var createdMonthlyGoals = new List<Goal>();

                    foreach (var monthly in model.MonthlyGoals)
                    {
                        var monthlyGoal = new Goal
                        {
                            GoalCode = $"MG{nextMonthlyNumber:D3}",
                            GoalType = "Monthly",

                            ParentGoalId = yearlyGoal.Id,

                            Title = monthly.Title,
                            Priority = monthly.Priority,

                            StartDate = monthly.StartDate,
                            DueDate = monthly.DueDate,

                            Completed_Date = null,

                            Status = "Not Started",
                            Progress = 0,

                            Goalpoints = 0,

                            // OPTIONAL QUANTITY
                            TargetQuantity = monthly.TargetQuantity,
                            CompletedQuantity = monthly.TargetQuantity.HasValue
                                ? 0
                                : null,

                            CreatedBy = creatorId
                        };

                        _context.Goal.Add(monthlyGoal);

                        await _context.SaveChangesAsync();

                        createdMonthlyGoals.Add(monthlyGoal);

                        // ------------------------------------------------
                        // CREATE ASSIGNMENTS
                        // ------------------------------------------------

                        var uniqueUserIds = monthly.AssignedUserIds
                            .Distinct()
                            .ToList();

                        foreach (var assignedUserId in uniqueUserIds)
                        {
                            var assignment = new GoalAssignment
                            {
                                GoalId = monthlyGoal.Id,
                                UserId = assignedUserId
                            };

                            _context.GoalAssignment.Add(assignment);
                        }

                        await _context.SaveChangesAsync();

                        nextMonthlyNumber++;
                    }

                    // ====================================================
                    // COMMIT
                    // ====================================================

                    await transaction.CommitAsync();

                    // ====================================================
                    // SUCCESS RESPONSE
                    // ====================================================

                    return Ok(new
                    {
                        message =
                            "Yearly goal and monthly goals created successfully.",

                        yearlyGoal = new
                        {
                            id = yearlyGoal.Id,
                            goalCode = yearlyGoal.GoalCode,
                            title = yearlyGoal.Title,
                            goalType = yearlyGoal.GoalType,

                            targetQuantity = yearlyGoal.TargetQuantity,
                            completedQuantity = yearlyGoal.CompletedQuantity,

                            pendingQuantity =
                                yearlyGoal.TargetQuantity.HasValue
                                    ? yearlyGoal.TargetQuantity.Value -
                                      (yearlyGoal.CompletedQuantity ?? 0)
                                    : (int?)null
                        },

                        monthlyGoals = createdMonthlyGoals.Select(g => new
                        {
                            id = g.Id,
                            goalCode = g.GoalCode,
                            title = g.Title,

                            priority = g.Priority,
                            goalType = g.GoalType,
                            parentGoalId = g.ParentGoalId,

                            targetQuantity = g.TargetQuantity,
                            completedQuantity = g.CompletedQuantity,

                            pendingQuantity =
                                g.TargetQuantity.HasValue
                                    ? g.TargetQuantity.Value -
                                      (g.CompletedQuantity ?? 0)
                                    : (int?)null
                        })
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    return StatusCode(500, new
                    {
                        message = "Failed to create yearly goal.",
                        error = ex.Message
                    });
                }
            }

            // ============================================================
            // 5. CREATE STANDALONE MONTHLY GOAL
            // ============================================================

            if (goalType.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
            {
                // --------------------------------------------------------
                // OPTIONAL QUANTITY
                // --------------------------------------------------------

                if (model.TargetQuantity.HasValue &&
                    model.TargetQuantity.Value <= 0)
                {
                    return BadRequest(
                        "Monthly targetQuantity must be greater than 0 when quantity is provided.");
                }

                // --------------------------------------------------------
                // ASSIGNMENT REQUIRED
                // --------------------------------------------------------

                if (model.AssignedUserIds == null ||
                    model.AssignedUserIds.Count == 0)
                {
                    return BadRequest(
                        "At least one user must be assigned to the monthly goal.");
                }

                // --------------------------------------------------------
                // GET PARENT YEARLY GOAL
                // --------------------------------------------------------

                Goal? parentGoal = null;

                if (model.ParentGoalId.HasValue)
                {
                    parentGoal = await _context.Goal
                        .FirstOrDefaultAsync(g =>
                            g.Id == model.ParentGoalId.Value);

                    if (parentGoal == null)
                    {
                        return BadRequest(
                            "The selected parent goal was not found.");
                    }

                    if (!string.Equals(
                            parentGoal.GoalType,
                            "Yearly",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(
                            "A monthly goal can only have a Yearly parent goal.");
                    }

                    // ----------------------------------------------------
                    // CHECK QUANTITY ONLY IF BOTH HAVE QUANTITY
                    // ----------------------------------------------------

                    if (model.TargetQuantity.HasValue &&
                        parentGoal.TargetQuantity.HasValue)
                    {
                        var existingMonthlyQuantity = await _context.Goal
                            .Where(g =>
                                g.ParentGoalId == parentGoal.Id &&
                                g.GoalType == "Monthly" &&
                                g.TargetQuantity.HasValue)
                            .SumAsync(g => (long?)g.TargetQuantity ?? 0);

                        var newTotal =
                            existingMonthlyQuantity +
                            model.TargetQuantity.Value;

                        if (newTotal > parentGoal.TargetQuantity.Value)
                        {
                            return BadRequest(
                                $"Monthly target quantity would exceed the parent yearly target. " +
                                $"Yearly target: {parentGoal.TargetQuantity.Value}, " +
                                $"existing monthly target: {existingMonthlyQuantity}, " +
                                $"new monthly target: {model.TargetQuantity.Value}.");
                        }
                    }
                }

                // --------------------------------------------------------
                // GET ASSIGNED USERS
                // --------------------------------------------------------

                var assignedUserIds = model.AssignedUserIds
                    .Distinct()
                    .ToList();

                var assignedUsers = await _context.Users
                    .Where(u => assignedUserIds.Contains(u.UserId))
                    .ToListAsync();

                // --------------------------------------------------------
                // CHECK MISSING USERS
                // --------------------------------------------------------

                var missingUserIds = assignedUserIds
                    .Except(assignedUsers.Select(u => u.UserId))
                    .ToList();

                if (missingUserIds.Any())
                {
                    return BadRequest(new
                    {
                        message = "One or more assigned users were not found.",
                        userIds = missingUserIds
                    });
                }

                // --------------------------------------------------------
                // DEPARTMENT VALIDATION
                // --------------------------------------------------------

                foreach (var user in assignedUsers)
                {
                    if (!string.Equals(
                            user.Department,
                            creator.Department,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(
                            $"User {user.UserId} does not belong to the creator's department.");
                    }
                }

                // --------------------------------------------------------
                // GENERATE MONTHLY CODE
                // --------------------------------------------------------

                var lastMonthlyGoal = await _context.Goal
                    .Where(g => g.GoalType == "Monthly")
                    .OrderByDescending(g => g.Id)
                    .FirstOrDefaultAsync();

                int nextMonthlyNumber = 1;

                if (lastMonthlyGoal != null &&
                    !string.IsNullOrWhiteSpace(lastMonthlyGoal.GoalCode))
                {
                    var code = lastMonthlyGoal.GoalCode;

                    if (code.StartsWith("MG") &&
                        int.TryParse(code.Substring(2), out int lastNumber))
                    {
                        nextMonthlyNumber = lastNumber + 1;
                    }
                }

                // ========================================================
                // TRANSACTION
                // ========================================================

                using var monthlyTransaction =
                    await _context.Database.BeginTransactionAsync();

                try
                {
                    // ====================================================
                    // CREATE MONTHLY GOAL
                    // ====================================================

                    var monthlyGoal = new Goal
                    {
                        GoalCode = $"MG{nextMonthlyNumber:D3}",
                        GoalType = "Monthly",

                        ParentGoalId = model.ParentGoalId,

                        Title = model.Title,
                        Priority = model.Priority,

                        StartDate = model.StartDate,
                        DueDate = model.DueDate,

                        Completed_Date = null,

                        Status = "Not Started",
                        Progress = 0,

                        Goalpoints = 0,

                        // OPTIONAL QUANTITY
                        TargetQuantity = model.TargetQuantity,
                        CompletedQuantity = model.TargetQuantity.HasValue
                            ? 0
                            : null,

                        CreatedBy = creatorId
                    };

                    _context.Goal.Add(monthlyGoal);

                    await _context.SaveChangesAsync();

                    // ====================================================
                    // CREATE ASSIGNMENTS
                    // ====================================================

                    foreach (var userId in assignedUserIds)
                    {
                        var assignment = new GoalAssignment
                        {
                            GoalId = monthlyGoal.Id,
                            UserId = userId
                        };

                        _context.GoalAssignment.Add(assignment);
                    }

                    await _context.SaveChangesAsync();

                    // ====================================================
                    // COMMIT
                    // ====================================================

                    await monthlyTransaction.CommitAsync();

                    // ====================================================
                    // SUCCESS RESPONSE
                    // ====================================================

                    return Ok(new
                    {
                        message = "Monthly goal created successfully.",

                        goal = new
                        {
                            id = monthlyGoal.Id,
                            goalCode = monthlyGoal.GoalCode,
                            goalType = monthlyGoal.GoalType,
                            parentGoalId = monthlyGoal.ParentGoalId,

                            title = monthlyGoal.Title,
                            priority = monthlyGoal.Priority,

                            startDate = monthlyGoal.StartDate,
                            dueDate = monthlyGoal.DueDate,

                            status = monthlyGoal.Status,
                            createdBy = monthlyGoal.CreatedBy,

                            targetQuantity = monthlyGoal.TargetQuantity,
                            completedQuantity = monthlyGoal.CompletedQuantity,

                            pendingQuantity =
                                monthlyGoal.TargetQuantity.HasValue
                                    ? monthlyGoal.TargetQuantity.Value -
                                      (monthlyGoal.CompletedQuantity ?? 0)
                                    : (int?)null
                        }
                    });
                }
                catch (Exception ex)
                {
                    await monthlyTransaction.RollbackAsync();

                    return StatusCode(500, new
                    {
                        message = "Failed to create monthly goal.",
                        error = ex.Message
                    });
                }
            }

            // ============================================================
            // 6. INVALID GOAL TYPE
            // ============================================================

            return BadRequest(
                "Invalid GoalType. Allowed values are Yearly or Monthly.");
        }

        [Authorize]
        [HttpPut("UpdateGoal/{code}")]
        public async Task<IActionResult> UpdateGoal(string code,[FromBody] UpdateGoalDto model)
        {
            if (model == null)
                return BadRequest("Invalid data.");

            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("Goal code is required.");

            // =========================================================
            // 1. GET LOGGED-IN USER
            // =========================================================

            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim.Value, out int editorId))
                return BadRequest("Invalid user ID.");

            string editorRole = roleClaim?.Value ?? "Unknown";

            var editor = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == editorId);

            if (editor == null)
                return BadRequest("Editor not found.");

            // =========================================================
            // 2. FIND GOAL
            // =========================================================

            var goal = await _context.Goal
                .FirstOrDefaultAsync(g => g.GoalCode == code);

            if (goal == null)
                return NotFound("Goal not found.");

            // =========================================================
            // 3. GET CURRENT ASSIGNED USERS
            // =========================================================

            var oldAssignedUserIds = await _context.GoalAssignment
                .Where(a => a.GoalId == goal.Id)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync();

            // =========================================================
            // 4. STORE OLD VALUES FOR AUDIT
            // =========================================================

            var oldTitle = goal.Title;
            var oldPriority = goal.Priority;
            var oldDueDate = goal.DueDate;
            var oldTargetQuantity = goal.TargetQuantity;

            // =========================================================
            // 5. VALIDATE TITLE
            // =========================================================

            if (model.Title != null)
            {
                if (string.IsNullOrWhiteSpace(model.Title))
                {
                    return BadRequest("Goal title cannot be empty.");
                }

                goal.Title = model.Title.Trim();
            }

            // =========================================================
            // 6. UPDATE PRIORITY
            // =========================================================

            if (model.Priority != null)
            {
                goal.Priority = string.IsNullOrWhiteSpace(model.Priority)
                    ? null
                    : model.Priority.Trim();
            }

            // =========================================================
            // 7. UPDATE DUE DATE
            // =========================================================

            if (model.DueDate.HasValue)
            {
                if (model.DueDate.Value < goal.StartDate)
                {
                    return BadRequest(
                        "Due date cannot be earlier than the goal start date.");
                }

                goal.DueDate = model.DueDate.Value;
            }

            if (model.TargetQuantity.HasValue)
            {
                if (model.TargetQuantity.Value <= 0)
                {
                    return BadRequest(
                        "TargetQuantity must be greater than 0 when quantity is provided.");
                }

                var completedQuantity = goal.CompletedQuantity ?? 0;

                if (model.TargetQuantity.Value < completedQuantity)
                {
                    return BadRequest(
                        $"Target quantity cannot be less than completed quantity ({completedQuantity}).");
                }

                // ---------------------------------------------------------
                // If this is a monthly goal with yearly parent,
                // validate against yearly quantity when available.
                // ---------------------------------------------------------

                if (goal.GoalType.Equals(
                        "Monthly",
                        StringComparison.OrdinalIgnoreCase) &&
                    goal.ParentGoalId.HasValue)
                {
                    var parentGoal = await _context.Goal
                        .FirstOrDefaultAsync(g =>
                            g.Id == goal.ParentGoalId.Value);

                    if (parentGoal != null &&
                        parentGoal.TargetQuantity.HasValue)
                    {
                        // Get other monthly goals under same yearly goal
                        var otherMonthlyTotal = await _context.Goal
                            .Where(g =>
                                g.ParentGoalId == parentGoal.Id &&
                                g.GoalType == "Monthly" &&
                                g.Id != goal.Id &&
                                g.TargetQuantity.HasValue)
                            .SumAsync(g => (long?)g.TargetQuantity ?? 0);

                        var newMonthlyTotal =
                            otherMonthlyTotal +
                            model.TargetQuantity.Value;

                        if (newMonthlyTotal > parentGoal.TargetQuantity.Value)
                        {
                            return BadRequest(
                                $"Monthly target quantity would exceed the yearly target. " +
                                $"Yearly target: {parentGoal.TargetQuantity.Value}, " +
                                $"other monthly quantity: {otherMonthlyTotal}, " +
                                $"new monthly quantity: {model.TargetQuantity.Value}.");
                        }
                    }
                }

                goal.TargetQuantity = model.TargetQuantity.Value;
            }

          

            bool assignmentChanged = false;

            if (model.AssignedUserIds != null)
            {
                var newAssignedUserIds = model.AssignedUserIds
                    .Distinct()
                    .ToList();

                // ---------------------------------------------------------
                // Staff is required
                // ---------------------------------------------------------

                if (newAssignedUserIds.Count == 0)
                {
                    return BadRequest(
                        "At least one staff member must be assigned to the goal.");
                }

                // ---------------------------------------------------------
                // Get selected users
                // ---------------------------------------------------------

                var newAssignedUsers = await _context.Users
                    .Where(u => newAssignedUserIds.Contains(u.UserId))
                    .ToListAsync();

                // ---------------------------------------------------------
                // Check missing users
                // ---------------------------------------------------------

                var missingUserIds = newAssignedUserIds
                    .Except(newAssignedUsers.Select(u => u.UserId))
                    .ToList();

                if (missingUserIds.Any())
                {
                    return BadRequest(new
                    {
                        message = "One or more assigned users were not found.",
                        userIds = missingUserIds
                    });
                }

                // ---------------------------------------------------------
                // Department validation
                // ---------------------------------------------------------

                foreach (var user in newAssignedUsers)
                {
                    if (!string.Equals(
                            user.Department,
                            editor.Department,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(
                            $"User {user.UserId} does not belong to the editor's department.");
                    }
                }

                // ---------------------------------------------------------
                // Check whether assignment actually changed
                // ---------------------------------------------------------

                assignmentChanged =
                    !oldAssignedUserIds
                        .OrderBy(x => x)
                        .SequenceEqual(
                            newAssignedUserIds.OrderBy(x => x));

                // ---------------------------------------------------------
                // Replace assignments
                // ---------------------------------------------------------

                if (assignmentChanged)
                {
                    var existingAssignments = await _context.GoalAssignment
                        .Where(a => a.GoalId == goal.Id)
                        .ToListAsync();

                    _context.GoalAssignment.RemoveRange(existingAssignments);

                    foreach (var assignedUserId in newAssignedUserIds)
                    {
                        _context.GoalAssignment.Add(
                            new GoalAssignment
                            {
                                GoalId = goal.Id,
                                UserId = assignedUserId
                            });
                    }
                }
            }

            // =========================================================
            // 10. CHECK WHAT CHANGED
            // =========================================================

            bool titleChanged =
                oldTitle != goal.Title;

            bool priorityChanged =
                oldPriority != goal.Priority;

            bool dueDateChanged =
                oldDueDate != goal.DueDate;

            bool quantityChanged =
                oldTargetQuantity != goal.TargetQuantity;

            if (!titleChanged &&
                !priorityChanged &&
                !dueDateChanged &&
                !quantityChanged &&
                !assignmentChanged)
            {
                return BadRequest("No changes were made.");
            }

            // =========================================================
            // 11. BUILD AUDIT INFORMATION
            // =========================================================

            var changedFields = new List<string>();

            if (titleChanged)
                changedFields.Add("Title");

            if (priorityChanged)
                changedFields.Add("Priority");

            if (dueDateChanged)
                changedFields.Add("DueDate");

            if (quantityChanged)
                changedFields.Add("TargetQuantity");

            if (assignmentChanged)
                changedFields.Add("AssignedStaff");

            // =========================================================
            // 12. OLD ASSIGNED STAFF TEXT
            // =========================================================

            var oldAssignedUsers = await _context.Users
                .Where(u => oldAssignedUserIds.Contains(u.UserId))
                .Select(u => new
                {
                    u.UserId,
                    u.Name
                })
                .ToListAsync();

            var oldStaffText = oldAssignedUsers.Count > 0
                ? string.Join(
                    ", ",
                    oldAssignedUsers.Select(
                        u => $"{u.Name} ({u.UserId})"))
                : "None";

            // =========================================================
            // 13. NEW ASSIGNED STAFF TEXT
            // =========================================================

            List<int> finalAssignedUserIds;

            if (model.AssignedUserIds != null)
            {
                finalAssignedUserIds = model.AssignedUserIds
                    .Distinct()
                    .ToList();
            }
            else
            {
                finalAssignedUserIds = oldAssignedUserIds;
            }

            var newAssignedUsersForAudit = await _context.Users
                .Where(u => finalAssignedUserIds.Contains(u.UserId))
                .Select(u => new
                {
                    u.UserId,
                    u.Name
                })
                .ToListAsync();

            var newStaffText = newAssignedUsersForAudit.Count > 0
                ? string.Join(
                    ", ",
                    newAssignedUsersForAudit.Select(
                        u => $"{u.Name} ({u.UserId})"))
                : "None";

            // =========================================================
            // 14. AUDIT LOG
            // =========================================================

            _context.Auditlog.Add(new Auditlog
            {
                EntityId = goal.GoalCode,
                EntityType = "Goal",
                Action = "Edit",

                Fieldchanged = string.Join(
                    ", ",
                    changedFields),

                Oldvalue =
                    $"Title: {oldTitle}; " +
                    $"Priority: {oldPriority}; " +
                    $"DueDate: {oldDueDate:yyyy-MM-dd}; " +
                    $"TargetQuantity: {oldTargetQuantity?.ToString() ?? "None"}; " +
                    $"AssignedStaff: {oldStaffText}",

                Newvalue =
                    $"Title: {goal.Title}; " +
                    $"Priority: {goal.Priority}; " +
                    $"DueDate: {goal.DueDate:yyyy-MM-dd}; " +
                    $"TargetQuantity: {goal.TargetQuantity?.ToString() ?? "None"}; " +
                    $"AssignedStaff: {newStaffText}",

                EditedUid = editor.UserId.ToString(),
                EditedRole = editorRole,
                ChangeDateandTime = DateTime.Now
            });

            // =========================================================
            // 15. SAVE
            // =========================================================

            await _context.SaveChangesAsync();

            // =========================================================
            // 16. NOTIFY CURRENTLY ASSIGNED STAFF
            // =========================================================

            var finalAssignedUsers = await _context.Users
                .Where(u =>
                    finalAssignedUserIds.Contains(u.UserId) &&
                    !string.IsNullOrWhiteSpace(u.FcmToken))
                .ToListAsync();

            foreach (var assignedUser in finalAssignedUsers)
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        assignedUser.FcmToken,
                        "Goal Updated",
                        $"The goal '{goal.Title}' was updated by {editor.Name}."
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"FCM Error ({assignedUser.UserId}): {ex.Message}");
                }
            }

            // =========================================================
            // 17. NOTIFY IMMEDIATE HIGHER ROLE
            // =========================================================

            if (int.TryParse(editor.Role, out int editorPosition))
            {
                var upperRole = await _context.Roles
                    .Where(r =>
                        r.Status &&
                        r.Position < editorPosition)
                    .OrderByDescending(r => r.Position)
                    .FirstOrDefaultAsync();

                if (upperRole != null)
                {
                    var upperUsers = await _context.Users
                        .Where(u =>
                            u.Department == editor.Department &&
                            u.Role == upperRole.Position.ToString() &&
                            !string.IsNullOrWhiteSpace(u.FcmToken))
                        .ToListAsync();

                    foreach (var upperUser in upperUsers)
                    {
                        try
                        {
                            await _firebaseNotificationService.SendNotificationAsync(
                                upperUser.FcmToken,
                                "Goal Updated",
                                $"{editor.Name} updated the goal '{goal.Title}'."
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Upper Position FCM Error ({upperUser.UserId}): {ex.Message}");
                        }
                    }
                }
            }

            // =========================================================
            // 18. GET FINAL ASSIGNED USERS FOR RESPONSE
            // =========================================================

            var responseAssignedUsers = await _context.Users
                .Where(u => finalAssignedUserIds.Contains(u.UserId))
                .Select(u => new
                {
                    userId = u.UserId,
                    name = u.Name,
                    email = u.Email,
                    department = u.Department
                })
                .ToListAsync();

            // =========================================================
            // 19. RESPONSE
            // =========================================================

            return Ok(new
            {
                message = "Goal updated successfully.",

                goal = new
                {
                    id = goal.Id,
                    goalCode = goal.GoalCode,
                    goalType = goal.GoalType,
                    parentGoalId = goal.ParentGoalId,

                    title = goal.Title,
                    priority = goal.Priority,

                    startDate = goal.StartDate,
                    dueDate = goal.DueDate,

                    status = goal.Status,
                    progress = goal.Progress,
                    goalpoints = goal.Goalpoints,

                    targetQuantity = goal.TargetQuantity,
                    completedQuantity = goal.CompletedQuantity,

                    pendingQuantity =
                        goal.TargetQuantity.HasValue
                            ? Math.Max(
                                0,
                                goal.TargetQuantity.Value -
                                (goal.CompletedQuantity ?? 0))
                            : (int?)null,

                    assignedUsers = responseAssignedUsers
                }
            });
        }

        [Authorize]
        [HttpDelete("DeleteGoal/{code}")]
        public async Task<IActionResult> DeleteGoal(string code)
        {
            // =========================================================
            // 1. GET LOGGED-IN USER
            // =========================================================

            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim.Value, out int editorId))
                return BadRequest("Invalid user ID.");

            string editorRole = roleClaim?.Value ?? "Unknown";

            var editor = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == editorId);

            if (editor == null)
                return BadRequest("Editor not found.");

            // =========================================================
            // 2. FIND GOAL
            // =========================================================

            var goal = await _context.Goal
                .FirstOrDefaultAsync(g => g.GoalCode == code);

            if (goal == null)
                return NotFound("Goal not found.");

            // =========================================================
            // 3. DON'T DELETE COMPLETED GOAL
            // =========================================================

            if (string.Equals(
                    goal.Status,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Cannot delete completed goal.");
            }

            var goalName = goal.Title;

            // =========================================================
            // 4. FIND RELATED GOALS
            // =========================================================

            var goalIds = new List<int>
    {
        goal.Id
    };

            // If Yearly Goal -> also delete its Monthly Goals
            if (string.Equals(
                    goal.GoalType,
                    "Yearly",
                    StringComparison.OrdinalIgnoreCase))
            {
                var monthlyGoalIds = await _context.Goal
                    .Where(g => g.ParentGoalId == goal.Id)
                    .Select(g => g.Id)
                    .ToListAsync();

                goalIds.AddRange(monthlyGoalIds);
            }

            // =========================================================
            // 5. GET ASSIGNED USERS BEFORE DELETE
            //    Needed for notification
            // =========================================================

            var assignedUserIds = await _context.GoalAssignment
                .Where(a => goalIds.Contains(a.GoalId))
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync();

            var assignedUsers = await _context.Users
                .Where(u =>
                    assignedUserIds.Contains(u.UserId) &&
                    !string.IsNullOrWhiteSpace(u.FcmToken))
                .ToListAsync();

            // =========================================================
            // 6. GET ALL TASKS FOR THESE GOALS
            // =========================================================

            var goalCodes = await _context.Goal
                .Where(g => goalIds.Contains(g.Id))
                .Select(g => g.GoalCode)
                .ToListAsync();

            var tasks = await _context.Tasks
                .Where(t => goalCodes.Contains(t.GoalCode))
                .ToListAsync();

            // =========================================================
            // 7. TRANSACTION
            // =========================================================

            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // =====================================================
                // 8. AUDIT LOG
                // =====================================================

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

                // =====================================================
                // 9. DELETE TASK MEMBERS AND TASKS
                // =====================================================

                if (tasks.Any())
                {
                    var taskCodes = tasks
                        .Select(t => t.TaskCode)
                        .ToList();

                    var taskMembers = await _context.TaskMembers
                        .Where(tm => taskCodes.Contains(tm.TaskCode))
                        .ToListAsync();

                    if (taskMembers.Any())
                    {
                        _context.TaskMembers.RemoveRange(taskMembers);
                    }

                    _context.Tasks.RemoveRange(tasks);
                }

                // =====================================================
                // 10. DELETE GOAL ASSIGNMENTS
                // =====================================================

                var goalAssignments = await _context.GoalAssignment
                    .Where(a => goalIds.Contains(a.GoalId))
                    .ToListAsync();

                if (goalAssignments.Any())
                {
                    _context.GoalAssignment.RemoveRange(goalAssignments);
                }

                // =====================================================
                // 11. DELETE MONTHLY GOALS
                // =====================================================

                if (goalIds.Count > 1)
                {
                    var monthlyGoals = await _context.Goal
                        .Where(g =>
                            goalIds.Contains(g.Id) &&
                            g.Id != goal.Id)
                        .ToListAsync();

                    if (monthlyGoals.Any())
                    {
                        _context.Goal.RemoveRange(monthlyGoals);
                    }
                }

                // =====================================================
                // 12. DELETE MAIN GOAL
                // =====================================================

                _context.Goal.Remove(goal);

                // =====================================================
                // 13. SAVE EVERYTHING
                // =====================================================

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    message = "Failed to delete goal.",
                    error = ex.Message
                });
            }

            // =========================================================
            // 14. NOTIFY ASSIGNED USERS
            // =========================================================

            foreach (var assignedUser in assignedUsers)
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        assignedUser.FcmToken,
                        "Goal Deleted",
                        $"The goal '{goalName}' assigned to you was deleted by {editor.Name}."
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"FCM Error ({assignedUser.UserId}): {ex.Message}"
                    );
                }
            }

            // =========================================================
            // 15. NOTIFY IMMEDIATE HIGHER ROLE
            // =========================================================

            if (int.TryParse(editor.Role, out int editorPosition))
            {
                var upperRole = await _context.Roles
                    .Where(r =>
                        r.Status &&
                        r.Position < editorPosition)
                    .OrderByDescending(r => r.Position)
                    .FirstOrDefaultAsync();

                if (upperRole != null)
                {
                    var upperUsers = await _context.Users
                        .Where(u =>
                            u.Department == editor.Department &&
                            u.Role == upperRole.Position.ToString() &&
                            !string.IsNullOrWhiteSpace(u.FcmToken))
                        .ToListAsync();

                    foreach (var upperUser in upperUsers)
                    {
                        try
                        {
                            await _firebaseNotificationService.SendNotificationAsync(
                                upperUser.FcmToken,
                                "Goal Deleted",
                                $"{editor.Name} deleted the goal '{goalName}'."
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Upper Position FCM Error ({upperUser.UserId}): {ex.Message}"
                            );
                        }
                    }
                }
            }

            // =========================================================
            // 16. RESPONSE
            // =========================================================

            return Ok(new
            {
                message = goal.GoalType == "Yearly"
                    ? "Yearly goal, monthly goals, assignments and tasks deleted successfully."
                    : "Goal, assignments and tasks deleted successfully."
            });
        }

        [Authorize]
        [HttpGet("GetGoals")]
        public async Task<IActionResult> GetGoals()
        {
            // =========================================================
            // 1. GET LOGGED-IN USER
            // =========================================================

            var userIdClaim = User.FindFirst("UserId");
            var roleClaim = User.FindFirst("Role");

            if (userIdClaim == null)
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim.Value, out int userId))
                return BadRequest("Invalid user ID.");

            string role = roleClaim?.Value ?? "";

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return BadRequest("User not found.");

            // =========================================================
            // 2. BASE QUERY
            // =========================================================

            IQueryable<Goal> query = _context.Goal;

            // =========================================================
            // 3. ACCESS CONTROL
            // =========================================================

            // Director
            if (role == "1")
            {
                // Director can see everything
                query = query;
            }
            // Manager
            else if (role == "3")
            {
                // Manager can see:
                // 1. Goals created by them
                // 2. Goals assigned to users
                //    in their department

                var departmentUserIds = await _context.Users
                    .Where(u => u.Department == user.Department)
                    .Select(u => u.UserId)
                    .ToListAsync();

                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => departmentUserIds.Contains(a.UserId))
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(g =>
                    g.CreatedBy == userId ||
                    assignedGoalIds.Contains(g.Id)
                );
            }
            // Other roles
            else
            {
                // User can see goals assigned to them
                var assignedGoalIds = await _context.GoalAssignment
                    .Where(a => a.UserId == userId)
                    .Select(a => a.GoalId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(g =>
                    assignedGoalIds.Contains(g.Id) ||
                    g.CreatedBy == userId
                );
            }

            // =========================================================
            // 4. GET GOALS
            // =========================================================

            var goals = await query
                .OrderByDescending(g => g.Id)
                .Select(g => new
                {
                    // Goal information
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

                    // Creator
                    createdBy = g.CreatedBy,

                    createdByName = _context.Users
                        .Where(u => u.UserId == g.CreatedBy)
                        .Select(u => u.Name)
                        .FirstOrDefault(),

                    createdByDepartment = _context.Users
                        .Where(u => u.UserId == g.CreatedBy)
                        .Select(u => u.Department)
                        .FirstOrDefault(),

                    // Parent yearly goal
                    parentGoalCode = _context.Goal
                        .Where(pg => pg.Id == g.ParentGoalId)
                        .Select(pg => pg.GoalCode)
                        .FirstOrDefault(),

                    parentGoalTitle = _context.Goal
                        .Where(pg => pg.Id == g.ParentGoalId)
                        .Select(pg => pg.Title)
                        .FirstOrDefault(),

                    // Assigned users
                    assignedUsers = _context.GoalAssignment
                        .Where(a => a.GoalId == g.Id)
                        .Join(
                            _context.Users,
                            a => a.UserId,
                            u => u.UserId,
                            (a, u) => new
                            {
                                userId = u.UserId,
                                name = u.Name,
                                email = u.Email,
                                department = u.Department,
                                role = u.Role
                            }
                        )
                        .ToList()
                })
                .ToListAsync();

            // =========================================================
            // 5. RETURN EVERYTHING
            // =========================================================

            return Ok(new
            {
                message = "Goals retrieved successfully.",
                count = goals.Count,
                goals
            });
        }

    
        [Authorize]
        [HttpGet("GetGoalsWithTasks")]
        public async Task<IActionResult> GetGoalsWithTasks()
        {
            try
            {
                // ============================================================
                // 1. GET LOGGED-IN USER
                // ============================================================

                var userIdClaim = User.FindFirst("UserId")?.Value;
                var roleClaim = User.FindFirst("Role")?.Value;

                if (!int.TryParse(userIdClaim, out int userId))
                    return Unauthorized("Invalid UserId.");

                if (!int.TryParse(roleClaim, out int role))
                    return Unauthorized("Invalid Role.");

                // ============================================================
                // 2. GET GOALS BASED ON ROLE
                // ============================================================

                List<Goal> goals;

                if (role == 1) // Director
                {
                    // Director can see all goals
                    goals = await _context.Goal
                        .OrderByDescending(g => g.Id)
                        .ToListAsync();
                }
                else
                {
                    // --------------------------------------------------------
                    // STEP 1: Get goals directly assigned to logged-in user
                    // --------------------------------------------------------

                    var assignedGoalIds = await _context.GoalAssignment
                        .Where(a => a.UserId == userId)
                        .Select(a => a.GoalId)
                        .Distinct()
                        .ToListAsync();

                    // --------------------------------------------------------
                    // STEP 2: Get those assigned goals
                    // --------------------------------------------------------

                    var assignedGoals = await _context.Goal
                        .Where(g => assignedGoalIds.Contains(g.Id))
                        .ToListAsync();

                    // --------------------------------------------------------
                    // STEP 3: Find parent yearly goals
                    // --------------------------------------------------------
                    //
                    // If user has:
                    //
                    // Monthly MG001
                    //     ParentGoalId = 10
                    //
                    // then also include Goal Id 10.
                    //
                    // --------------------------------------------------------

                    var parentGoalIds = assignedGoals
                        .Where(g => g.ParentGoalId.HasValue)
                        .Select(g => g.ParentGoalId!.Value)
                        .Distinct()
                        .ToList();

                    // --------------------------------------------------------
                    // STEP 4: Get parent yearly goals
                    // --------------------------------------------------------

                    var parentGoals = await _context.Goal
                        .Where(g => parentGoalIds.Contains(g.Id))
                        .ToListAsync();

                    // --------------------------------------------------------
                    // STEP 5: Combine assigned goals + parent yearly goals
                    // --------------------------------------------------------

                    goals = assignedGoals
                        .Concat(parentGoals)
                        .GroupBy(g => g.Id)
                        .Select(g => g.First())
                        .OrderByDescending(g => g.Id)
                        .ToList();
                }

                // ============================================================
                // 3. GET GOAL CODES
                // ============================================================

                var goalCodes = goals
                    .Where(g => !string.IsNullOrWhiteSpace(g.GoalCode))
                    .Select(g => g.GoalCode!)
                    .Distinct()
                    .ToList();

                // ============================================================
                // 4. GET TASKS
                // ============================================================

                var tasks = await _context.Tasks
                    .Where(t => goalCodes.Contains(t.GoalCode))
                    .ToListAsync();

                // ============================================================
                // 5. GET TASK MEMBERS
                // ============================================================

                var taskCodes = tasks
                    .Where(t => !string.IsNullOrWhiteSpace(t.TaskCode))
                    .Select(t => t.TaskCode!)
                    .Distinct()
                    .ToList();

                var taskMembers = await _context.TaskMembers
                    .Where(tm => taskCodes.Contains(tm.TaskCode))
                    .ToListAsync();

                // ============================================================
                // 6. GET USERS
                // ============================================================

                var users = await _context.Users
                    .ToListAsync();

                // ============================================================
                // 7. TASK RESPONSE
                // ============================================================

                object BuildTaskResponse(TaskTable task)
                {
                    var members = taskMembers
                        .Where(tm => tm.TaskCode == task.TaskCode)
                        .Select(tm =>
                        {
                            var member = users.FirstOrDefault(
                                u => u.UserId.ToString() == tm.Assign_To
                            );

                            return new
                            {
                                userId = tm.Assign_To,
                                name = member?.Name ?? "N/A",
                                email = member?.Email ?? "N/A"
                            };
                        })
                        .ToList();

                    return new
                    {
                        taskCode = task.TaskCode,
                        goalCode = task.GoalCode,
                        task = task.Task,
                        description = task.Description,
                        priority = task.Priority,
                        status = task.Status,
                        createdAt = task.Created_At,
                        dueDate = task.Due_Date,
                        completedDate = task.Completed_Date,
                        members = members
                    };
                }

                // ============================================================
                // 8. QUANTITY
                // ============================================================
                // IMPORTANT:
                // Every goal uses its OWN TargetQuantity.
                //
                // Yearly:
                //     TargetQuantity = yearly target
                //
                // Monthly:
                //     TargetQuantity = monthly target
                //
                // We do NOT calculate yearly quantity from monthly goals.
                // ============================================================

                int? GetPendingQuantity(Goal goal)
                {
                    if (!goal.TargetQuantity.HasValue)
                        return null;

                    int completed = goal.CompletedQuantity ?? 0;

                    return Math.Max(
                        0,
                        goal.TargetQuantity.Value - completed
                    );
                }

                // ============================================================
                // 9. RESULT
                // ============================================================

                var result = new List<object>();

                // ============================================================
                // 10. YEARLY GOALS
                // ============================================================

                var yearlyGoals = goals
                    .Where(g =>
                        !string.IsNullOrWhiteSpace(g.GoalType) &&
                        g.GoalType.Equals(
                            "Yearly",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var yearlyGoal in yearlyGoals)
                {
                    // ========================================================
                    // YEARLY ASSIGNED USERS
                    // ========================================================

                    var yearlyAssignedUserIds = await _context.GoalAssignment
                        .Where(a => a.GoalId == yearlyGoal.Id)
                        .Select(a => a.UserId)
                        .ToListAsync();

                    var yearlyAssignedUsers = users
                        .Where(u => yearlyAssignedUserIds.Contains(u.UserId))
                        .Select(u => new
                        {
                            userId = u.UserId,
                            name = u.Name,
                            email = u.Email,
                            department = u.Department
                        })
                        .ToList();

                    // ========================================================
                    // YEARLY TASKS
                    // ========================================================

                    var yearlyTasks = tasks
                        .Where(t => t.GoalCode == yearlyGoal.GoalCode)
                        .Select(BuildTaskResponse)
                        .ToList();

                    // ========================================================
                    // MONTHLY GOALS UNDER THIS YEAR
                    // ========================================================

                    var monthlyGoals = goals
                        .Where(g =>
                            !string.IsNullOrWhiteSpace(g.GoalType) &&
                            g.GoalType.Equals(
                                "Monthly",
                                StringComparison.OrdinalIgnoreCase) &&
                            g.ParentGoalId == yearlyGoal.Id)
                        .OrderBy(g => g.StartDate)
                        .ToList();

                    var monthlyResult = new List<object>();

                    foreach (var monthlyGoal in monthlyGoals)
                    {
                        // ====================================================
                        // MONTHLY ASSIGNED USERS
                        // ====================================================

                        var monthlyAssignedUserIds =
                            await _context.GoalAssignment
                                .Where(a => a.GoalId == monthlyGoal.Id)
                                .Select(a => a.UserId)
                                .ToListAsync();

                        var monthlyAssignedUsers = users
                            .Where(u => monthlyAssignedUserIds.Contains(u.UserId))
                            .Select(u => new
                            {
                                userId = u.UserId,
                                name = u.Name,
                                email = u.Email,
                                department = u.Department
                            })
                            .ToList();

                        // ====================================================
                        // MONTHLY CREATOR
                        // ====================================================

                        var monthlyCreator = users
                            .FirstOrDefault(
                                u => u.UserId == monthlyGoal.CreatedBy);

                        // ====================================================
                        // MONTHLY TASKS
                        // ====================================================

                        var monthlyTasks = tasks
                            .Where(t => t.GoalCode == monthlyGoal.GoalCode)
                            .Select(BuildTaskResponse)
                            .ToList();

                        // ====================================================
                        // MONTHLY RESULT
                        // ====================================================

                        monthlyResult.Add(new
                        {
                            id = monthlyGoal.Id,

                            goalCode = monthlyGoal.GoalCode,

                            goalType = monthlyGoal.GoalType,

                            parentGoalId = monthlyGoal.ParentGoalId,

                            title = monthlyGoal.Title,

                            priority = monthlyGoal.Priority,

                            status = monthlyGoal.Status,

                            progress = monthlyGoal.Progress,

                            goalpoints = monthlyGoal.Goalpoints,

                            // =================================================
                            // MONTHLY QUANTITY
                            // =================================================

                            targetQuantity = monthlyGoal.TargetQuantity,

                            completedQuantity = monthlyGoal.CompletedQuantity,

                            pendingQuantity = GetPendingQuantity(monthlyGoal),

                            // =================================================
                            // DATES
                            // =================================================

                            startDate = monthlyGoal.StartDate,

                            dueDate = monthlyGoal.DueDate,

                            completedDate = monthlyGoal.Completed_Date,

                            // =================================================
                            // CREATOR
                            // =================================================

                            createdBy = monthlyGoal.CreatedBy,

                            createdByName =
                                monthlyCreator?.Name ?? "N/A",

                            // =================================================
                            // USERS
                            // =================================================

                            assignedUsers = monthlyAssignedUsers,

                            // =================================================
                            // TASKS
                            // =================================================

                            taskCount = monthlyTasks.Count,

                            tasks = monthlyTasks
                        });
                    }

                    // ========================================================
                    // YEARLY CREATOR
                    // ========================================================

                    var yearlyCreator = users
                        .FirstOrDefault(
                            u => u.UserId == yearlyGoal.CreatedBy);

                    // ========================================================
                    // YEARLY RESULT
                    // ========================================================

                    result.Add(new
                    {
                        id = yearlyGoal.Id,

                        goalCode = yearlyGoal.GoalCode,

                        goalType = yearlyGoal.GoalType,

                        parentGoalId = yearlyGoal.ParentGoalId,

                        title = yearlyGoal.Title,

                        priority = yearlyGoal.Priority,

                        status = yearlyGoal.Status,

                        progress = yearlyGoal.Progress,

                        goalpoints = yearlyGoal.Goalpoints,

                        // =====================================================
                        // YEARLY QUANTITY
                        // =====================================================
                        // This is the value stored directly in the YEARLY goal.
                        //
                        // Example:
                        // Upto 150 machine -> 150
                        //
                        // It is NOT calculated from monthly goals.
                        // =====================================================

                        targetQuantity = yearlyGoal.TargetQuantity,

                        completedQuantity = yearlyGoal.CompletedQuantity,

                        pendingQuantity = GetPendingQuantity(yearlyGoal),

                        // =====================================================
                        // DATES
                        // =====================================================

                        startDate = yearlyGoal.StartDate,

                        dueDate = yearlyGoal.DueDate,

                        completedDate = yearlyGoal.Completed_Date,

                        // =====================================================
                        // CREATOR
                        // =====================================================

                        createdBy = yearlyGoal.CreatedBy,

                        createdByName =
                            yearlyCreator?.Name ?? "N/A",

                        // =====================================================
                        // USERS
                        // =====================================================

                        assignedUsers = yearlyAssignedUsers,

                        // =====================================================
                        // TASKS
                        // =====================================================

                        taskCount = yearlyTasks.Count,

                        tasks = yearlyTasks,

                        // =====================================================
                        // MONTHLY GOALS
                        // =====================================================

                        monthlyGoals = monthlyResult
                    });
                }

                // ============================================================
                // 11. STANDALONE MONTHLY GOALS
                // ============================================================

                var standaloneMonthlyGoals = goals
                    .Where(g =>
                        !string.IsNullOrWhiteSpace(g.GoalType) &&
                        g.GoalType.Equals(
                            "Monthly",
                            StringComparison.OrdinalIgnoreCase) &&
                        g.ParentGoalId == null)
                    .OrderBy(g => g.StartDate)
                    .ToList();

                foreach (var monthlyGoal in standaloneMonthlyGoals)
                {
                    // ========================================================
                    // ASSIGNED USERS
                    // ========================================================

                    var assignedUserIds = await _context.GoalAssignment
                        .Where(a => a.GoalId == monthlyGoal.Id)
                        .Select(a => a.UserId)
                        .ToListAsync();

                    var assignedUsers = users
                        .Where(u => assignedUserIds.Contains(u.UserId))
                        .Select(u => new
                        {
                            userId = u.UserId,
                            name = u.Name,
                            email = u.Email,
                            department = u.Department
                        })
                        .ToList();

                    // ========================================================
                    // CREATOR
                    // ========================================================

                    var creator = users
                        .FirstOrDefault(
                            u => u.UserId == monthlyGoal.CreatedBy);

                    // ========================================================
                    // TASKS
                    // ========================================================

                    var monthlyTasks = tasks
                        .Where(t => t.GoalCode == monthlyGoal.GoalCode)
                        .Select(BuildTaskResponse)
                        .ToList();

                    // ========================================================
                    // STANDALONE MONTHLY RESULT
                    // ========================================================

                    result.Add(new
                    {
                        id = monthlyGoal.Id,

                        goalCode = monthlyGoal.GoalCode,

                        goalType = monthlyGoal.GoalType,

                        parentGoalId = monthlyGoal.ParentGoalId,

                        title = monthlyGoal.Title,

                        priority = monthlyGoal.Priority,

                        status = monthlyGoal.Status,

                        progress = monthlyGoal.Progress,

                        goalpoints = monthlyGoal.Goalpoints,

                        // =====================================================
                        // MONTHLY QUANTITY
                        // =====================================================

                        targetQuantity = monthlyGoal.TargetQuantity,

                        completedQuantity = monthlyGoal.CompletedQuantity,

                        pendingQuantity = GetPendingQuantity(monthlyGoal),

                        // =====================================================
                        // DATES
                        // =====================================================

                        startDate = monthlyGoal.StartDate,

                        dueDate = monthlyGoal.DueDate,

                        completedDate = monthlyGoal.Completed_Date,

                        // =====================================================
                        // CREATOR
                        // =====================================================

                        createdBy = monthlyGoal.CreatedBy,

                        createdByName =
                            creator?.Name ?? "N/A",

                        // =====================================================
                        // USERS
                        // =====================================================

                        assignedUsers = assignedUsers,

                        // =====================================================
                        // TASKS
                        // =====================================================

                        taskCount = monthlyTasks.Count,

                        tasks = monthlyTasks,

                        monthlyGoals = new List<object>()
                    });
                }

                // ============================================================
                // 12. RETURN
                // ============================================================

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message = "Error while getting goals with tasks.",
                        error = ex.Message
                    });
            }
        }

        [Authorize]
        [HttpGet("tasks")]
        public async Task<IActionResult> GetAllTasks()
        {
            // =====================================================
            // 1. GET ALL TASKS
            // =====================================================

            var tasks = await _context.Tasks
                .OrderByDescending(t => t.Created_At)
                .ToListAsync();


            // =====================================================
            // 2. GET TASK MEMBERS
            // =====================================================

            var taskMembers = await _context.TaskMembers
                .ToListAsync();


            // =====================================================
            // 3. GET USERS
            // =====================================================

            var users = await _context.Users
                .ToListAsync();


            // =====================================================
            // 4. BUILD TASK RESPONSE
            // =====================================================

            var result = tasks.Select(t =>
            {
                // -------------------------------------------------
                // ASSIGNER
                // -------------------------------------------------

                var assignerMember = taskMembers
                    .Where(tm =>
                        tm.TaskCode == t.TaskCode &&
                        !string.IsNullOrWhiteSpace(tm.Assign_By))
                    .FirstOrDefault();

                object? assigner = null;

                string assignerDepartment = "N/A";


                if (assignerMember != null)
                {
                    var assignerIdString =
                        assignerMember.Assign_By
                            .Split('-', 2)[0];

                    if (int.TryParse(
                            assignerIdString,
                            out int assignerId))
                    {
                        var assignerUser = users
                            .FirstOrDefault(u =>
                                u.UserId == assignerId);

                        if (assignerUser != null)
                        {
                            assigner = new
                            {
                                userId = assignerUser.UserId,
                                name = assignerUser.Name,
                                email = assignerUser.Email,
                                department = assignerUser.Department,
                                role = assignerUser.Role
                            };

                            assignerDepartment =
                                assignerUser.Department ?? "N/A";
                        }
                    }
                }


                // -------------------------------------------------
                // PENDING QUANTITY
                // -------------------------------------------------

                int? pendingQuantity = null;

                if (t.Quantity.HasValue)
                {
                    pendingQuantity = Math.Max(
                        0,
                        t.Quantity.Value -
                        (t.CompletedQuantity ?? 0)
                    );
                }


                // -------------------------------------------------
                // ASSIGNED USERS
                // -------------------------------------------------

                var assignedTo = taskMembers
                    .Where(tm =>
                        tm.TaskCode == t.TaskCode &&
                        !string.IsNullOrWhiteSpace(tm.Assign_To))
                    .Select(tm =>
                    {
                        var userIdString =
                            tm.Assign_To
                                .Split('-', 2)[0];

                        if (!int.TryParse(
                                userIdString,
                                out int userId))
                        {
                            return null;
                        }

                        var user = users
                            .FirstOrDefault(u =>
                                u.UserId == userId);

                        if (user == null)
                            return null;

                        return new
                        {
                            userId = user.UserId,
                            name = user.Name,
                            email = user.Email,
                            department = user.Department,
                            role = user.Role,

                            taskMemberCode = tm.TMCode,

                            userStatus = tm.UserStatus,

                            assignedAt = tm.Assigned_At,

                            assignedBy = tm.Assign_By
                        };
                    })
                    .Where(x => x != null)
                    .ToList();


                // -------------------------------------------------
                // FINAL TASK
                // -------------------------------------------------

                return new
                {
                    // =============================================
                    // BASIC TASK DETAILS
                    // =============================================

                    taskCode = t.TaskCode,

                    task = t.Task,

                    goalCode = t.GoalCode,

                    description = t.Description,

                    priority = t.Priority,

                    status = t.Status,


                    // =============================================
                    // DATES
                    // =============================================

                    createdAt = t.Created_At,

                    dueDate = t.Due_Date,

                    completedDate =
                        t.Completed_Date == default(DateTime)
                            ? (DateTime?)null
                            : t.Completed_Date,


                    // =============================================
                    // MEMBERS
                    // =============================================

                    totalMembers = t.Members,

                    assignedTo = assignedTo,


                    // =============================================
                    // ASSIGNER
                    // =============================================

                    assignedBy = assigner,

                    assignerDepartment = assignerDepartment,


                    // =============================================
                    // EDIT STATUS
                    // =============================================

                    wasEdited = t.wasEdited,


                    // =============================================
                    // PERFORMANCE
                    // =============================================

                    performanceType = t.PerformanceType,


                    // =============================================
                    // QUANTITY
                    // =============================================

                    quantity = t.Quantity,

                    completedQuantity = t.CompletedQuantity,

                    pendingQuantity = pendingQuantity,


                    // =============================================
                    // TIME
                    // =============================================

                    startTime = t.StartTime,

                    endTime = t.EndTime
                };
            }).ToList();


            // =====================================================
            // 5. TASK STATUS COUNTS
            // =====================================================

            int totalTasks = tasks.Count;

            int pendingCount = tasks.Count(t =>
                !string.IsNullOrWhiteSpace(t.Status) &&
                t.Status.Trim().Equals(
                    "pending",
                    StringComparison.OrdinalIgnoreCase)
            );

            int notStartedCount = tasks.Count(t =>
                !string.IsNullOrWhiteSpace(t.Status) &&
                t.Status.Trim().Equals(
                    "not started",
                    StringComparison.OrdinalIgnoreCase)
            );

            int inProgressCount = tasks.Count(t =>
                !string.IsNullOrWhiteSpace(t.Status) &&
                (
                    t.Status.Trim().Equals(
                        "inprogress",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    t.Status.Trim().Equals(
                        "in progress",
                        StringComparison.OrdinalIgnoreCase)
                )
            );

            int completedCount = tasks.Count(t =>
                !string.IsNullOrWhiteSpace(t.Status) &&
                t.Status.Trim().Equals(
                    "completed",
                    StringComparison.OrdinalIgnoreCase)
            );


            // =====================================================
            // 6. FINAL RESPONSE
            // =====================================================

            return Ok(new
            {
                totalTasks,

                pendingCount,

                notStartedCount,

                inProgressCount,

                completedCount,

                result
            });
        }

        [Authorize]
        [HttpGet("taskbyid/{taskCode}")]
        public async Task<IActionResult> GetTaskByCode(string taskCode)
        {
            // =====================================================
            // 1. GET TASK
            // =====================================================

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskCode == taskCode);

            if (task == null)
                return NotFound("Task not found");


            // =====================================================
            // 2. GET TASK MEMBERS
            // =====================================================

            var taskMembers = await _context.TaskMembers
                .Where(tm => tm.TaskCode == taskCode)
                .ToListAsync();


            // =====================================================
            // 3. GET QUANTITY SPLITS
            // =====================================================

            var quantitySplits = await _context.TaskQuantitySplit
                .Where(s => s.TaskCode == taskCode)
                .OrderBy(s => s.Id)
                .ToListAsync();


            // =====================================================
            // 4. GET ALL USER IDS FROM TASK MEMBERS
            // =====================================================

            var assignedUserIds = taskMembers
                .Where(tm => !string.IsNullOrWhiteSpace(tm.Assign_To))
                .Select(tm =>
                {
                    var firstPart = tm.Assign_To
                        .Split('-', 2)[0];

                    return int.TryParse(firstPart, out int id)
                        ? (int?)id
                        : null;
                })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();


            // =====================================================
            // 5. GET USERS
            // =====================================================

            var assignedUsers = await _context.Users
                .Where(u => assignedUserIds.Contains(u.UserId))
                .ToListAsync();


            // =====================================================
            // 6. GET ASSIGNER ID
            // =====================================================

            var assignedByString = taskMembers
                .Select(tm => tm.Assign_By)
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x));

            int? assignedById = null;

            if (!string.IsNullOrWhiteSpace(assignedByString))
            {
                var firstPart = assignedByString
                    .Split('-', 2)[0];

                if (int.TryParse(firstPart, out int parsedId))
                {
                    assignedById = parsedId;
                }
            }


            // =====================================================
            // 7. GET ASSIGNER
            // =====================================================

            var assignedByUser = assignedById.HasValue
                ? await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.UserId == assignedById.Value)
                : null;


            // =====================================================
            // 8. BUILD NORMAL ASSIGNED USER LIST
            //
            // This is used for normal tasks.
            // =====================================================

            var assignedTo = taskMembers
                .Where(tm =>
                    !string.IsNullOrWhiteSpace(tm.Assign_To))
                .Select(tm =>
                {
                    var firstPart = tm.Assign_To
                        .Split('-', 2)[0];

                    if (!int.TryParse(
                            firstPart,
                            out int memberUserId))
                    {
                        return null;
                    }

                    var memberUser = assignedUsers
                        .FirstOrDefault(u =>
                            u.UserId == memberUserId);

                    if (memberUser == null)
                        return null;

                    return new
                    {
                        userId = memberUser.UserId,

                        name = memberUser.Name,

                        email = memberUser.Email,

                        department = memberUser.Department,

                        role = memberUser.Role,

                        taskMemberCode = tm.TMCode,

                        userStatus = tm.UserStatus,

                        assignedAt = tm.Assigned_At,

                        assignedBy = tm.Assign_By,

                        splitId = tm.SplitId
                    };
                })
                .Where(x => x != null)
                .ToList();


            // =====================================================
            // 9. BUILD SPLIT RESPONSE
            //
            // IMPORTANT:
            // Each split gets ONLY the users assigned to that split.
            // =====================================================

            var splitResponse = quantitySplits
                .Select(split =>
                {
                    var splitMembers = taskMembers
                        .Where(tm =>
                            tm.SplitId.HasValue &&
                            tm.SplitId.Value == split.Id)
                        .ToList();

                    var members = splitMembers
                        .Select(tm =>
                        {
                            if (string.IsNullOrWhiteSpace(
                                tm.Assign_To))
                            {
                                return null;
                            }

                            var firstPart = tm.Assign_To
                                .Split('-', 2)[0];

                            if (!int.TryParse(
                                    firstPart,
                                    out int memberUserId))
                            {
                                return null;
                            }

                            var memberUser = assignedUsers
                                .FirstOrDefault(u =>
                                    u.UserId == memberUserId);

                            if (memberUser == null)
                                return null;

                            return new
                            {
                                userId = memberUser.UserId,

                                name = memberUser.Name,

                                email = memberUser.Email,

                                department = memberUser.Department,

                                role = memberUser.Role,

                                taskMemberCode = tm.TMCode,

                                userStatus = tm.UserStatus,

                                assignedAt = tm.Assigned_At,

                                assignedBy = tm.Assign_By,

                                splitId = tm.SplitId
                            };
                        })
                        .Where(x => x != null)
                        .ToList();


                    // =================================================
                    // CHECK WHETHER ALL MEMBERS COMPLETED
                    // =================================================

                    bool allMembersCompleted =
                        members.Any() &&
                        members.All(m =>
                            !string.IsNullOrWhiteSpace(
                                m!.userStatus) &&
                            m.userStatus.Trim().Equals(
                                "completed",
                                StringComparison.OrdinalIgnoreCase));


                    // =================================================
                    // CHECK QUANTITY COMPLETED
                    // =================================================

                    bool quantityCompleted =
                        split.CompletedQuantity >= split.Quantity;


                    // =================================================
                    // SPLIT STATUS
                    // =================================================

                    string splitStatus;

                    if (allMembersCompleted &&
                        quantityCompleted)
                    {
                        splitStatus = "completed";
                    }
                    else if (
                        split.CompletedQuantity > 0 ||
                        members.Any(m =>
                            !string.IsNullOrWhiteSpace(
                                m!.userStatus) &&
                            !m.userStatus.Trim().Equals(
                                "notstarted",
                                StringComparison.OrdinalIgnoreCase) &&
                            !m.userStatus.Trim().Equals(
                                "not started",
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        splitStatus = "inprogress";
                    }
                    else
                    {
                        splitStatus = "not started";
                    }


                    return new
                    {
                        id = split.Id,

                        quantity = split.Quantity,

                        completedQuantity =
                            split.CompletedQuantity,

                        pendingQuantity = Math.Max(
                            0,
                            split.Quantity -
                            split.CompletedQuantity),

                        status = splitStatus,

                        memberCount = members.Count,

                        members = members
                    };
                })
                .ToList();


            // =====================================================
            // 10. PENDING TOTAL QUANTITY
            // =====================================================

            int? pendingQuantity = null;

            if (task.Quantity.HasValue)
            {
                int completedQuantity =
                    task.CompletedQuantity ?? 0;

                pendingQuantity = Math.Max(
                    0,
                    task.Quantity.Value -
                    completedQuantity);
            }


            // =====================================================
            // 11. RETURN TASK
            // =====================================================

            return Ok(new
            {
                taskCode = task.TaskCode,

                task = task.Task,

                goalCode = task.GoalCode,

                description = task.Description,

                priority = task.Priority,

                status = task.Status,

                createdAt = task.Created_At,

                dueDate = task.Due_Date,

                completedDate =
                    task.Completed_Date == default(DateTime)
                        ? (DateTime?)null
                        : task.Completed_Date,

                wasEdited = task.wasEdited,


                // =================================================
                // MEMBERS
                // =================================================

                totalMembers = task.Members,

                assignedTo = assignedTo,


                // =================================================
                // ASSIGNER
                // =================================================

                assignedBy = assignedByUser == null
                    ? null
                    : new
                    {
                        userId = assignedByUser.UserId,

                        name = assignedByUser.Name,

                        email = assignedByUser.Email,

                        department = assignedByUser.Department,

                        role = assignedByUser.Role
                    },

                assignerDepartment =
                    assignedByUser?.Department,


                // =================================================
                // PERFORMANCE
                // =================================================

                performanceType =
                    task.PerformanceType,


                // =================================================
                // TOTAL QUANTITY
                // =================================================

                quantity = task.Quantity,

                completedQuantity =
                    task.CompletedQuantity,

                pendingQuantity =
                    pendingQuantity,


                // =================================================
                // QUANTITY SPLITS
                // =================================================

                hasQuantitySplits =
                    quantitySplits.Any(),

                quantitySplits =
                    splitResponse,


                // =================================================
                // TIME
                // =================================================

                startTime = task.StartTime,

                endTime = task.EndTime
            });
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

            // =====================================================
            // 1. NOTIFY REMOVED USERS
            // =====================================================

            foreach (var member in membersToRemove)
            {
                int removedUserId =
                    int.Parse(member.Assign_To.Split('-')[0]);

                var removedUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == removedUserId);

                if (removedUser != null &&
                    !string.IsNullOrWhiteSpace(removedUser.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            removedUser.FcmToken!,
                            "Removed From Task",
                            $"You have been removed from the task '{task.Task}' by {editor.Name}"
                        );

                        Console.WriteLine(
                            $"Removal notification sent to {removedUser.Name}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"FCM Error for removed user {removedUser.UserId}: {ex.Message}"
                        );
                    }
                }
            }

            // =====================================================
            // 2. NOTIFY CURRENT MEMBERS IF TASK DETAILS CHANGED
            // =====================================================

            if (isChanged)
            {
                var currentUserIds = newUserIds
                    .Distinct()
                    .ToList();

                var notifyUsers = await _context.Users
                    .Where(u =>
                        currentUserIds.Contains(u.UserId) &&
                        !string.IsNullOrWhiteSpace(u.FcmToken))
                    .ToListAsync();

                foreach (var user in notifyUsers)
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            user.FcmToken!,
                            "Task Updated",
                            $"The task '{task.Task}' assigned to you was updated by {editor.Name}"
                        );

                        Console.WriteLine(
                            $"Task update notification sent to {user.Name} ({user.UserId})"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"FCM Error for user {user.UserId}: {ex.Message}"
                        );
                    }
                }
            }

            // =====================================================
            // 3. NOTIFY NEWLY ADDED USERS
            // =====================================================

            foreach (var user in usersToAdd)
            {
                if (!string.IsNullOrWhiteSpace(user.FcmToken))
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            user.FcmToken!,
                            "Task Assigned",
                            $"You have been added to the task '{task.Task}' by {editor.Name}"
                        );

                        Console.WriteLine(
                            $"Task assignment notification sent to {user.Name} ({user.UserId})"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"FCM Error for user {user.UserId}: {ex.Message}"
                        );
                    }
                }
            }

            // =====================================================
            // 4. NOTIFY IMMEDIATE UPPER POSITION
            //    IF TASK EDITED OR MEMBERS CHANGED
            // =====================================================

            if (isChanged || hasMemberChanges)
            {
                if (int.TryParse(editor.Role, out int creatorPosition))
            {
                // Find immediate higher position
                var upperRole = await _context.Roles
                    .Where(r =>
                        r.Status &&
                        r.Position < creatorPosition)
                    .OrderByDescending(r => r.Position)
                    .FirstOrDefaultAsync();

                if (upperRole != null)
                {
                    // Users.Role stores the position number
                    var upperUsers = await _context.Users
                        .Where(u =>
                            u.Department == editor.Department &&
                            u.Role == upperRole.Position.ToString() &&
                            !string.IsNullOrWhiteSpace(u.FcmToken))
                        .ToListAsync();

                    foreach (var upperUser in upperUsers)
                    {
                        try
                        {
                            await _firebaseNotificationService.SendNotificationAsync(
                                         upperUser.FcmToken,
                                         "Task Updated",
                                         $"{editor.Name} updated the task '{task.Task}'"
                                     );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Upper Position FCM Error ({upperUser.UserId}): {ex.Message}"
                            );
                        }
                    }
                }
            }
          
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

            // =====================================================
            // SEND TASK DELETED NOTIFICATION
            // =====================================================

            // Existing in-app notification
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

            // Get assigned users with FCM tokens
            var notifyUsers = await _context.Users
                .Where(u =>
                    assignedUserIds.Contains(u.UserId) &&
                    !string.IsNullOrWhiteSpace(u.FcmToken))
                .ToListAsync();

            // Send Firebase push notification
            foreach (var user in notifyUsers)
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        user.FcmToken!,
                        "Task Deleted",
                        $"The task '{taskName}' assigned to you was deleted by {editor.Name}"
                    );

                  
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"FCM Error for user {user.UserId}: {ex}"
                    );
                }
            }
            var editorRoleInfo = await _context.Roles
       .FirstOrDefaultAsync(r =>
           r.RoleName == editor.Role);

            if (editorRoleInfo != null)
            {
                // Find the immediate higher position
                //
                // Example:
                // Staff 4 -> Assistant Manager 3
                // Assistant Manager 3 -> Manager 2
                // Manager 2 -> Director 1
                //
                var upperRole = await _context.Roles
                    .Where(r =>
                        r.Position < editorRoleInfo.Position)
                    .OrderByDescending(r => r.Position)
                    .FirstOrDefaultAsync();

                if (upperRole != null)
                {
                    // Get upper-position users
                    // from the SAME department
                    var upperUsers = await _context.Users
                        .Where(u =>
                            u.Department == editor.Department &&
                            u.Role == upperRole.RoleName &&
                            !string.IsNullOrWhiteSpace(u.FcmToken))
                        .ToListAsync();

                    foreach (var upperUser in upperUsers)
                    {
                        try
                        {
                            await _firebaseNotificationService.SendNotificationAsync(
                                upperUser.FcmToken!,
                                "Task Deleted",
                                $"{editor.Name} deleted the task '{taskName}'"
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Upper Position FCM Error " +
                                $"({upperUser.UserId}): {ex}"
                            );
                        }
                    }
                }
            }
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

            var directors = await _context.Users
        .Join(
            _context.Roles,
            u => u.Role,
            r => r.RoleName,
            (u, r) => new
            {
                User = u,
                Role = r
            }
        )
        .Where(x =>
            x.Role.Position == 1 &&
            !string.IsNullOrWhiteSpace(x.User.FcmToken))
        .Select(x => x.User)
        .ToListAsync();


            // ==========================================
            // DIRECTOR NOTIFICATION
            // ==========================================

            string directorMessage =
                $"{editor.Name} deleted {userName} ";


            foreach (var director in directors)
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        director.FcmToken,
                        "User Deleted",
                        directorMessage
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Director FCM Error ({director.UserId}): {ex.Message}"
                    );
                }
            }



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
            if (user.Role != dto.Role)
                AddAudit("Department", user.Role, dto.Role);

            // ✅ Update
            user.Name = dto.Name;
            user.Email = dto.Email;
            user.Department = dto.Department;
            user.Role = dto.Role;

            if (userChanged)
                user.wasEdited = true;

            await _context.SaveChangesAsync();
            if (userChanged)
            {
                var directors = await _context.Users
                    .Join(
                        _context.Roles,
                        u => u.Role,
                        r => r.RoleName,
                        (u, r) => new
                        {
                            User = u,
                            Role = r
                        }
                    )
                    .Where(x =>
                        x.Role.Position == 1 &&
                        !string.IsNullOrWhiteSpace(x.User.FcmToken))
                    .Select(x => x.User)
                    .ToListAsync();


                string directorMessage =
                    $"{editor.Name} updated {user.Name}'s user details.";


                foreach (var director in directors)
                {
                    try
                    {
                        await _firebaseNotificationService.SendNotificationAsync(
                            director.FcmToken,
                            "User Updated",
                            directorMessage
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Director FCM Error ({director.UserId}): {ex.Message}"
                        );
                    }
                }
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

        [HttpPost("department-access")]
        public async Task<IActionResult> AddDepartmentAccess([FromBody] DepartmentAccessRequest model)
        {
            try
            {
                // ================================
                // VALIDATION
                // ================================

                if (model.UserId <= 0)
                    return BadRequest("Invalid UserId.");

                if (model.RoleId <= 0)
                    return BadRequest("Invalid RoleId.");

                if (model.HeadDepartmentId <= 0)
                    return BadRequest("Invalid HeadDepartmentId.");

                if (model.SubDepartmentIds == null ||
                    !model.SubDepartmentIds.Any())
                {
                    return BadRequest("At least one sub department is required.");
                }

                // Remove duplicate department IDs
                var subDepartmentIds = model.SubDepartmentIds
                    .Distinct()
                    .ToList();

                // ================================
                // CHECK USER
                // ================================

                var user = await _context.Users
                    .FirstOrDefaultAsync(x => x.UserId == model.UserId);

                if (user == null)
                    return NotFound("User not found.");

                // ================================
                // CHECK ROLE
                // ================================

                var role = await _context.Roles
                    .FirstOrDefaultAsync(x => x.Id == model.RoleId);

                if (role == null)
                    return NotFound("Role not found.");

                // ================================
                // CHECK HEAD DEPARTMENT
                // ================================

                var headDepartment = await _context.Departments
                    .FirstOrDefaultAsync(x => x.Id == model.HeadDepartmentId);

                if (headDepartment == null)
                    return NotFound("Head department not found.");

                // ================================
                // CHECK SUB DEPARTMENTS
                // ================================

                var existingDepartments = await _context.Departments
                    .Where(x => subDepartmentIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync();

                var invalidDepartments = subDepartmentIds
                    .Except(existingDepartments)
                    .ToList();

                if (invalidDepartments.Any())
                {
                    return BadRequest(new
                    {
                        message = "One or more sub departments do not exist.",
                        invalidDepartmentIds = invalidDepartments
                    });
                }

                // ================================
                // REMOVE EXISTING ACCESS
                // ================================
                // This makes the request behave like
                // "set access" rather than "add duplicates".

                var existingAccess = await _context.DepartmentAccess
                    .Where(x =>
                        x.UserId == model.UserId &&
                        x.HeadDepartmentId == model.HeadDepartmentId)
                    .ToListAsync();

                if (existingAccess.Any())
                {
                    _context.DepartmentAccess.RemoveRange(existingAccess);
                }

                // ================================
                // ADD NEW ACCESS
                // ================================

                var newAccess = subDepartmentIds
                    .Select(subDepartmentId => new DepartmentAccess
                    {
                        UserId = model.UserId,
                        RoleId = model.RoleId,
                        HeadDepartmentId = model.HeadDepartmentId,
                        SubDepartmentId = subDepartmentId
                    })
                    .ToList();

                await _context.DepartmentAccess.AddRangeAsync(newAccess);

                await _context.SaveChangesAsync();

                // ================================
                // RESPONSE
                // ================================

                return Ok(new
                {
                    message = "Department access added successfully.",
                    userId = model.UserId,
                    roleId = model.RoleId,
                    headDepartmentId = model.HeadDepartmentId,
                    subDepartmentIds = subDepartmentIds
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while adding department access.",
                    error = ex.Message
                });
            }
        }
        [Authorize]
        [HttpGet("my-department-access")]
        public async Task<IActionResult> GetMyDepartmentAccess()
        {
            try
            {

                var userIdClaim = User.FindFirst("UserId")?.Value;

                if (string.IsNullOrWhiteSpace(userIdClaim))
                    return Unauthorized("UserId not found in token.");

                if (!int.TryParse(userIdClaim, out int userId))
                    return Unauthorized("Invalid UserId in token.");

                var accessList = await _context.DepartmentAccess
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                if (!accessList.Any())
                {
                    return Ok(new
                    {
                        userId = userId,
                        message = "No department access found.",
                        headDepartments = new List<object>()
                    });
                }

     
                var departmentIds = accessList
                    .SelectMany(x => new[]
                    {
                x.HeadDepartmentId,
                x.SubDepartmentId
                    })
                    .Distinct()
                    .ToList();

                var departments = await _context.Departments
                    .Where(x => departmentIds.Contains(x.Id))
                    .ToListAsync();

                var result = accessList
                    .GroupBy(x => new
                    {
                        x.HeadDepartmentId,
                        x.RoleId
                    })
                    .Select(group =>
                    {
                        var headDepartment = departments
                            .FirstOrDefault(x => x.Id == group.Key.HeadDepartmentId);

                        var subDepartments = group
                            .Select(x => x.SubDepartmentId)
                            .Distinct()
                            .Select(subId =>
                            {
                                var department = departments
                                    .FirstOrDefault(x => x.Id == subId);

                                return new
                                {
                                    id = subId,
                                    name = department?.DepartmentName
                                };
                            })
                            .ToList();

                        return new
                        {
                            roleId = group.Key.RoleId,

                            headDepartment = new
                            {
                                id = group.Key.HeadDepartmentId,
                                name = headDepartment?.DepartmentName
                            },

                            subDepartments = subDepartments
                        };
                    })
                    .ToList();

                return Ok(new
                {
                    userId = userId,
                    headDepartments = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while getting department access.",
                    error = ex.Message
                });
            }
        }


        [Authorize]
        [HttpGet("my-department-auditlogs")]
        public async Task<IActionResult> GetMyDepartmentAuditLogs()
        {
            try
            {


                var userIdClaim = User.FindFirst("UserId")?.Value;

                if (string.IsNullOrWhiteSpace(userIdClaim))
                    return Unauthorized("UserId not found in token.");

                if (!int.TryParse(userIdClaim, out int userId))
                    return Unauthorized("Invalid UserId in token.");


                var accessList = await _context.DepartmentAccess
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                if (!accessList.Any())
                {
                    return Ok(new
                    {
                        userId = userId,
                        message = "No department access found.",
                        departmentIds = new List<int>(),
                        departmentNames = new List<string>(),
                        auditLogs = new List<object>()
                    });
                }


                var departmentIds = accessList
                    .SelectMany(x => new[]
                    {
                x.HeadDepartmentId,
                x.SubDepartmentId
                    })
                    .Distinct()
                    .ToList();


                departmentIds = departmentIds
                    .Where(x => x > 0)
                    .ToList();


                if (!departmentIds.Any())
                {
                    return Ok(new
                    {
                        userId = userId,
                        message = "No valid department IDs found.",
                        departmentIds = departmentIds,
                        departmentNames = new List<string>(),
                        auditLogs = new List<object>()
                    });
                }


                var departments = await _context.Departments
                    .Where(d => departmentIds.Contains(d.Id))
                    .ToListAsync();


                var departmentNames = departments
                    .Where(d => !string.IsNullOrWhiteSpace(d.DepartmentName))
                    .Select(d => d.DepartmentName.Trim())
                    .Distinct()
                    .ToList();


                if (!departmentNames.Any())
                {
                    return Ok(new
                    {
                        userId = userId,
                        departmentIds = departmentIds,
                        departmentNames = departmentNames,
                        auditLogs = new List<object>()
                    });
                }

                var departmentUsers = await _context.Users
                    .Where(u =>
                        u.Department != null &&
                        departmentNames.Contains(u.Department.Trim()))
                    .Select(u => new
                    {
                        u.UserId,
                        u.Name,
                        u.Department,
                        u.Role
                    })
                    .ToListAsync();


                var departmentUserIds = departmentUsers
                    .Select(u => u.UserId.ToString())
                    .Distinct()
                    .ToList();


                if (!departmentUserIds.Any())
                {
                    return Ok(new
                    {
                        userId = userId,
                        departmentIds = departmentIds,
                        departmentNames = departmentNames,
                        departmentUsers = departmentUsers,
                        auditLogs = new List<object>()
                    });
                }



                var auditLogs = await _context.Auditlog
                    .Where(log =>
                        !string.IsNullOrEmpty(log.EditedUid) &&
                        departmentUserIds.Contains(log.EditedUid))
                    .OrderByDescending(log => log.ChangeDateandTime)
                    .ToListAsync();

                var result = auditLogs.Select(log =>
                {
                    var editedUser = departmentUsers
                        .FirstOrDefault(u =>
                            u.UserId.ToString() == log.EditedUid);

                    return new
                    {
                        id = log.Id,

                        entityId = log.EntityId,

                        entityType = log.EntityType,

                        action = log.Action,

                        fieldChanged = log.Fieldchanged,

                        oldValue = log.Oldvalue,

                        newValue = log.Newvalue,

                        editedUid = log.EditedUid,

                        editedUserName = editedUser?.Name,

                        editedDepartment = editedUser?.Department,

                        editedRole = log.EditedRole,

                        changeDateAndTime = log.ChangeDateandTime
                    };
                }).ToList();

                return Ok(new
                {
                    userId = userId,

                    departmentIds = departmentIds,

                    departmentNames = departmentNames,

                    departmentUsers = departmentUsers,

                    auditLogs = result,

                    totalRecords = result.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred while getting department audit logs.",
                    error = ex.Message
                });
            }
        }


    }

}
