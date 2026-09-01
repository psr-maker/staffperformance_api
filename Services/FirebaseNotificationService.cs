using FirebaseAdmin.Messaging;

namespace StaffWork_Track.Services
{
    public class FirebaseNotificationService
    {
        public async Task<string> SendNotificationAsync(
            string deviceToken,
            string title,
            string body)
        {
            var message = new Message
            {
                Token = deviceToken,

                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = title,
                    Body = body
                },

                Android = new AndroidConfig
                {
                    Priority = Priority.High
                }
            };

            return await FirebaseMessaging.DefaultInstance.SendAsync(message);
        }
    }
}