using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;

public class ChatController : Controller
{
    private readonly SocialNetworkDbContext _db;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatController(SocialNetworkDbContext db, IHubContext<ChatHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    public IActionResult Index(int? c)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Index", "Chat") });
        }

        var friendIds = _db.Friendships
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToList();

        var friends = _db.Users.Where(u => friendIds.Contains(u.UserId)).ToList();

        var convs = _db.Conversations
         .Include(c => c.Messages)
         .Include(c => c.Participants)
             .ThenInclude(p => p.User)
         .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
         .ToList() // Lấy ra memory trước
         .Select(c =>
         {
             var isGroup = c.IsGroup;
             var otherUserId = isGroup ? 0 : (c.User1Id == userId ? c.User2Id : c.User1Id);

             // Tính LastMessage và LastMessageTime
             var lastMsg = c.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
             var lastMsgTime = c.Messages.Any() ? c.Messages.Max(m => m.CreatedAt) : c.CreatedAt;

             // Tính UnreadCount an toàn
             int unreadCount = 0;
             if (isGroup)
             {
                 var participant = c.Participants.FirstOrDefault(p => p.UserId == userId.Value);
                 if (participant != null && (participant.DeletedAt == null || lastMsgTime > participant.DeletedAt))
                 {
                     unreadCount = c.Messages.Count(m =>
                         m.SenderId != userId.Value &&
                         !m.IsRead &&
                         m.CreatedAt > participant.JoinedAt &&
                         (participant.DeletedAt == null || m.CreatedAt > participant.DeletedAt.Value));
                 }
             }
             else
             {
                 var deletedAt = c.User1Id == userId.Value ? c.DeletedAtUser1 : c.DeletedAtUser2;
                 var showConv = deletedAt == null || lastMsgTime > deletedAt;
                 if (showConv)
                 {
                     unreadCount = c.Messages.Count(m =>
                         m.SenderId != userId.Value &&
                         !m.IsRead &&
                         (deletedAt == null || m.CreatedAt > deletedAt.Value));
                 }
             }

             // Chỉ giữ lại cuộc trò chuyện nếu:
             // - Là nhóm và mình còn trong nhóm (hoặc chưa xóa hoàn toàn)
             // - Là chat cá nhân và chưa bị xóa hoặc có tin mới sau khi xóa
             bool shouldShow = isGroup
                 ? c.Participants.Any(p => p.UserId == userId.Value && (p.DeletedAt == null || lastMsgTime > p.DeletedAt))
                 : (c.User1Id == userId.Value
                     ? (c.DeletedAtUser1 == null || lastMsgTime > c.DeletedAtUser1)
                     : (c.DeletedAtUser2 == null || lastMsgTime > c.DeletedAtUser2));

             // Chỉ hiển thị chat cá nhân nếu là bạn bè
             if (!isGroup && !friendIds.Contains(otherUserId))
                 shouldShow = false;

             return new
             {
                 Id = c.Id,
                 IsGroup = isGroup,
                 OtherUserId = otherUserId,
                 GroupName = c.GroupName,
                 GroupAvatar = c.GroupAvatar,
                 LastMessage = lastMsg,
                 LastMessageTime = lastMsgTime,
                 UnreadCount = unreadCount,
                 ShouldShow = shouldShow,
                 GroupAdminId = isGroup ? c.User1Id : 0
             };
         })
         .Where(x => x.ShouldShow)
         .OrderByDescending(x => x.LastMessageTime)
         .Select(x => new
         {
             x.Id,
             x.IsGroup,
             x.OtherUserId,
             x.GroupName,
             x.GroupAvatar,
             x.LastMessage,
             x.LastMessageTime,
             x.UnreadCount,
             DisplayName = x.IsGroup
                 ? (x.GroupName ?? "Nhóm chat")
                 : "",
             DisplayAvatar = x.IsGroup
                 ? (x.GroupAvatar ?? "default-group.png")
                 : "",
             x.GroupAdminId
         })
         .ToList();

