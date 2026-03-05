using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;
using System;

namespace SocialNetwork.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly SocialNetworkDbContext _context;

        public NotificationHub(SocialNetworkDbContext context)
        {
            _context = context;
        }

        // Gửi thông báo khi có like hoặc comment mới (nếu cần dùng sau, nhưng hiện lưu ở Controller)
        public async Task SendNotification(int userId, int postId, string type, string message)
        {
            var httpContext = Context.GetHttpContext();
            int? fromUserId = httpContext?.Session?.GetInt32("UserId");

            var notification = new Notification
            {
                UserId = userId,
                FromUserId = fromUserId ?? 0,
                PostId = postId > 0 ? postId : null,
                Type = type,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.Now
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            var fromUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == notification.FromUserId);
            var notificationData = new
            {
                Id = notification.Id,
                Type = notification.Type,
                Message = notification.Message,
                PostId = notification.PostId ?? 0,
                FriendRequestId = notification.FriendRequestId ?? 0,
                IsRead = notification.IsRead,
                createdAt = notification.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                fromUser = new
                {
                    UserId = fromUser?.UserId ?? 0,
                    name = fromUser?.FullName ?? "Ẩn danh",
                    avatar = string.IsNullOrEmpty(fromUser?.ImageUrl)
                        ? "/Uploads/default-avatar.png"
                        : "/Uploads/" + fromUser.ImageUrl
                }
            };

            await Clients.Group(userId.ToString()).SendAsync("ReceiveNotification", notificationData);
        }

        // Kết nối SignalR
        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.Session?.GetInt32("UserId")?.ToString();
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                Console.WriteLine($"User {userId} added to group {userId}.");
            }
            await base.OnConnectedAsync();
        }
    }
}