using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StaffWork_Track.Services;

namespace staff.Controllers
{
    [Route("api/Notification")]
    [ApiController]
    public class NotificController : ControllerBase
    {
        private readonly FirebaseNotificationService _notificationService;

        public NotificController(
            FirebaseNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send(
           [FromBody] SendNotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceToken))
            {
                return BadRequest("Device token is required.");
            }

            var messageId = await _notificationService.SendNotificationAsync(
                request.DeviceToken,
                request.Title,
                request.Body
            );

            return Ok(new
            {
                success = true,
                messageId
            });
        }
    }

    public class SendNotificationRequest
    {
        public string DeviceToken { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}