        string otherUserName = null;
        string otherUserAvatar = null;
        int? otherUserId = null;

        if (c.HasValue)
        {
            var conversation = _db.Conversations.FirstOrDefault(x => x.Id == c.Value);
            if (conversation != null)
            {
                otherUserId = (conversation.User1Id == userId) ? conversation.User2Id : conversation.User1Id;
                
                if (conversation.IsGroup)
                {
                    otherUserName = conversation.GroupName;
                    otherUserAvatar = conversation.GroupAvatar;
                }
                else
                {
                    var otherUser = _db.Users.FirstOrDefault(u => u.UserId == otherUserId);
                    if (otherUser != null)
                    {
                        otherUserName = otherUser.FullName;
                        otherUserAvatar = otherUser.ImageUrl;
                    }
                }
            }
        }


        ViewBag.Friends = friends;
        ViewBag.Conversations = convs.Cast<dynamic>().ToList();
        ViewBag.MyUserId = userId;
        ViewBag.SelectedConversationId = c;
        ViewBag.OtherUserName = otherUserName;
        ViewBag.OtherUserId = otherUserId;
        ViewBag.OtherUserAvatar = otherUserAvatar;
        
        // Pass Last Active Data
        ViewBag.LastActiveTimes = ChatHub.LastActiveTimes;
        ViewBag.OnlineUserIds = ChatHub.GetOnlineUserIds();

