using Microsoft.AspNetCore.Mvc;
using SocialNetwork.Models;
using Microsoft.EntityFrameworkCore;
using System;
using Microsoft.AspNetCore.SignalR;
using SocialNetwork.Hubs;

public class FriendController : Controller
{
    private readonly SocialNetworkDbContext _context;
    private readonly IHttpContextAccessor _httpContext;
    private readonly IHubContext<NotificationHub> _hubContext;

    public FriendController(SocialNetworkDbContext context, IHttpContextAccessor httpContext, IHubContext<NotificationHub> hubContext)
    {
        _context = context;
        _httpContext = httpContext;
        _hubContext = hubContext;
    }

    public IActionResult Index()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            // Gửi returnUrl để khi login xong quay lại
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Index", "Friend") });
        }

        // Lấy danh sách bạn bè
        var friends = _context.FriendRequests
            .Include(f => f.FromUser)
            .Include(f => f.ToUser)
            .Where(f => (f.FromUserId == userId || f.ToUserId == userId)
                     && f.Status == FriendRequestStatus.Accepted)
            .Select(f => f.FromUserId == userId ? f.ToUser : f.FromUser)
            .ToList();

        // Lời mời đến
        var incoming = _context.FriendRequests
            .Include(f => f.FromUser)
            .Where(f => f.ToUserId == userId && f.Status == FriendRequestStatus.Pending)
            .ToList();

        // Lời mời đã gửi
        var outgoing = _context.FriendRequests
            .Include(f => f.ToUser)
            .Where(f => f.FromUserId == userId && f.Status == FriendRequestStatus.Pending)
            .ToList();

        ViewBag.Friends = friends;
        ViewBag.Incoming = incoming;
        ViewBag.Outgoing = outgoing;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SendRequest(int toUserId)
    {
        var fromUserId = HttpContext.Session.GetInt32("UserId");
        if (fromUserId == null)
            return Json(new { success = false, message = "Bạn chưa đăng nhập" });

        if (toUserId == fromUserId.Value)
            return Json(new { success = false, message = "Không thể tự kết bạn với mình" });

        var a = Math.Min(fromUserId.Value, toUserId);
        var b = Math.Max(fromUserId.Value, toUserId);

        // Đã là bạn
        if (_context.Friendships.Any(f => f.UserAId == a && f.UserBId == b))
            return Json(new { success = false, message = "Đã là bạn bè" });

        // Đã gửi lời mời
        if (_context.FriendRequests.Any(r => r.FromUserId == fromUserId && r.ToUserId == toUserId && r.Status == FriendRequestStatus.Pending))
            return Json(new { success = false, message = "Đã gửi lời mời rồi" });

        // Người kia đã gửi cho mình
        if (_context.FriendRequests.Any(r => r.FromUserId == toUserId && r.ToUserId == fromUserId.Value && r.Status == FriendRequestStatus.Pending))
            return Json(new { success = false, message = "Người này đã gửi lời mời cho bạn" });

        // Tạo lời mời
        var request = new FriendRequest
        {
            FromUserId = fromUserId.Value,
            ToUserId = toUserId,
            CreatedAt = DateTime.Now,
            Status = FriendRequestStatus.Pending
        };
        _context.FriendRequests.Add(request);
        _context.SaveChanges();

        // Tạo thông báo
        var fromUser = _context.Users.Find(fromUserId.Value);
        var noti = new Notification
        {
            UserId = toUserId,
            FromUserId = fromUserId.Value,
            Type = "friend_request",
            Message = $" đã gửi lời mời kết bạn",
            FriendRequestId = request.Id,
            CreatedAt = DateTime.Now
        };
        _context.Notifications.Add(noti);
        _context.SaveChanges();

        // Gửi SignalR
        await _hubContext.Clients.Group(toUserId.ToString())
            .SendAsync("ReceiveNotification", new
            {
                Id = noti.Id,
                Type = "friend_request",
                Message = $"{fromUser?.FullName} đã gửi lời mời kết bạn",
                FriendRequestId = request.Id,
                createdAt = noti.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                fromUser = new
                {
                    UserId = fromUserId.Value,
                    name = fromUser?.FullName,
                    avatar = string.IsNullOrEmpty(fromUser?.ImageUrl) ? "/Uploads/default-avatar.png" : $"/Uploads/{fromUser.ImageUrl}"
                }
            });

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<JsonResult> Accept([FromBody] int id)
    {
        var request = _context.FriendRequests.Find(id);
        if (request == null) return Json(new { success = false });

        var userId = HttpContext.Session.GetInt32("UserId").Value;
        if (request.ToUserId != userId) return Json(new { success = false });

        request.Status = FriendRequestStatus.Accepted;
        request.RespondedAt = DateTime.Now;

        var a = Math.Min(request.FromUserId, request.ToUserId);
        var b = Math.Max(request.FromUserId, request.ToUserId);
        if (!_context.Friendships.Any(f => f.UserAId == a && f.UserBId == b))
            _context.Friendships.Add(new Friendship { UserAId = a, UserBId = b, CreatedAt = DateTime.UtcNow });

        // XÓA THÔNG BÁO
        var noti = _context.Notifications.FirstOrDefault(n => n.FriendRequestId == id);
        if (noti != null) _context.Notifications.Remove(noti);

        _context.SaveChanges();

        // GỬI TOAST CHO CẢ 2 (dùng Groups thay vì Users để khớp với NotificationHub)
        var fromName = _context.Users.Find(request.FromUserId)?.FullName;
        var toName = _context.Users.Find(request.ToUserId)?.FullName;

        // userId là người chấp nhận (ToUserId), otherUserId là người gửi lời mời (FromUserId)
        var otherUserId = request.FromUserId;
        var friendName = fromName; // Tên của người gửi lời mời

        var toastMessage = $"✅ Bạn và <b>{friendName}</b> đã là bạn bè!";

        // Gửi toast cho cả hai người
        await _hubContext.Clients.Group(userId.ToString()).SendAsync("FriendAccepted", new
        {
            message = toastMessage,
            friendName = friendName
        });

        await _hubContext.Clients.Group(otherUserId.ToString()).SendAsync("FriendAccepted", new
        {
            message = toastMessage,
            friendName = toName
        });

        // XÓA NOTI REALTIME (dùng Groups)
        if (noti != null)
            await _hubContext.Clients.Group(request.ToUserId.ToString())
                .SendAsync("RemoveNotification", noti.Id);

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<JsonResult> Decline([FromBody] int id)
    {
        var request = _context.FriendRequests.Find(id);
        if (request == null) return Json(new { success = false });

        var userId = HttpContext.Session.GetInt32("UserId");
        if (request.ToUserId != userId) return Json(new { success = false, message = "Bạn không có quyền từ chối lời mời này." });

        request.Status = FriendRequestStatus.Declined;
        request.RespondedAt = DateTime.Now;
        _context.SaveChanges();

        // XÓA THÔNG BÁO
        var noti = _context.Notifications.FirstOrDefault(n => n.FriendRequestId == id);
        if (noti != null) _context.Notifications.Remove(noti);

        _context.SaveChanges();


        // XÓA NOTI REALTIME (dùng Groups)
        if (noti != null)
            await _hubContext.Clients.Group(request.ToUserId.ToString())
                .SendAsync("RemoveNotification", noti.Id);

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<JsonResult> Cancel([FromBody] int id)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue) return Json(new { success = false, message = "Chưa đăng nhập" });

        var request = await _context.FriendRequests
            .Include(fr => fr.FromUser)
            .FirstOrDefaultAsync(fr => fr.Id == id && fr.FromUserId == userId.Value);

        if (request == null)
            return Json(new { success = false, message = "Không tìm thấy lời mời" });

        // 1. XÓA THÔNG BÁO
        var noti = await _context.Notifications
            .FirstOrDefaultAsync(n => n.FriendRequestId == id && n.Type == "friend_request");

        if (noti != null)
        {
            _context.Notifications.Remove(noti);
            // Gửi SignalR xóa realtime bên kia
            await _hubContext.Clients.Group(request.ToUserId.ToString())
                .SendAsync("RemoveNotification", noti.Id);
        }

        // 2. XÓA LUÔN FRIEND REQUEST (QUAN TRỌNG!)
        _context.FriendRequests.Remove(request);
        await _context.SaveChangesAsync();

        // 3. TOAST CHO NGƯỜI HỦY
        await _hubContext.Clients.Group(userId.Value.ToString())
            .SendAsync("FriendCanceled", new { message = "❌ Đã hủy lời mời kết bạn" });

        return Json(new { success = true });
    }
    [HttpPost]
    public async Task<JsonResult> CancelOutgoing([FromBody] int toUserId)
    {
        var fromUserId = HttpContext.Session.GetInt32("UserId");
        if (!fromUserId.HasValue) return Json(new { success = false, message = "Chưa đăng nhập" });

        var request = await _context.FriendRequests
            .FirstOrDefaultAsync(r => r.FromUserId == fromUserId.Value && r.ToUserId == toUserId && r.Status == FriendRequestStatus.Pending);

        if (request == null)
            return Json(new { success = false, message = "Không tìm thấy lời mời" });

        // XÓA THÔNG BÁO (nếu có)
        var noti = await _context.Notifications
            .FirstOrDefaultAsync(n => n.FriendRequestId == request.Id && n.Type == "friend_request");
        if (noti != null)
        {
            _context.Notifications.Remove(noti);
            await _hubContext.Clients.Group(toUserId.ToString()).SendAsync("RemoveNotification", noti.Id);
        }

        // XÓA LỜI MỜI
        _context.FriendRequests.Remove(request);
        await _context.SaveChangesAsync();

        // Toast cho người hủy
        await _hubContext.Clients.Group(fromUserId.Value.ToString())
            .SendAsync("FriendCanceled", new { message = "Đã hủy lời mời kết bạn" });

        return Json(new { success = true });
    }
    [HttpGet]
    public JsonResult GetRequestId(int toUserId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return Json(new { success = false, message = "Bạn chưa đăng nhập" });

        var request = _context.FriendRequests
            .FirstOrDefault(r => r.ToUserId == userId && r.FromUserId == toUserId && r.Status == FriendRequestStatus.Pending);
        if (request == null) return Json(new { success = false, message = "Không tìm thấy yêu cầu" });

        return Json(new { success = true, requestId = request.Id });
    }
    [HttpGet]
    public JsonResult CheckFriendStatus(int toUserId)
    {
        var fromUserId = HttpContext.Session.GetInt32("UserId");
        if (fromUserId == null) return Json(new { success = false, status = "not_logged_in" });

        var a = Math.Min(fromUserId.Value, toUserId);
        var b = Math.Max(fromUserId.Value, toUserId);

        var isFriend = _context.Friendships.Any(f => f.UserAId == a && f.UserBId == b);
        if (isFriend) return Json(new { success = true, status = "friends" });

        var outgoingRequest = _context.FriendRequests.Any(r => r.FromUserId == fromUserId && r.ToUserId == toUserId && r.Status == FriendRequestStatus.Pending);
        if (outgoingRequest) return Json(new { success = true, status = "outgoing_request" });

        var incomingRequest = _context.FriendRequests.Any(r => r.FromUserId == toUserId && r.ToUserId == fromUserId && r.Status == FriendRequestStatus.Pending);
        if (incomingRequest) return Json(new { success = true, status = "incoming_request" });

        return Json(new { success = true, status = "none" });
    }

    // Tóm tắt bạn bè + gợi ý (5-10 người)
    [HttpGet]
    public JsonResult GetFriendSummary()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return Json(new { success = false });

        var friendCount = _context.Friendships.Count(f => f.UserAId == userId || f.UserBId == userId);
        var postCount = _context.Posts.Count(p => p.UserId == userId);

        // Tập người đã là bạn hoặc chính mình
        var friendIds = _context.Friendships
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToHashSet();
        friendIds.Add(userId.Value);

        // Tập người đã gửi lời mời kết bạn (chưa được chấp nhận)
        var sentRequestIds = _context.FriendRequests
            .Where(fr => fr.FromUserId == userId && fr.Status == FriendRequestStatus.Pending)
            .Select(fr => fr.ToUserId)
            .ToHashSet();

        // Gợi ý: loại bỏ bạn bè + người đã gửi lời mời + chính mình
        var suggestions = _context.Users
            .Where(u => !friendIds.Contains(u.UserId) && !sentRequestIds.Contains(u.UserId) && u.UserId != userId)
            .OrderByDescending(u => u.CreatedAt)
            .Take(10)
            .Select(u => new
            {
                u.UserId,
                fullName = u.FullName,
                avatar = string.IsNullOrEmpty(u.ImageUrl) ? "/Uploads/default-avatar.png" : "/Uploads/" + u.ImageUrl
            })
            .ToList();

        var online = ChatHub.GetOnlineUserIds().ToHashSet();

        var onlineCount = friendIds.Count(id => id != userId.Value && online.Contains(id));

        var onlineFriends = _context.Users
            .Where(u => friendIds.Contains(u.UserId) && u.UserId != userId.Value && online.Contains(u.UserId))
            .OrderBy(u => u.FullName)
            .Take(10)
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                avatar = string.IsNullOrEmpty(u.ImageUrl) ? "/Uploads/default-avatar.png" : "/Uploads/" + u.ImageUrl
            })
            .ToList();

        return Json(new
        {
            success = true,
            friendCount,
            postCount,
            onlineCount,
            onlineFriends,
            suggestions
        });
    }
    [HttpPost]
    public async Task<JsonResult> Unfriend([FromBody] int userId)
    {
        var currentUserId = HttpContext.Session.GetInt32("UserId");
        if (!currentUserId.HasValue) return Json(new { success = false, message = "Chưa đăng nhập" });

        var a = Math.Min(currentUserId.Value, userId);
        var b = Math.Max(currentUserId.Value, userId);


        var friendship = _context.Friendships
            .FirstOrDefault(f => f.UserAId == a && f.UserBId == b);
        if (friendship != null)
            _context.Friendships.Remove(friendship);

   
        var acceptedRequest = _context.FriendRequests
            .FirstOrDefault(fr =>
                (fr.FromUserId == currentUserId && fr.ToUserId == userId) ||
                (fr.FromUserId == userId && fr.ToUserId == currentUserId.Value)
            && fr.Status == FriendRequestStatus.Accepted);

        if (acceptedRequest != null)
            _context.FriendRequests.Remove(acceptedRequest);

        await _context.SaveChangesAsync();

        // Gửi toast cho cả 2
        var currentName = _context.Users.Find(currentUserId)?.FullName;
        var otherName = _context.Users.Find(userId)?.FullName;

        await _hubContext.Clients.Group(currentUserId.ToString())
            .SendAsync("FriendRemoved", new { message = $"Bạn đã hủy kết bạn với {otherName}" });

        await _hubContext.Clients.Group(userId.ToString())
            .SendAsync("FriendRemoved", new { message = $"{currentName} đã hủy kết bạn với bạn" });

        return Json(new { success = true });
    }
}
