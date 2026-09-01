using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OfficeOpenXml;
using staff_work_tracking.Data;
using StaffWork_Track.Services;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

namespace staff.Controllers
{
    [Route("api/Announcement")]
    [ApiController]
    public class AnnouncementsController : ControllerBase
    {

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly GeoService _geoService;
        private readonly FirebaseNotificationService _firebaseNotificationService;

        public AnnouncementsController(AppDbContext context, IWebHostEnvironment env, GeoService geoService, FirebaseNotificationService firebaseNotificationService)
        {
            _context = context;
            _env = env;
            _geoService = geoService;
            _firebaseNotificationService = firebaseNotificationService;
        }


        [Authorize]
        [HttpPost("postannouncements")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UploadAnnouncement(
     [FromForm] string title,
     [FromForm] string? description,
     [FromForm] string targetRole,
     IFormFile? file)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null)
                return Unauthorized();

            long senderId = long.Parse(userIdClaim);
            string? filePath = null;
            string fileType = "text";
            string? jsonData = null;
            string? ext = null; // ✅ MOVE HERE

            // 📁 FILE UPLOAD
            if (file != null && file.Length > 0)
            {
                ext = Path.GetExtension(file.FileName).ToLower(); // ✅ assign here

                var folder = "/var/www/uploads/announcements";
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var fileName = Guid.NewGuid() + ext;
                var fullPath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                filePath = $"/uploads/announcements/{fileName}";

                fileType = ext switch
                {
                    ".pdf" => "pdf",
                    ".jpg" or ".jpeg" or ".png" => "image",
                    ".csv" => "excel",
                    ".xls" or ".xlsx" => "excel",
                    _ => "file"
                };

                // ✅ MOVE CSV LOGIC INSIDE SAME BLOCK (BEST PRACTICE)
                if (ext == ".csv")
                {
                    using (var reader = new StreamReader(file.OpenReadStream()))
                    {
                        var lines = new List<string>();
                        while (!reader.EndOfStream)
                            lines.Add(await reader.ReadLineAsync());

                        var headers = lines[0].Split(',');

                        var result = new List<Dictionary<string, string>>();

                        for (int i = 1; i < lines.Count; i++)
                        {
                            var values = lines[i].Split(',');
                            var obj = new Dictionary<string, string>();

                            for (int j = 0; j < headers.Length; j++)
                            {
                                obj[headers[j]] = values[j];
                            }

                            result.Add(obj);
                        }

                        jsonData = JsonConvert.SerializeObject(result);
                    }
                }
            }

            var announcement = new Announcement
            {
                Title = title,
                Description = description,
                TargetRole = targetRole,
                FileType = fileType,
                FilePath = filePath,
                FileName = file?.FileName,
                JsonData = jsonData,
                CreatedBy = senderId.ToString(),
                CreatedDate = DateTime.UtcNow
            };

            _context.Announcements.Add(announcement);
            await _context.SaveChangesAsync();
            // =====================================================
            // SEND ANNOUNCEMENT NOTIFICATION TO TARGET ROLE
            // =====================================================

            var targetUsers = await _context.Users
     .Where(u =>
         !string.IsNullOrWhiteSpace(u.FcmToken) &&
         (
             targetRole == "All" ||
             u.Role == targetRole
         )
     )
     .ToListAsync();

            foreach (var user in targetUsers)
            {
                try
                {
                    await _firebaseNotificationService.SendNotificationAsync(
                        user.FcmToken!,
                        "New Announcement",
                        title
                    );

                    Console.WriteLine(
                        $"Announcement notification sent to {user.Name} ({user.UserId})"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"FCM Error for user {user.UserId}: {ex.Message}"
                    );
                }
            }
            return Ok(new
            {
                message = "Announcement created",
                id = announcement.Id
            });
        }


