using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialNetwork.Models;
using System.Collections.Concurrent;
using System.Threading.Tasks;
public class ChatHub : Hub
{
    private static readonly ConcurrentDictionary<int, HashSet<string>> _connections = new();
    private static readonly ConcurrentDictionary<int, int> _activeCallHosts = new();
    private static readonly ConcurrentDictionary<int, HashSet<int>> _activeCallParticipants = new();
    
    // NEW: Track last active times
    public static readonly ConcurrentDictionary<int, DateTime> LastActiveTimes = new();

    private readonly SocialNetworkDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public ChatHub(SocialNetworkDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }
   public override Task OnConnectedAsync()
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId.HasValue)
        {
            Context.Items["UserId"] = userId.Value;

            var wasOffline = !_connections.ContainsKey(userId.Value);
            var set = _connections.GetOrAdd(userId.Value, _ => new HashSet<string>());
            lock (set) set.Add(Context.ConnectionId);

            // Remove from LastActiveTimes because they are now Online
            LastActiveTimes.TryRemove(userId.Value, out _);

            Console.WriteLine($"User {userId.Value} connected (ConnectionId: {Context.ConnectionId})");

            // Chỉ gửi UserOnline nếu đây là lần đầu tiên user này xuất hiện online
            if (wasOffline)
            {
                // Thông báo cho TẤT CẢ client: user này vừa online
                Clients.All.SendAsync("UserOnline", userId.Value);
            }
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        var userId = Context.Items["UserId"] as int?
                   ?? _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");

        if (userId.HasValue)
        {
            if (_connections.TryGetValue(userId.Value, out var set))
            {
                lock (set) set.Remove(Context.ConnectionId);

                if (set.Count == 0)
                {
                    _connections.TryRemove(userId.Value, out _);
                    
                    // Mark as offline with current time
                    LastActiveTimes[userId.Value] = DateTime.UtcNow;

                    Console.WriteLine($"User {userId.Value} went offline");

                    // Thông báo cho TẤT CẢ client: user này đã offline
                    Clients.All.SendAsync("UserOffline", userId.Value);
                    
                    // CLEANUP VIDEO CALLS
                    // Tìm các cuộc gọi mà user này đang tham gia
                    foreach (var kvp in _activeCallParticipants)
                    {
                        var convId = kvp.Key;
                        var participants = kvp.Value;
                        bool removed = false;
                        lock (participants)
                        {
                            if (participants.Contains(userId.Value))
                            {
                                participants.Remove(userId.Value);
                                removed = true;
                            }
                        }

                        if (removed)
                        {
                            // Nếu là host -> End call
                            if (_activeCallHosts.TryGetValue(convId, out int hostId) && hostId == userId.Value)
                            {
                                _activeCallHosts.TryRemove(convId, out _);
                                _activeCallParticipants.TryRemove(convId, out _);
                                
                                // Notify all remaining participants
                                lock (participants)
                                {
                                    foreach (var memberId in participants)
                                    {
                                        if (_connections.TryGetValue(memberId, out var conns))
                                        {
                                            foreach(var conn in conns) Clients.Client(conn).SendAsync("VideoCallEnded", new { conversationId = convId, endedBy = userId.Value });
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Notify others that user left
                                lock (participants)
                                {
                                    foreach (var memberId in participants)
                                    {
                                        if (_connections.TryGetValue(memberId, out var conns))
                                        {
                                            foreach(var conn in conns) Clients.Client(conn).SendAsync("UserLeftCall", new { conversationId = convId, userId = userId.Value });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return base.OnDisconnectedAsync(exception);
    }

    // THÊM METHOD NÀY – ĐỂ CLIENT GỌI KHI MỚI LOAD TRANG
    public Task<List<int>> GetOnlineUsers()
    {
        var onlineUserIds = _connections.Keys.ToList();
        return Task.FromResult(onlineUserIds);
    }
    public async Task SendMessage(int conversationId, string content)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null)
        {
            throw new HubException("Not authenticated");
        }
        var conv = await _db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Participants) // Include participants to check membership
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv == null)
        {
            throw new HubException("Conversation not found");
        }
        
        // Kiểm tra người dùng có phải là thành viên của cuộc trò chuyện
        bool isParticipant = conv.Participants.Any(p => p.UserId == userId.Value);
        if (!isParticipant && conv.User1Id != userId.Value && conv.User2Id != userId.Value)
        {
            throw new HubException("Not a participant");
        }

        // Lưu tin nhắn
        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = userId.Value,
            Content = content,
            CreatedAt = DateTime.Now,
            IsRead = false
        };
        try
        {
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            // Lấy thông tin người gửi
            var sender = await _db.Users.FindAsync(userId.Value);
            var senderName = sender?.FullName ?? "Unknown";
            var senderAvatar = string.IsNullOrEmpty(sender?.ImageUrl) ? "/Uploads/default-avatar.png" : $"/Uploads/{sender.ImageUrl}";

            // Tạo DTO
            var dto = new
            {
                id = msg.Id,
                conversationId = conversationId,
                senderId = msg.SenderId,
                senderName = senderName,
                senderAvatar = senderAvatar,
                content = msg.Content,
                createdAt = msg.CreatedAt.ToString("o"),
                isGroup = conv.IsGroup,
                groupName = conv.IsGroup ? conv.GroupName : null,
                groupAvatar = conv.IsGroup ? conv.GroupAvatar : null,
                groupAdminId = conv.IsGroup ? conv.User1Id : 0
            };

            // Gửi tin nhắn đến tất cả người dùng trong cuộc trò chuyện
            // Lấy danh sách người nhận từ Participants nếu là group, hoặc User1/User2 nếu là private
            IEnumerable<int> recipients;
            if (conv.IsGroup)
            {
                recipients = conv.Participants.Select(p => p.UserId);
            }
            else
            {
                recipients = new[] { conv.User1Id, conv.User2Id }.Distinct();
            }

            foreach (var recipientId in recipients)
            {
                if (_connections.TryGetValue(recipientId, out var conns))
                {
                    List<string> connectionsToNotify;
                    lock (conns)
                    {
                        connectionsToNotify = conns.ToList();
                    }
                    foreach (var connId in connectionsToNotify)
                    {
                        await Clients.Client(connId).SendAsync("ReceiveMessage", dto);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SendMessage: {ex.Message}");
            throw; // Ném lại ngoại lệ để client nhận được thông báo
        }
    }

    public async Task NotifyGroupDeleted(int conversationId, List<int> memberIds)
    {
        foreach (var memberId in memberIds)
        {
            if (_connections.TryGetValue(memberId, out var conns))
            {
                 List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("GroupDeleted", conversationId);
                }
            }
        }
    }

    public async Task NotifyUserLeft(int conversationId, int userId, string userName, List<int> remainingMemberIds)
    {
         foreach (var memberId in remainingMemberIds)
        {
            if (_connections.TryGetValue(memberId, out var conns))
            {
                 List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("UserLeftGroup", new { conversationId, userId, userName });
                }
            }
        }
    }
    public async Task DeleteMessageForEveryone(int messageId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null) throw new HubException("Not authenticated");

        var msg = await _db.Messages
            .Include(m => m.Conversation)
            .ThenInclude(c => c.Messages)
            .Include(m => m.Conversation.Participants) // Include Participants
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (msg == null) throw new HubException("Message not found");
        if (msg.SenderId != userId.Value) throw new HubException("Not allowed");

        var conv = msg.Conversation;
        var convId = conv.Id;

        // XÁC ĐỊNH DANH SÁCH NGƯỜI NHẬN
        IEnumerable<int> recipients;
        if (conv.IsGroup)
        {
            recipients = conv.Participants.Select(p => p.UserId);
        }
        else
        {
            recipients = new[] { conv.User1Id, conv.User2Id }.Distinct();
        }

        // Nếu nội dung là file/ảnh được lưu ở /Uploads/chat → xóa file vật lý
        try
        {
            var content = msg.Content ?? string.Empty;
            string? url = null;
            var imgMatch = System.Text.RegularExpressions.Regex.Match(content, "^\\[img\\](.*)\\[/img\\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var videoMatch = System.Text.RegularExpressions.Regex.Match(content, "^\\[video\\](.*)\\[/video\\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var fileMatch = System.Text.RegularExpressions.Regex.Match(content, "^\\[file\\](.*)\\|(.*)\\[/file\\]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (imgMatch.Success)
            {
                url = imgMatch.Groups[1].Value;
            }
            else if (videoMatch.Success)
            {
                url = videoMatch.Groups[1].Value;
            }
            else if (fileMatch.Success)
            {
                url = fileMatch.Groups[2].Value;
            }

            if (!string.IsNullOrEmpty(url) && url.Replace("\\", "/").Contains("/Uploads/chat/", StringComparison.OrdinalIgnoreCase))
            {
                var webRoot = _httpContextAccessor.HttpContext?.RequestServices.GetService<IWebHostEnvironment>()?.WebRootPath;
                if (!string.IsNullOrEmpty(webRoot))
                {
                    var relative = url.StartsWith("/") ? url.Substring(1) : url;
                    var physical = System.IO.Path.Combine(webRoot, relative.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()));
                    if (System.IO.File.Exists(physical))
                    {
                        System.IO.File.Delete(physical);
                        Console.WriteLine($"Deleted file: {physical}");
                    }
                }
            }
        }
        catch (Exception ex) 
        { 
             Console.WriteLine($"Error deleting message file: {ex.Message}");
        }

        // XÓA TIN NHẮN
        _db.Messages.Remove(msg);
        await _db.SaveChangesAsync();

        // LẤY TIN NHẮN CUỐNG CÙNG MỚI (sau khi xóa)
        var lastMsg = await _db.Messages
            .Where(m => m.ConversationId == convId)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        var lastMsgDto = lastMsg != null ? new
        {
            id = lastMsg.Id,
            content = lastMsg.Content,
            senderId = lastMsg.SenderId,
            createdAt = lastMsg.CreatedAt.ToString("o")
        } : null;

        // GỬI XÓA TIN + CẬP NHẬT LAST MESSAGE CHO TẤT CẢ
        foreach (var recipientId in recipients)
        {
            if (_connections.TryGetValue(recipientId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("MessageDeleted", new
                    {
                        messageId = messageId,
                        conversationId = convId
                    });

                    // GỬI CẬP NHẬT LAST MESSAGE
                    await Clients.Client(connId).SendAsync("UpdateLastMessage", new
                    {
                        conversationId = convId,
                        lastMessage = lastMsgDto
                    });
                }
            }
        }
    }
    // Trả về danh sách userId đang online (đọc-only)
    public static IReadOnlyCollection<int> GetOnlineUserIds()
    {
        return _connections.Keys.ToList();
    }
    // Video Call Signaling Methods
    public async Task<int> InitiateVideoCall(int conversationId, int targetUserId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null) throw new HubException("Not authenticated");
        
        var conv = await _db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
            
        if (conv == null) throw new HubException("Conversation not found");

        // Check participation
        bool isParticipant = false;
        IEnumerable<int> recipients;

        if (conv.IsGroup)
        {
            isParticipant = conv.Participants.Any(p => p.UserId == userId.Value);
            recipients = conv.Participants.Where(p => p.UserId != userId.Value).Select(p => p.UserId);
        }
        else
        {
            isParticipant = (conv.User1Id == userId.Value || conv.User2Id == userId.Value);
            recipients = new[] { conv.User1Id, conv.User2Id }.Where(id => id != userId.Value);
        }

        if (!isParticipant) throw new HubException("Not a participant");

        // Register Host and Participant
        _activeCallHosts[conversationId] = userId.Value;
        var participantsSet = _activeCallParticipants.GetOrAdd(conversationId, _ => new HashSet<int>());
        lock (participantsSet)
        {
            participantsSet.Clear(); // Reset if new call
            participantsSet.Add(userId.Value);
        }

        var recipientsList = recipients.ToList();

        // Gửi thông báo cuộc gọi đến TẤT CẢ người nhận
        foreach (var recipientId in recipientsList)
        {
            if (_connections.TryGetValue(recipientId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("IncomingVideoCall", new
                    {
                        conversationId = conversationId,
                        callerId = userId.Value,
                        callerName = _httpContextAccessor.HttpContext?.Session.GetString("FullName") ?? "Người dùng"
                    });
                }
            }
        }

        return recipientsList.Count;
    }

    public async Task AcceptVideoCall(int conversationId, int callerId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null) throw new HubException("Not authenticated");

        // Add to participants
        var participantsSet = _activeCallParticipants.GetOrAdd(conversationId, _ => new HashSet<int>());
        lock (participantsSet)
        {
            participantsSet.Add(userId.Value);
        }

        // Notify EXISTING participants (excluding self)
        // They will initiate offers to this new user
        List<int> usersToNotify;
        lock (participantsSet)
        {
            usersToNotify = participantsSet.Where(u => u != userId.Value).ToList();
        }

        foreach (var participantId in usersToNotify)
        {
            if (_connections.TryGetValue(participantId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("VideoCallAccepted", new
                    {
                        conversationId = conversationId,
                        answererId = userId.Value
                    });
                }
            }
        }
    }

    public async Task RejectVideoCall(int conversationId, int callerId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null) throw new HubException("Not authenticated");
        
        // For group calls, maybe we don't need to notify everyone if one person rejects.
        // But for 1-1, we do.
        // Let's notify the caller (Host) at least.
        
        if (_connections.TryGetValue(callerId, out var conns))
        {
            List<string> connectionsToNotify;
            lock (conns)
            {
                connectionsToNotify = conns.ToList();
            }
            foreach (var connId in connectionsToNotify)
            {
                await Clients.Client(connId).SendAsync("VideoCallRejected", new
                {
                    conversationId = conversationId,
                    rejecterId = userId.Value
                });
            }
        }
    }

    public async Task EndVideoCall(int conversationId, int otherUserId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null) throw new HubException("Not authenticated");

        bool isHost = false;
        if (_activeCallHosts.TryGetValue(conversationId, out int hostId))
        {
            if (hostId == userId.Value) isHost = true;
        }

        if (isHost)
        {
            // Host ended -> End for everyone
            _activeCallHosts.TryRemove(conversationId, out _);
            _activeCallParticipants.TryRemove(conversationId, out _);

            // Notify ALL participants of the conversation (including those who haven't accepted yet)
            var conv = await _db.Conversations
                .Include(c => c.Participants)
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conv != null)
            {
                IEnumerable<int> recipients;
                if (conv.IsGroup)
                {
                    recipients = conv.Participants.Select(p => p.UserId);
                }
                else
                {
                    recipients = new[] { conv.User1Id, conv.User2Id }.Distinct();
                }

                foreach (var memberId in recipients)
                {
                    if (_connections.TryGetValue(memberId, out var conns))
                    {
                        List<string> connectionsToNotify;
                        lock (conns) connectionsToNotify = conns.ToList();
                        foreach (var connId in connectionsToNotify)
                        {
                            await Clients.Client(connId).SendAsync("VideoCallEnded", new
                            {
                                conversationId = conversationId,
                                endedBy = userId.Value
                            });
                        }
                    }
                }
            }
        }
        else
        {
            // Participant left -> Notify others
            if (_activeCallParticipants.TryGetValue(conversationId, out var participants))
            {
                lock (participants) participants.Remove(userId.Value);
                
                List<int> remainingParticipants;
                lock (participants) remainingParticipants = participants.ToList();

                // Nếu chỉ còn 1 người (host) hoặc không còn ai -> Kết thúc cuộc gọi
                if (remainingParticipants.Count <= 1)
                {
                    // Kết thúc cuộc gọi cho tất cả
                    _activeCallHosts.TryRemove(conversationId, out _);
                    _activeCallParticipants.TryRemove(conversationId, out _);

                    // Gửi VideoCallEnded cho tất cả (bao gồm cả người vừa rời)
                    var allMembers = remainingParticipants.ToList();
                    allMembers.Add(userId.Value); // Thêm người vừa rời

                    foreach (var memberId in allMembers)
                    {
                        if (_connections.TryGetValue(memberId, out var conns))
                        {
                            List<string> connectionsToNotify;
                            lock (conns) connectionsToNotify = conns.ToList();
                            foreach (var connId in connectionsToNotify)
                            {
                                await Clients.Client(connId).SendAsync("VideoCallEnded", new
                                {
                                    conversationId = conversationId,
                                    endedBy = userId.Value
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Còn nhiều người -> Chỉ thông báo người này rời
                    foreach (var memberId in remainingParticipants)
                    {
                        if (_connections.TryGetValue(memberId, out var conns))
                        {
                            List<string> connectionsToNotify;
                            lock (conns) connectionsToNotify = conns.ToList();
                            foreach (var connId in connectionsToNotify)
                            {
                                await Clients.Client(connId).SendAsync("UserLeftCall", new
                                {
                                    conversationId = conversationId,
                                    userId = userId.Value
                                });
                            }
                        }
                    }
                }
            }
        }
    }
    // WebRTC Signaling: Offer
    public async Task SendOffer(int conversationId, int targetUserId, string offer)
    {
        try
        {
            // Lấy userId từ Context.Items (đã lưu khi connect) hoặc từ Session
            int? userId = null;
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int uid)
            {
                userId = uid;
            }
            else
            {
                // Fallback: Thử lấy lại từ Session (ít khi xảy ra nếu OnConnected đã chạy đúng)
                var httpContext = Context.GetHttpContext() ?? _httpContextAccessor.HttpContext;
                userId = httpContext?.Session?.GetInt32("UserId");
            }
            if (userId == null)
            {
                Console.WriteLine("❌ SendOffer: User not authenticated");
                throw new HubException("Not authenticated");
            }
            Console.WriteLine($"📞 SendOffer: User {userId.Value} sending offer to {targetUserId} in conversation {conversationId}");
            if (string.IsNullOrEmpty(offer))
            {
                Console.WriteLine("❌ SendOffer: Offer is null or empty");
                throw new HubException("Offer cannot be null or empty");
            }
            if (_connections.TryGetValue(targetUserId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("ReceiveOffer", new
                    {
                        conversationId = conversationId,
                        fromUserId = userId.Value,
                        offer = offer
                    });
                    Console.WriteLine($"✅ SendOffer: Offer sent to connection {connId}");
                }
            }
            else
            {
                Console.WriteLine($"⚠️ SendOffer: Target user {targetUserId} is not online");
                // Không throw exception, chỉ log warning vì user có thể offline
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SendOffer Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw new HubException($"Error sending offer: {ex.Message}");
        }
    }
    // WebRTC Signaling: Answer
    public async Task SendAnswer(int conversationId, int targetUserId, string answer)
    {
        try
        {
            int? userId = null;
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int uid)
            {
                userId = uid;
            }
            else
            {
                var httpContext = Context.GetHttpContext() ?? _httpContextAccessor.HttpContext;
                userId = httpContext?.Session?.GetInt32("UserId");
            }
            if (userId == null)
            {
                Console.WriteLine("❌ SendAnswer: User not authenticated");
                throw new HubException("Not authenticated");
            }
            if (string.IsNullOrEmpty(answer))
            {
                Console.WriteLine("❌ SendAnswer: Answer is null or empty");
                throw new HubException("Answer cannot be null or empty");
            }
            if (_connections.TryGetValue(targetUserId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("ReceiveAnswer", new
                    {
                        conversationId = conversationId,
                        fromUserId = userId.Value,
                        answer = answer
                    });
                }
            }
            else
            {
                Console.WriteLine($"⚠️ SendAnswer: Target user {targetUserId} is not online");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SendAnswer Error: {ex.Message}");
            throw new HubException($"Error sending answer: {ex.Message}");
        }
    }
    // WebRTC Signaling: ICE Candidate
    public async Task SendIceCandidate(int conversationId, int targetUserId, string candidate, string sdpMid, int? sdpMLineIndex)
    {
        try
        {
            int? userId = null;
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int uid)
            {
                userId = uid;
            }
            else
            {
                var httpContext = Context.GetHttpContext() ?? _httpContextAccessor.HttpContext;
                userId = httpContext?.Session?.GetInt32("UserId");
            }
            if (userId == null)
            {
                throw new HubException("Not authenticated");
            }
            if (_connections.TryGetValue(targetUserId, out var conns))
            {
                List<string> connectionsToNotify;
                lock (conns)
                {
                    connectionsToNotify = conns.ToList();
                }
                foreach (var connId in connectionsToNotify)
                {
                    await Clients.Client(connId).SendAsync("ReceiveIceCandidate", new
                    {
                        conversationId = conversationId,
                        fromUserId = userId.Value,
                        candidate = candidate,
                        sdpMid = sdpMid,
                        sdpMLineIndex = sdpMLineIndex
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SendIceCandidate Error: {ex.Message}");
            // Không throw exception cho ICE candidates vì có thể có nhiều candidates
        }
    }

    public async Task<int> MarkAsRead(int conversationId)
    {
        var userId = _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");
        if (userId == null && Context.Items.TryGetValue("UserId", out var uidObj) && uidObj is int uid)
        {
            userId = uid;
        }

        if (userId == null) throw new HubException("Not authenticated");

        var conv = await _db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv == null) throw new HubException("Conversation not found");

        DateTime? deletedAt = null;
        if (conv.IsGroup)
        {
            var participant = conv.Participants.FirstOrDefault(p => p.UserId == userId);
            if (participant == null) throw new HubException("Not a participant");
            deletedAt = participant.DeletedAt;
        }
        else
        {
            deletedAt = conv.User1Id == userId ? conv.DeletedAtUser1 : conv.DeletedAtUser2;
        }

        var unreadMessages = conv.Messages
            .Where(m => m.SenderId != userId && !m.IsRead && (deletedAt == null || m.CreatedAt > deletedAt))
            .ToList();

        if (unreadMessages.Any())
        {
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }
            await _db.SaveChangesAsync();

            // NOTIFY SENDERS "MessagesRead"
            var senderIds = unreadMessages.Select(m => m.SenderId).Distinct().ToList();
            var readerId = userId.Value;
            
            // Fix: Use _connections dictionary instead of Clients.Users
            foreach (var sId in senderIds)
            {
                 if (_connections.TryGetValue(sId, out var conns))
                 {
                     List<string> connectionsToNotify;
                     lock (conns)
                     {
                         connectionsToNotify = conns.ToList();
                     }
                     
                     if (connectionsToNotify.Any())
                     {
                         await Clients.Clients(connectionsToNotify).SendAsync("MessagesRead", new 
                         {
                             conversationId = conversationId,
                             readerId = readerId,
                             lastReadMessageId = unreadMessages.Max(m => m.Id)
                         });
                    }
                 }
            }
        }

        // Calculate Total Unread Count
         var totalUnread = await _db.Conversations
            .Where(c => c.User1Id == userId || c.User2Id == userId || c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Messages)
            .Include(c => c.Participants)
            .Select(c => new {
                c.IsGroup,
                c.User1Id,
                c.DeletedAtUser1,
                c.DeletedAtUser2,
                Participants = c.Participants.Where(p => p.UserId == userId).Select(p => new { p.JoinedAt, p.DeletedAt }).ToList(),
                Messages = c.Messages.Where(m => m.SenderId != userId && !m.IsRead).Select(m => new { m.CreatedAt }).ToList()
            })
            .AsNoTracking()
            .ToListAsync();

        int count = 0;
        foreach(var c in totalUnread)
        {
             if (c.IsGroup)
             {
                 var p = c.Participants.FirstOrDefault();
                 if (p == null) continue;
                 count += c.Messages.Count(m => m.CreatedAt > p.JoinedAt && (p.DeletedAt == null || m.CreatedAt > p.DeletedAt));
             }
             else
             {
                 var d = c.User1Id == userId ? c.DeletedAtUser1 : c.DeletedAtUser2;
                 count += c.Messages.Count(m => d == null || m.CreatedAt > d);
             }
        }

        return count;
    }
}


