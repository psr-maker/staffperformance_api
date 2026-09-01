using staff_work_tracking.Data;
using Microsoft.EntityFrameworkCore;
namespace staff.Services
{
    public class NotificationService
    {
        private readonly AppDbContext _context;

        public NotificationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SendTaskGoalNotification(
    string action,
    string entityType,
    string entityCode,
    string entityName,
    int senderId,
    string senderName,
    string senderRole,
    string department,
    string? assignTo = null,          
    List<int>? assignedUserIds = null 
)
        {
            List<int> receiverIds = new();

            // 🔹 STAFF → Manager + Director
            if (senderRole == "Staff")
            {
                var managers = await _context.Users
                    .Where(u => u.Role == "2" && u.Department == department)
                    .Select(u => u.UserId)
                    .ToListAsync();

                var directors = await _context.Users
                    .Where(u => u.Role == "1")
                    .Select(u => u.UserId)
                    .ToListAsync();

                receiverIds.AddRange(managers);
                receiverIds.AddRange(directors);
            }

            // 🔹 MANAGER → Director
            else if (senderRole == "2")
            {
                var directors = await _context.Users
                    .Where(u => u.Role == "1")
                    .Select(u => u.UserId)
                    .ToListAsync();

                receiverIds.AddRange(directors);
            }

            // 🔹 DIRECTOR → skip higher roles

            // ✅ GOAL → Assigned user
            if (!string.IsNullOrEmpty(assignTo))
            {
                var idPart = assignTo.Split('-')[0];
                if (int.TryParse(idPart, out int assignedUserId))
                {
                    receiverIds.Add(assignedUserId);
                }
            }

            // ✅ TASK → Assigned users
            if (assignedUserIds != null && assignedUserIds.Any())
            {
                receiverIds.AddRange(assignedUserIds);
            }

            // ❌ REMOVE creator
            receiverIds = receiverIds
                .Where(id => id != senderId)
                .Distinct()
                .ToList();

            foreach (var rid in receiverIds)
            {
                //_context.Notifications.Add(new Notification
                //{
                    
                //    Title = $"{entityType} {action}",
                //    Message = $"{department} {senderRole} {senderName} {action.ToLower()} {entityType.ToLower()} - {entityName}",
                //    SenderId = senderId,
                //    ReceiverId = rid,
                  
                //    RelatedId = entityCode,
                //    IsRead = false,
                  
                //});
            }

            await _context.SaveChangesAsync();
        }


        public async Task SendUserCrudNotificationToDirector(
    string action,
    int senderId,
    string senderName,
    string senderRole,
    string department,
    string targetUserName
)
        {
            // ❌ Skip if Director
            if (senderRole == "1")
                return;

            var directors = await _context.Users
                .Where(u => u.Role == "1")
                .ToListAsync();

            string message = $"{department} {senderRole} {senderName} " +
                             $"{action.ToLower()} user {targetUserName}";

            foreach (var director in directors)
            {
                //_context.Notifications.Add(new Notification
                //{
                 
                //    Title = $"User {action}",
                //    Message = message,
                //    SenderId = senderId,
                //    ReceiverId = director.UserId,
                  
                //    RelatedId = targetUserName,
                //    IsRead = false,
                   
                //});
            }

            await _context.SaveChangesAsync();
        }


    }
}