        bool isGroup = false;
        int? groupAdminId = null;
        if (c.HasValue)
        {
             var conversation = _db.Conversations.FirstOrDefault(x => x.Id == c.Value);
             if (conversation != null) 
             {
                 isGroup = conversation.IsGroup;
                 if (isGroup) groupAdminId = conversation.User1Id;
             }
        }
        ViewBag.IsGroup = isGroup;
        ViewBag.GroupAdminId = groupAdminId;

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue) return Json(new { success = false });

        var conv = await _db.Conversations
            .Include(c => c.Messages)
                .ThenInclude(m => m.Sender) // QUAN TRỌNG: Include Sender để lấy FullName + ImageUrl
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv == null) return Json(new { success = false });

        bool isParticipant = conv.IsGroup
            ? conv.Participants.Any(p => p.UserId == userId.Value)
            : (conv.User1Id == userId.Value || conv.User2Id == userId.Value);

        if (!isParticipant) return Json(new { success = false });

        var deletedAt = conv.IsGroup
            ? conv.Participants.FirstOrDefault(p => p.UserId == userId.Value)?.DeletedAt
            : (conv.User1Id == userId.Value ? conv.DeletedAtUser1 : conv.DeletedAtUser2);

        var messages = conv.Messages
            .Where(m => deletedAt == null || m.CreatedAt > deletedAt)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.SenderId,
                m.Content,
                createdAt = m.CreatedAt.ToString("o"),
                m.IsRead,
                senderName = m.Sender.FullName,
                senderAvatar = string.IsNullOrEmpty(m.Sender.ImageUrl)
                    ? "/Uploads/default-avatar.png"
                    : $"/Uploads/{m.Sender.ImageUrl}"
            })
            .ToList();

        return Json(new { success = true, data = messages });
    }

    [HttpPost]
    public IActionResult StartConversation([FromBody] int otherUserId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Json(new { success = false, message = "Bạn chưa đăng nhập." });

        bool isFriend = _db.Friendships.Any(f =>
            (f.UserAId == userId && f.UserBId == otherUserId) ||
            (f.UserAId == otherUserId && f.UserBId == userId));

        if (!isFriend)
            return Json(new { success = false, message = "Chưa kết bạn" });

        int user1 = Math.Min(userId.Value, otherUserId);
        int user2 = Math.Max(userId.Value, otherUserId);

        var conversation = _db.Conversations.FirstOrDefault(c =>
            c.User1Id == user1 && c.User2Id == user2);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                User1Id = user1,
                User2Id = user2,
                CreatedAt = DateTime.Now
            };
            _db.Conversations.Add(conversation);
            _db.SaveChanges();
        }

        return Json(new { success = true, conversationId = conversation.Id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteConversation([FromBody] int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue)
            return Json(new { success = false, message = "Chưa đăng nhập." });

        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv == null)
            return Json(new { success = false, message = "Không tìm thấy cuộc trò chuyện." });

        // Kiểm tra quyền
        bool isParticipant = conv.IsGroup
            ? conv.Participants.Any(p => p.UserId == userId.Value)
            : (conv.User1Id == userId.Value || conv.User2Id == userId.Value);

        if (!isParticipant)
            return Json(new { success = false, message = "Bạn không thuộc cuộc trò chuyện này." });

        try
        {
            if (conv.IsGroup)
            {
                // === NHÓM: Chỉ đánh dấu đã xóa ở phía mình (không xóa participant) ===
                var participant = conv.Participants.First(p => p.UserId == userId.Value);
                participant.DeletedAt = DateTime.Now; // Cần có cột này
            }
            else
            {
                // === CHAT CÁ NHÂN: Như cũ ===
                if (conv.User1Id == userId)
                    conv.DeletedAtUser1 = DateTime.Now;
                else
                    conv.DeletedAtUser2 = DateTime.Now;
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true, message = "Đã xóa cuộc trò chuyện khỏi danh sách." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DeleteConversation Error: {ex.Message}");
            return Json(new { success = false, message = "Lỗi khi xóa." });
        }
    }

    [HttpPost]
    public IActionResult MarkAsRead([FromBody] int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Json(new { success = false, message = "Chưa đăng nhập" });

        var conv = _db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .FirstOrDefault(c => c.Id == conversationId);

        if (conv == null)
            return Json(new { success = false, message = "Không tìm thấy đoạn chat" });

        DateTime? deletedAt = null;
        if (conv.IsGroup)
        {
            var participant = conv.Participants.FirstOrDefault(p => p.UserId == userId);
            if (participant == null)
                return Json(new { success = false, message = "Không thuộc nhóm này" });
            deletedAt = participant.DeletedAt;
        }
        else
        {
            deletedAt = conv.User1Id == userId ? conv.DeletedAtUser1 : conv.DeletedAtUser2;
        }

        var unreadMessages = conv.Messages
            .Where(m => m.SenderId != userId && !m.IsRead && (deletedAt == null || m.CreatedAt > deletedAt))
            .ToList();

        foreach (var msg in unreadMessages)
            msg.IsRead = true;

        if (unreadMessages.Count > 0)
        {
            _db.SaveChanges();

            // Notify Sender that messages have been read
            var senderId = unreadMessages.First().SenderId; // Taking one sender (usually simpler in 1-1)
            // In group, there might be multiple senders, but typically MarkAsRead is for the whole conversation.
            // Let's notify all participants to update their UI (e.g. "Seen" status)
            
            // Get all unique senders of the unread messages
            var senderIds = unreadMessages.Select(m => m.SenderId).Distinct().ToList();
            
            // Also notify the user who just read (to update their own total unread count across devices if needed) - handled by return value usually.
            
            // We need to notify the SENDERS that their messages were read by THIS user.
            var readerId = userId.Value;
            var readerName = HttpContext.Session.GetString("FullName") ?? "Someone";

             // Notify via Hub
             // We need to get connection IDs for these users
             // Note: Controller doesn't have direct access to _connections dictionary in Hub.
             // We can use Clients.User(userId) which SignalR handles if IUserIdProvider is set up, 
             // OR we just use Clients.Users(...)
            
            var senderIdStrings = senderIds.Select(id => id.ToString()).ToList();
            if (senderIdStrings.Any())
            {
                 // Send "MessagesRead" event
                 // data: conversationId, readerId, messageIds (optional, or just "all up to now")
                 _hubContext.Clients.Users(senderIdStrings).SendAsync("MessagesRead", new 
                 {
                     conversationId = conversationId,
                     readerId = readerId,
                     // readerName = readerName, 
                     // Sending the ID of the last read message can be helpful
                     lastReadMessageId = unreadMessages.Max(m => m.Id)
                 });
            }
        }

        var newUnreadCount = _db.Conversations
            .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .AsEnumerable()
            .Sum(c => {
                if (c.IsGroup)
                {
                    var participant = c.Participants.FirstOrDefault(p => p.UserId == userId.Value);
                    if (participant == null) return 0;
                    
                    var lastMsgTime = c.Messages.Any() ? c.Messages.Max(m => m.CreatedAt) : c.CreatedAt;
                    if (participant.DeletedAt != null && lastMsgTime <= participant.DeletedAt) return 0;
                    
                    return c.Messages.Count(m =>
                        m.SenderId != userId.Value &&
                        !m.IsRead &&
                        m.CreatedAt > participant.JoinedAt &&
                        (participant.DeletedAt == null || m.CreatedAt > participant.DeletedAt.Value));
                }
                else
                {
                    var deletedAt = c.User1Id == userId ? c.DeletedAtUser1 : c.DeletedAtUser2;
                    return c.Messages.Count(m => 
                        m.SenderId != userId && 
                        !m.IsRead && 
                        (deletedAt == null || m.CreatedAt > deletedAt));
                }
            });

        return Json(new { success = true, count = unreadMessages.Count, totalUnread = newUnreadCount });
    }

    [HttpGet]
    public IActionResult GetUnreadMessageCount()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Json(new { success = false, message = "Bạn chưa đăng nhập." });

        var unreadCount = _db.Conversations
            .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .AsEnumerable()
            .Sum(c => {
                if (c.IsGroup)
                {
                    var participant = c.Participants.FirstOrDefault(p => p.UserId == userId.Value);
                    if (participant == null) return 0;
                    
                    var lastMsgTime = c.Messages.Any() ? c.Messages.Max(m => m.CreatedAt) : c.CreatedAt;
                    if (participant.DeletedAt != null && lastMsgTime <= participant.DeletedAt) return 0;
                    
                    return c.Messages.Count(m =>
                        m.SenderId != userId.Value &&
                        !m.IsRead &&
                        m.CreatedAt > participant.JoinedAt &&
                        (participant.DeletedAt == null || m.CreatedAt > participant.DeletedAt.Value));
                }
                else
                {
                    var deletedAt = c.User1Id == userId ? c.DeletedAtUser1 : c.DeletedAtUser2;
                    return c.Messages.Count(m => 
                        m.SenderId != userId && 
                        !m.IsRead && 
                        (deletedAt == null || m.CreatedAt > deletedAt));
                }
            });

        return Json(new { success = true, unread = unreadCount });
    }

    [HttpGet]
    public IActionResult GetUnreadMessages()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Json(new { success = false, message = "Bạn chưa đăng nhập." });

        var conversations = _db.Conversations
            .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Messages)
                .ThenInclude(m => m.Sender)
            .Include(c => c.User1)
            .Include(c => c.User2)
            .Include(c => c.Participants)
            .ToList();

        var unreadMessages = conversations
            .SelectMany(c => {
                DateTime? deletedAt = null;
                
                if (c.IsGroup)
                {
                    var participant = c.Participants.FirstOrDefault(p => p.UserId == userId);
                    if (participant == null) return Enumerable.Empty<dynamic>();
                    deletedAt = participant.DeletedAt;
                }
                else
                {
                    deletedAt = c.User1Id == userId ? c.DeletedAtUser1 : c.DeletedAtUser2;
                }
                
                return c.Messages
                    .Where(m => deletedAt == null || m.CreatedAt > deletedAt)
                    .Select(m => new { Message = m, Conversation = c });
            })
            .Where(x => x.Message.SenderId != userId && !x.Message.IsRead)
            .OrderByDescending(x => x.Message.CreatedAt)
            .Select(x => new
            {
                conversationId = x.Message.ConversationId,
                isGroup = x.Conversation.IsGroup,
                groupName = x.Conversation.IsGroup ? x.Conversation.GroupName : null,
                groupAvatar = x.Conversation.IsGroup ? x.Conversation.GroupAvatar : null,
                message = new
                {
                    x.Message.Id,
                    x.Message.SenderId,
                    x.Message.Content,
                    createdAt = x.Message.CreatedAt.ToString("o"),
                    x.Message.IsRead
                },
                sender = new
                {
                    fullName = x.Message.Sender.FullName,
                    avatar = string.IsNullOrEmpty(x.Message.Sender.ImageUrl)
                        ? "/Uploads/default-avatar.png"
                        : "/Uploads/" + x.Message.Sender.ImageUrl
                }
            })
            .ToList();

        return Json(new { success = true, data = unreadMessages });
    }

    [HttpPost]
    public IActionResult MarkAllMessagesAsRead()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Json(new { success = false, message = "Chưa đăng nhập" });

        var unreadMessages = _db.Conversations
            .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .AsEnumerable()
            .SelectMany(c => {
                if (c.IsGroup)
                {
                    var participant = c.Participants.FirstOrDefault(p => p.UserId == userId.Value);
                    if (participant == null) return Enumerable.Empty<Message>();
                    
                    return c.Messages.Where(m => 
                        m.SenderId != userId &&
                        !m.IsRead &&
                        m.CreatedAt > participant.JoinedAt &&
                        (participant.DeletedAt == null || m.CreatedAt > participant.DeletedAt.Value));
                }
                else
                {
                    var deletedAt = c.User1Id == userId ? c.DeletedAtUser1 : c.DeletedAtUser2;
                    return c.Messages.Where(m => 
                        m.SenderId != userId && 
                        !m.IsRead && 
                        (deletedAt == null || m.CreatedAt > deletedAt));
                }
            })
            .ToList();

        foreach (var msg in unreadMessages)
            msg.IsRead = true;

        if (unreadMessages.Count > 0)
            _db.SaveChanges();

        return Json(new { success = true, count = unreadMessages.Count });
    }

    [HttpPost]
    public async Task<IActionResult> UploadAttachment(int conversationId, IFormFile file)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return Json(new { success = false, message = "Chưa đăng nhập" });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File không hợp lệ" });

        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv == null) return Json(new { success = false, message = "Không tìm thấy đoạn chat" });
        
        bool isParticipant = conv.Participants.Any(p => p.UserId == userId);
        if (!isParticipant && conv.User1Id != userId && conv.User2Id != userId)
            return Json(new { success = false, message = "Không thuộc cuộc trò chuyện" });

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads", "chat");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var safeName = Path.GetFileNameWithoutExtension(file.FileName);
        var ext = Path.GetExtension(file.FileName);
        var newName = $"{Guid.NewGuid()}{ext}";
        var path = Path.Combine(uploadsDir, newName);
        using (var stream = new FileStream(path, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var publicUrl = $"/Uploads/chat/{newName}";
        var isImage = file.ContentType != null && file.ContentType.StartsWith("image");
        var isVideo = file.ContentType != null && file.ContentType.StartsWith("video");
        
        string contentFormat;
        if (isImage)
            contentFormat = $"[img]{publicUrl}[/img]";
        else if (isVideo)
            contentFormat = $"[video]{publicUrl}[/video]";
        else
            contentFormat = $"[file]{safeName}{ext}|{publicUrl}[/file]";

        return Json(new { success = true, url = publicUrl, name = safeName + ext, type = isImage ? "image" : (isVideo ? "video" : "file"), content = contentFormat });
    }

    [HttpPost]
    public IActionResult CreateGroup([FromBody] CreateGroupRequest request)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return Json(new { success = false, message = "Chưa đăng nhập" });

        if (string.IsNullOrEmpty(request.GroupName) || request.MemberIds == null || request.MemberIds.Count < 2)
        {
            return Json(new { success = false, message = "Tên nhóm và ít nhất 2 thành viên khác là bắt buộc." });
        }

        var conversation = new Conversation
        {
            IsGroup = true,
            GroupName = request.GroupName,
            CreatedAt = DateTime.Now,
            User1Id = userId.Value,
            User2Id = userId.Value // Fixed: Set to creator's ID to satisfy FK
        };

        _db.Conversations.Add(conversation);
        _db.SaveChanges(); // Save để lấy Id
        // Add creator
        _db.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userId.Value,
            Role = "Admin"
        });

        // Add members
        foreach (var memberId in request.MemberIds)
        {
            if (memberId != userId.Value) // Tránh duplicate
            {
                _db.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = memberId,
                    Role = "Member"
                });
            }
        }

        _db.SaveChanges();

        return Json(new { success = true, conversationId = conversation.Id });
    }

    [HttpPost]
    public async Task<IActionResult> LeaveGroup([FromBody] int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue)
            return Json(new { success = false, message = "Chưa đăng nhập" });

        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.IsGroup);

        if (conv == null)
            return Json(new { success = false, message = "Không tìm thấy nhóm" });

        var participant = conv.Participants.FirstOrDefault(p => p.UserId == userId.Value);
        if (participant == null)
            return Json(new { success = false, message = "Bạn không trong nhóm này" });

        // Lấy danh sách thành viên trước khi xóa (để gửi SignalR)
        var allMemberIds = conv.Participants.Select(p => p.UserId).ToList();

        // Kiểm tra còn ai không (trước khi xóa)
        var remainingCount = conv.Participants.Count(p => p.UserId != userId.Value);

        if (remainingCount == 0)
        {
            // Không còn ai → XÓA HOÀN TOÀN NHÓM KHỎI DATABASE
            _db.ConversationParticipants.RemoveRange(conv.Participants);
            _db.Conversations.Remove(conv);

            await _db.SaveChangesAsync();

            // Thông báo cho tất cả (kể cả mình) rằng nhóm đã biến mất
            await _hubContext.Clients.Users(allMemberIds.Select(x => x.ToString()).ToList())
                .SendAsync("GroupDeleted", conversationId);

            return Json(new { success = true, groupDeleted = true });
        }
        else
        {
            // Còn người → tạo tin nhắn hệ thống TRƯỚC khi xóa participant
            var user = await _db.Users.FindAsync(userId.Value);
            var userName = user?.FullName ?? "Một thành viên";

            // 1. TẠO TIN NHẮN HỆ THỐNG (TRƯỚC KHI XÓA PARTICIPANT)
            var systemMsg = new Message
            {
                ConversationId = conversationId,
                SenderId = userId.Value,
                Content = $"[system]{userName} đã rời nhóm[/system]",
                CreatedAt = DateTime.Now,
                IsRead = false
            };
            _db.Messages.Add(systemMsg);
            
            // 2. LƯU TIN NHẮN TRƯỚC
            await _db.SaveChangesAsync();

            // 3. GỬI TIN NHẮN CHO TẤT CẢ (bao gồm cả người rời nhóm) - TRƯỚC KHI XÓA
            var msgDto = new
            {
                id = systemMsg.Id,
                conversationId = conversationId,
                senderId = userId.Value,
                senderName = userName,
                senderAvatar = string.IsNullOrEmpty(user?.ImageUrl) ? "/Uploads/default-avatar.png" : $"/Uploads/{user.ImageUrl}",
                content = systemMsg.Content,
                createdAt = systemMsg.CreatedAt.ToString("o"),
                isGroup = true,
                groupName = conv.GroupName,
                groupAvatar = conv.GroupAvatar
            };

            // Gửi cho tất cả thành viên (bao gồm cả người rời)
            await _hubContext.Clients.Users(allMemberIds.Select(x => x.ToString()).ToList())
                .SendAsync("ReceiveMessage", msgDto);

            // 4. BÂY GIỜ MỚI XÓA PARTICIPANT
            _db.ConversationParticipants.Remove(participant);
            await _db.SaveChangesAsync();

            var remainingMemberIds = conv.Participants
                .Where(p => p.UserId != userId.Value)
                .Select(p => p.UserId)
                .ToList();

            // 3. GỬI SỰ KIỆN UserLeftGroup cho người rời nhóm (để xóa conversation khỏi danh sách)
            await _hubContext.Clients.User(userId.Value.ToString())
                .SendAsync("UserLeftGroup", new
                {
                    conversationId,
                    userId = userId.Value,
                    userName = userName,
                    isSelf = true // Đánh dấu là chính mình rời
                });

            // 4. GỬI SỰ KIỆN UserLeftGroup cho các thành viên còn lại (để cập nhật danh sách thành viên)
            await _hubContext.Clients.Users(remainingMemberIds.Select(x => x.ToString()).ToList())
                .SendAsync("UserLeftGroup", new
                {
                    conversationId,
                    userId = userId.Value,
                    userName = userName,
                    isSelf = false
                });

            return Json(new { success = true, groupDeleted = false });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DisbandGroup([FromBody] int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue)
            return Json(new { success = false, message = "Chưa đăng nhập" });

        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.IsGroup);

        if (conv == null)
            return Json(new { success = false, message = "Không tìm thấy nhóm" });

        // Kiểm tra quyền: chỉ admin (người tạo nhóm) mới được giải tán
        if (conv.User1Id != userId.Value)
            return Json(new { success = false, message = "Chỉ trưởng nhóm mới có thể giải tán nhóm" });

        // QUAN TRỌNG: LẤY DANH SÁCH THÀNH VIÊN TRƯỚC KHI XÓA!
        var memberUserIds = conv.Participants.Select(p => p.UserId).ToList();

        if (!memberUserIds.Any())
            return Json(new { success = false, message = "Nhóm không có thành viên" });

        // XÓA DỮ LIỆU
        _db.ConversationParticipants.RemoveRange(conv.Participants);
        _db.Conversations.Remove(conv);
        await _db.SaveChangesAsync();

        // GỬI THÔNG BÁO REALTIME CHO TẤT CẢ THÀNH VIÊN (kể cả người giải tán)
        var memberIdsStr = memberUserIds.Select(id => id.ToString()).ToList();

        await _hubContext.Clients.Users(memberIdsStr)
            .SendAsync("GroupDeleted", conversationId);

        // (Tùy chọn) Gửi tin hệ thống để đẹp hơn
        await _hubContext.Clients.Users(memberIdsStr)
            .SendAsync("ReceiveMessage", new
            {
                conversationId,
                senderId = 0,
                senderName = "Hệ thống",
                senderAvatar = "/Uploads/system.png",
                content = "Nhóm đã bị giải tán bởi trưởng nhóm",
                createdAt = DateTime.UtcNow.ToString("o"),
                isGroup = true
            });

        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetGroupMembers(int conversationId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (!userId.HasValue) return Json(new { success = false, message = "Chưa đăng nhập" });

        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.IsGroup);

        if (conv == null) return Json(new { success = false, message = "Không tìm thấy nhóm" });

        // Check if user is participant
        if (!conv.Participants.Any(p => p.UserId == userId.Value))
            return Json(new { success = false, message = "Bạn không thuộc nhóm này" });

        var members = conv.Participants.Select(p => new
        {
            userId = p.UserId,
            fullName = p.User.FullName,
            avatar = string.IsNullOrEmpty(p.User.ImageUrl) ? "/Uploads/default-avatar.png" : $"/Uploads/{p.User.ImageUrl}",
            role = p.Role, // "Admin" or "Member"
            isAdmin = p.Role == "Admin"
        }).ToList();

        return Json(new { success = true, data = members });
    }
    public class CreateGroupRequest
    {
        public string GroupName { get; set; }
        public List<int> MemberIds { get; set; }
    }
}