using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using staff_work_tracking.Data;

namespace staff.Controllers
{
    [Route("api/Notification")]
    [ApiController]
    [Authorize]
    public class NotificController : ControllerBase
    {
        private readonly AppDbContext _context;

        public NotificController(AppDbContext context)
        {
            _context = context;
        }
        [Authorize]
        [HttpGet("MyNotifications")]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized("Invalid token");

            if (!long.TryParse(userIdClaim.Value, out long userId))
                return Unauthorized("Invalid UserId claim");
            var notifications = await _context.Notifications
                .Where(n => n.ReceiverId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.IsRead,
                    n.CreatedAt,
                    n.Message,
                    SenderName = _context.Users
                        .Where(u => u.UserId == n.SenderId)
                        .Select(u => u.Name)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(notifications);
        }


        [Authorize]
        [HttpDelete("delete-notification/{id}")]
        public async Task<IActionResult> DeleteNotification(int id)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.ReceiverId == userId);

            if (notification == null)
                return NotFound("Notification not found for this user");

            _context.Notifications.Remove(notification);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Notification deleted successfully",
                notificationId = id
            });
        }
       
        [Authorize]
        [HttpPut("mark-all-read")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            var notifications = await _context.Notifications
                .Where(n => n.ReceiverId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Notifications marked as read" });
        }
     
        [Authorize]
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            var count = await _context.Notifications
                .CountAsync(n => n.ReceiverId == userId && !n.IsRead);

            return Ok(new
            {
                unreadCount = count
            });
        }
        [Authorize]
        [HttpDelete("delete-all")]
        public async Task<IActionResult> DeleteAllNotifications()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            var notifications = await _context.Notifications
                .Where(n => n.ReceiverId == userId)
                .ToListAsync();

            if (!notifications.Any())
                return Ok(new { message = "No notifications found" });

            _context.Notifications.RemoveRange(notifications);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "All notifications deleted"
            });
        }
    }
}