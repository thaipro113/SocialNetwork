using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;

namespace SocialNetwork.Controllers
{
    public class NotificationController : Controller
    {
        private readonly SocialNetworkDbContext _context;

        public NotificationController(SocialNetworkDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                // Gửi returnUrl để khi login xong quay lại
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Index", "Notification") });
            }

            var notifications = _context.Notifications
                .Include(n => n.FromUser)
                .Include(n => n.Post)
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Message,
                    n.IsRead,
                    n.CreatedAt,
                    n.PostId,
                    FromUser = new
                    {
                        n.FromUser.FullName,
                        Avatar = string.IsNullOrEmpty(n.FromUser.ImageUrl)
                            ? "/Uploads/default-avatar.png"
                            : "/Uploads/" + n.FromUser.ImageUrl
                    }
                })
                .ToList();

            ViewBag.Notifications = notifications;
            return View();
        }

        // Lấy danh sách thông báo (mặc định 10 cái mới nhất)
        public IActionResult GetNotifications()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false });

            var notifications = _context.Notifications
                .Include(n => n.FromUser)
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new
                {
                    id = n.Id,                    // <-- ĐÚNG: id (không phải Id)
                    type = n.Type,
                    message = n.Message,
                    postId = n.PostId ?? 0,
                    friendRequestId = n.FriendRequestId,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                    fromUser = new
                    {
                        fullName = n.FromUser.FullName,
                        avatar = string.IsNullOrEmpty(n.FromUser.ImageUrl)
                            ? "/Uploads/default-avatar.png"
                            : "/Uploads/" + n.FromUser.ImageUrl
                    }
                })
                .ToList();

            var unreadCount = _context.Notifications.Count(n => n.UserId == userId && !n.IsRead);

            return Json(new { success = true, data = notifications, unread = unreadCount });
        }
        [HttpPost]
        public IActionResult MarkSingleAsRead(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var notification = _context.Notifications.FirstOrDefault(n => n.Id == id && n.UserId == userId);

            if (notification == null)
                return Json(new { success = false });

            notification.IsRead = true;
            _context.SaveChanges();

            return Json(new { success = true });
        }

        // Đánh dấu một hoặc tất cả là đã đọc
        [HttpPost]
        public IActionResult MarkAsRead(int? id = null)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false });

            if (id.HasValue)
            {
                // Mark single
                var noti = _context.Notifications.FirstOrDefault(n => n.Id == id.Value && n.UserId == userId && !n.IsRead);
                if (noti != null)
                {
                    noti.IsRead = true;
                    _context.SaveChanges();
                    return Json(new { success = true, updated = 1 });
                }
                return Json(new { success = false, message = "Notification not found or already read" });
            }
            else
            {
                // Mark all
                var notis = _context.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToList();
                foreach (var n in notis) n.IsRead = true;
                int updated = _context.SaveChanges();
                return Json(new { success = true, updated });
            }
        }
        [HttpPost]
        public IActionResult Delete(int id)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false });

            var noti = _context.Notifications.FirstOrDefault(n => n.Id == id && n.UserId == userId);
            if (noti != null)
            {
                _context.Notifications.Remove(noti);
                _context.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Notification not found" });
        }

        [HttpPost]
        public IActionResult DeleteAll()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false });

            var notis = _context.Notifications.Where(n => n.UserId == userId).ToList();
            _context.Notifications.RemoveRange(notis);
            int deleted = _context.SaveChanges();

            return Json(new { success = true, deleted });
        }

    }
}