        [Authorize]
        [HttpGet("GetAnouncements")]
        public async Task<IActionResult> GetAllAnnouncements()
        {
            var role = User.FindFirst("Role")?.Value;
            var userId = User.FindFirst("UserId")?.Value;

            if (string.IsNullOrEmpty(role))
            {
                return Unauthorized("Role not found.");
            }

            var query = _context.Announcements.AsQueryable();

            // Director = Role 1
            // Director can see every announcement
            if (role != "1")
            {
                query = query.Where(a =>
                    a.TargetRole == "All" ||
                    a.TargetRole == role ||
                    a.CreatedBy == userId
                );
            }

            var announcements = await query
                .OrderByDescending(a => a.CreatedDate)
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Description,
                    a.FileType,
                    a.FilePath,
                    a.FileName,
                    a.JsonData,
                    a.TargetRole,
                    a.CreatedBy,
                    a.CreatedDate
                })
                .ToListAsync();

            return Ok(announcements);
        }

        [Authorize]
        [HttpDelete("delete-announcement/{id}")]
        public async Task<IActionResult> DeleteAnnouncement(int id)
        {
            var announcement = await _context.Announcements
                .FirstOrDefaultAsync(a => a.Id == id);

            if (announcement == null)
                return NotFound("Announcement not found");

            // Delete file from disk
            if (!string.IsNullOrEmpty(announcement.FilePath))
            {
                var fullPath = Path.Combine(_env.WebRootPath, announcement.FilePath.TrimStart('/'));

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }

            _context.Announcements.Remove(announcement);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Announcement deleted successfully" });
        }


        [Authorize]
        [HttpPost("addworklog")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> AddWorkLog(
       [FromForm] CreateWorkLogDto dto)
        {
            try
            {
            
                var userIdClaim = User.FindFirst("UserId")?.Value;

                if (userIdClaim == null)
                    return Unauthorized("Invalid token.");

                if (!int.TryParse(userIdClaim, out int userId))
                    return Unauthorized("Invalid User ID.");

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                    return Unauthorized("User not found.");


                if (string.IsNullOrWhiteSpace(dto.Title))
                {
                    return BadRequest(
                        "Work title is required."
                    );
                }

                if (string.IsNullOrWhiteSpace(dto.WorkType))
                {
                    return BadRequest(
                        "Work type is required."
                    );
                }

                var workType =
                    dto.WorkType.Trim().ToUpper();

                if (workType != "IN" &&
                    workType != "OUT")
                {
                    return BadRequest(
                        "WorkType must be IN or OUT."
                    );
                }


                if (dto.Latitude == 0 ||
                    dto.Longitude == 0)
                {
                    return BadRequest(
                        "Location is required."
                    );
                }

                if (dto.Image == null)
                {
                    return BadRequest(
                        "Photo is required."
                    );
                }


                var workDate = dto.WorkDate.Date;

                string? imagePath = null;

                var folder =
                    "/var/www/uploads/worklog";

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var extension =
                    Path.GetExtension(dto.Image.FileName);

                var fileName =
                    $"{Guid.NewGuid()}{extension}";

                var filePath =
                    Path.Combine(folder, fileName);

                using (var stream =
                    new FileStream(
                        filePath,
                        FileMode.Create))
                {
                    await dto.Image.CopyToAsync(stream);
                }

                imagePath =
                    "/uploads/worklog/" + fileName;

                var currentTime = DateTime.Now;

                var workLog = new WorkLog
                {
                    UserId = userId,

                    WorkDate = workDate,

                    Title = dto.Title.Trim(),

                    WorkType = workType,

                    Time = currentTime,

                    Description =
                        dto.Description?.Trim(),

                    DepartmentName =
                        user.Department,

                    Status =
                        dto.IsSubmit
                            ? "Submitted"
                            : "Draft",

                    Latitude =
                        dto.Latitude,

                    Longitude =
                        dto.Longitude,

                    LocationName =
                        dto.LocationName,

                    ImageUrl =
                        imagePath
                };


                _context.WorkLog.Add(workLog);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message =
                        $"{workType} worklog saved successfully.",

                    id = workLog.Id,

                    title = workLog.Title,

                    workType = workLog.WorkType,

                    time = workLog.Time,

                    status = workLog.Status,

                    imageUrl = workLog.ImageUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Error while creating worklog.",

                        error = ex.Message
                    }
                );
            }
        }

        [Authorize]
        [HttpGet("myworklogs")]
        public async Task<IActionResult> GetMyWorkLogs(
        [FromQuery] DateTime date)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (userIdClaim == null)
                return Unauthorized("Invalid token.");

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid User ID.");

            var logs = await _context.WorkLog
                .Where(w =>
                    w.UserId == userId &&
                    w.WorkDate.Date == date.Date
                )
                .OrderBy(w => w.Time)
                .Select(w => new
                {
                    w.Id,

                    // Actual work title
                    w.Title,

                    // IN / OUT
                    w.WorkType,

                    w.Description,

                    // IN/OUT time
                    w.Time,

                    w.Status,

                    w.Latitude,
                    w.Longitude,

                    w.LocationName,

                    w.ImageUrl,

                    w.WorkDate
                })
                .ToListAsync();

            return Ok(logs);
        }

        [Authorize]
        [HttpGet("department-worklogs")]
        public async Task<IActionResult> GetDepartmentWorkLogs([FromQuery] DateTime? date)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return NotFound("User not found");

            var query =
                from w in _context.WorkLog
                join u in _context.Users
                    on w.UserId equals u.UserId
                where w.DepartmentName == user.Department
                      && w.Status == "Submitted"
                select new
                {
                    w.Id,

                    // Work title
                    w.Title,

                    // IN / OUT
                    w.WorkType,

                    w.Description,

                    // IN / OUT time
                    w.Time,

                    w.Status,

                    w.Latitude,
                    w.Longitude,

                    w.LocationName,

                    w.ImageUrl,

                    w.WorkDate,

                    // Staff details
                    u.UserId,
                    u.Name
                };

            // Optional date filter
            if (date.HasValue)
            {
                query = query.Where(
                    w => w.WorkDate.Date == date.Value.Date
                );
            }

            var result = await query
                .OrderByDescending(w => w.WorkDate)
                .ThenBy(w => w.Time)
                .ToListAsync();

            return Ok(result);
        }

        [HttpGet("all-worklogs")]
        public async Task<IActionResult> GetAllWorkLogs([FromQuery] string? department,[FromQuery] DateTime? date)
        {
            var query =
                from w in _context.WorkLog
                join u in _context.Users 
                    on w.UserId equals u.UserId
                where w.Status == "Submitted"
                select new
                {
                    w.Id,

                    // Actual work title
                    w.Title,

                    // IN / OUT
                    w.WorkType,

                    w.Description,

                    // IN/OUT time
                    w.Time,

                    w.Status,

                    w.Latitude,
                    w.Longitude,

                    w.LocationName,

                    w.ImageUrl,

                    w.WorkDate,
                    w.DepartmentName
                };

            if (date.HasValue)
            {
                query = query.Where(w => w.WorkDate.Date == date.Value.Date);
            }

            if (!string.IsNullOrEmpty(department))
            {
                query = query.Where(w => w.DepartmentName == department);
            }

            var result = await query
                .OrderByDescending(w => w.WorkDate)
                .ThenBy(w => w.Time)
                .ToListAsync();

            return Ok(result);
        }

        [Authorize]
        [HttpPost("updateworklogstatus")]
        public async Task<IActionResult> UpdateWorkLogStatus([FromQuery] DateTime workDate,[FromQuery] string status)
        {
          
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null)
                return Unauthorized("Invalid token.");

            int userId = int.Parse(userIdClaim);

            if (status != "Submitted" && status != "Draft")
                return BadRequest("Status must be either 'Draft' or 'Submitted'.");

           
            var workLogs = await _context.WorkLog
                .Where(w => w.UserId == userId && w.WorkDate.Date == workDate.Date)
                .ToListAsync();

            if (!workLogs.Any())
                return NotFound("No worklogs found for this user and date.");

           
            foreach (var log in workLogs)
            {
                log.Status = status;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"All worklogs for user {userId} on {workDate:yyyy-MM-dd} updated to '{status}'.",
                updatedCount = workLogs.Count
            });
        }


        [Authorize]
        [HttpPost("send-warning")]
        public async Task<IActionResult> SendWarning([FromBody] Warning request)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;

            if (!int.TryParse(userIdClaim, out int senderId))
                return Unauthorized("Invalid token.");

            var sender = await _context.Users
        .FirstOrDefaultAsync(u => u.UserId == senderId);
            int previousWarningsCount = await _context.Warnings
                .Where(w => w.ReceiverId == request.ReceiverId)
                .CountAsync();

            int escalationLevel = previousWarningsCount + 1;

            var warning = new Warning
            {
                SenderId = senderId,
                ReceiverId = request.ReceiverId,
                Title = request.Title,
                Message = request.Message,
                Severity = request.Severity,
                EscalationLevel = escalationLevel,  
                CreatedDate = DateTime.Now
            };

            _context.Warnings.Add(warning);
            // ✅ Better notification message
            string message = $"You received a warning from {sender.Name}";

            // ✅ Create notification
            //var notification = new Notification
            //{
              
            //    Title = request.Title,
            //    Message = message,
            //    SenderId = senderId,
            //    ReceiverId = request.ReceiverId,
                
            //    RelatedId = warning.WarningId.ToString(),
            
            //    IsRead = false
            //};

         //   _context.Notifications.Add(notification);

            // ✅ Save once
            await _context.SaveChangesAsync();

            var employee = await _context.Users
.FirstOrDefaultAsync(u => u.UserId == request.ReceiverId);

            if (employee != null &&
                !string.IsNullOrWhiteSpace(employee.FcmToken))
            {
                try
                {

                    string title = "Warning";

                    await _firebaseNotificationService.SendNotificationAsync(
                        employee.FcmToken,
                        title,
                        message
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FCM Error: {ex}");
                }
            }
         

            return Ok(new
            {
                message = "Warning sent successfully",
                escalationLevel = escalationLevel
            });
        }


        [Authorize]
        [HttpGet("get-warnings")]
        public async Task<IActionResult> GetWarnings()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            var roleClaim = User.FindFirst("Role")?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid token.");

            var query = from w in _context.Warnings
                        join sender in _context.Users
                            on w.SenderId equals sender.UserId
                        join receiver in _context.Users
                            on w.ReceiverId equals receiver.UserId
                        select new
                        {
                            w.WarningId,
                            w.Title,
                            w.Message,
                            w.Severity,
                            w.EscalationLevel,
                            w.CreatedDate,

                            SenderId = sender.UserId,
                            SenderName = sender.Name,
                            SenderRole = sender.Role,

                            ReceiverId = receiver.UserId,
                            ReceiverName = receiver.Name,
                            ReceiverRole = receiver.Role,
                            ReceiverDept = receiver.Department
                        };

            if (roleClaim == "1")
            {
                // ✅ Director → all warnings
            }
            else
            {
                // ✅ Everyone else → only their warnings
                query = query.Where(w => w.ReceiverId == userId);
            }

            var warnings = await query
                .OrderByDescending(w => w.CreatedDate)
                .ToListAsync();

            return Ok(warnings);
        }


        [Authorize]
        [HttpGet("get-department-warnings")]
        public async Task<IActionResult> GetDepartmentWarnings()
        {
            try
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                var roleClaim = User.FindFirst("Role")?.Value;

                if (!int.TryParse(userIdClaim, out int userId))
                    return Unauthorized("Invalid token.");

                if (roleClaim != "2")
                    return Forbid("Access denied.");

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                    return NotFound("User not found");

                var query = from w in _context.Warnings
                            join sender in _context.Users
                                on w.SenderId equals sender.UserId
                            join receiver in _context.Users
                                on w.ReceiverId equals receiver.UserId
                            select new
                            {
                                w.WarningId,
                                w.Title,
                                w.Message,
                                w.Severity,
                                w.EscalationLevel,
                                w.CreatedDate,

                                SenderId = sender.UserId,
                                SenderName = sender.Name,
                                SenderRole = sender.Role,

                                ReceiverId = receiver.UserId,
                                ReceiverName = receiver.Name,
                                ReceiverRole = receiver.Role,
                                ReceiverDept = receiver.Department
                            };
             if (roleClaim == "2")
                {
               
                    query = query.Where(w => w.ReceiverDept == user.Department);
                }

                var warnings = await query
                    .OrderByDescending(w => w.CreatedDate)
                    .ToListAsync();

                return Ok(warnings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        [HttpGet("get-warnings-by-user/{userId}")]
        public async Task<IActionResult> GetWarningsByUser(int userId)
        {
            var warnings = await (
                from w in _context.Warnings
                join sender in _context.Users
                    on w.SenderId equals sender.UserId
                join receiver in _context.Users
                    on w.ReceiverId equals receiver.UserId
                where w.ReceiverId == userId  
                orderby w.CreatedDate descending
                select new
                {
                    w.WarningId,
                    w.Title,
                    w.Message,
                    w.Severity,
                    w.EscalationLevel,
                    w.CreatedDate,

                    SenderId = sender.UserId,
                    SenderName = sender.Name,
                    SenderRole = sender.Role,

                    ReceiverId = receiver.UserId,
                    ReceiverName = receiver.Name,
                    ReceiverRole = receiver.Role,
                    ReceiverDept = receiver.Department
                }
            ).ToListAsync();

            return Ok(warnings);
        }



        private async Task SendAnnouncementNotifications(
        long senderId,
        string title,
        string targetRole,
        int announcementId)
        {
            var sender = await _context.Users
                .Where(u => u.UserId == senderId)
                .Select(u => new { u.Role, u.Name })
                .FirstOrDefaultAsync();

            if (sender == null) return;

            List<int> receivers;

            if (targetRole.ToLower() == "all")
            {
                receivers = await _context.Users
                    .Where(u => u.UserId != senderId)
                    .Select(u => u.UserId)
                    .ToListAsync();
            }
            else
            {
                receivers = await _context.Users
                    .Where(u => u.Role.ToLower() == targetRole.ToLower()
                             && u.UserId != senderId)
                    .Select(u => u.UserId)
                    .ToListAsync();
            }

            foreach (var r in receivers)
            {
                //_context.Notifications.Add(new Notification
                //{
                 
                //    Title = "New Announcement",
                //    Message = $"{sender.Role} {sender.Name}: {title}",
                //    SenderId = senderId,
                //    ReceiverId = r,
                //    RelatedId = announcementId.ToString(),
                //    IsRead = false,
                   
                //});
            }

            await _context.SaveChangesAsync();
        }

    }
}
