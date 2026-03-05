using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Hubs;
using SocialNetwork.Models;

namespace SocialNetwork.Controllers
{
    public class PostController : Controller
    {
        private readonly SocialNetworkDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<NotificationHub> _hubContext;

        public PostController(SocialNetworkDbContext context, IWebHostEnvironment env, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _env = env;
            _hubContext = hubContext;
        }

        // Bảng tin (Feed)
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            
            // Lấy danh sách bạn bè
            var friendIds = new List<int>();
            if (userId.HasValue)
            {
                friendIds = await _context.Friendships
                    .Where(f => f.UserAId == userId || f.UserBId == userId)
                    .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
                    .ToListAsync();
            }

            var posts = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Comments).ThenInclude(c => c.User)
                .Include(p => p.Likes)
                .Include(p => p.OriginalPost).ThenInclude(op => op.User)
                .Where(p => 
                    // Public: Ai cũng thấy
                    p.Visibility == "Public" ||
                    // Friends: Chỉ bạn bè và tác giả thấy
                    (p.Visibility == "Friends" && userId.HasValue && 
                     (p.UserId == userId.Value || friendIds.Contains(p.UserId))) ||
                    // Private: Chỉ tác giả thấy
                    (p.Visibility == "Private" && userId.HasValue && p.UserId == userId.Value)
                )
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(posts);
        }
        // Hiển thị chi tiết bài viết
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            
            var post = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Likes)
                .Include(p => p.Comments).ThenInclude(c => c.User)
                .Include(p => p.OriginalPost).ThenInclude(op => op.User)
                .FirstOrDefaultAsync(p => p.PostId == id);

            if (post == null)
                return NotFound();

            // Kiểm tra quyền xem
            if (post.Visibility == "Private" && post.UserId != userId)
                return Forbid();

            if (post.Visibility == "Friends")
            {
                if (!userId.HasValue)
                    return RedirectToAction("Login", "Account");

                if (post.UserId != userId.Value)
                {
                    var isFriend = await _context.Friendships
                        .AnyAsync(f => 
                            (f.UserAId == userId && f.UserBId == post.UserId) ||
                            (f.UserAId == post.UserId && f.UserBId == userId));

                    if (!isFriend)
                        return Forbid();
                }
            }

            return View(post);
        }

        // Đăng bài (form trong layout modal submit tới đây)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string content, string visibility, IFormFile? media, string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(content) && media == null)
            {
                TempData["PostError"] = "Bài viết phải có nội dung hoặc ảnh/video!";
                return LocalRedirect(returnUrl ?? Url.Action("Index", "Post")!);
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            string? mediaPath = null;
            if (media != null && media.Length > 0)
            {
                string uploadDir = Path.Combine(_env.WebRootPath, "uploads/posts");
                if (!Directory.Exists(uploadDir))
                {
                    Directory.CreateDirectory(uploadDir);
                }

                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(media.FileName);
                string filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await media.CopyToAsync(stream);
                }

                mediaPath = "/uploads/posts/" + fileName;
            }

            var post = new Post
            {
                UserId = userId.Value,
                Content = content,
                Visibility = visibility,
                MediaUrl = mediaPath,
                CreatedAt = DateTime.Now
            };

            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            // 🔑 redirect về trang gọi (nếu không có thì quay về Index)
            return LocalRedirect(returnUrl ?? Url.Action("Index", "Post")!);
        }

        // Like bài viết
        [HttpPost]
        public async Task<IActionResult> Like([FromBody] int postId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var post = await _context.Posts.FirstOrDefaultAsync(p => p.PostId == postId);
            if (post == null) return NotFound();

            var liked = await _context.Likes
                .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId);

            bool isLiked;
            if (liked != null)
            {
                _context.Likes.Remove(liked); // Bỏ like
                isLiked = false;
            }
            else
            {
                _context.Likes.Add(new Like
                {
                    PostId = postId,
                    UserId = userId.Value,
                    CreatedAt = DateTime.Now
                });
                isLiked = true;

                // Gửi thông báo cho chủ bài viết (nếu không phải chính họ like)
                if (post.UserId != userId)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                    string message = $" đã thích bài viết của bạn";

                    var notification = new Notification
                    {
                        UserId = post.UserId,
                        FromUserId = userId.Value,
                        PostId = postId,
                        Type = "like",
                        Message = message,
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    };

                    _context.Notifications.Add(notification);
                    await _context.SaveChangesAsync(); // Lưu notification vào DB

                    var notificationData = new
                    {
                        Id = notification.Id,
                        Type = "like",
                        Message = message,
                        PostId = postId,
                        FriendRequestId = 0,
                        IsRead = false,
                        createdAt = notification.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                        fromUser = new
                        {
                            UserId = userId,
                            name = user?.FullName ?? "Ẩn danh",
                            avatar = string.IsNullOrEmpty(user?.ImageUrl)
                                ? "/Uploads/default-avatar.png"
                                : "/Uploads/" + user.ImageUrl
                        }
                    };

                    // Gửi qua Group thay vì User (vì không auth)
                    await _hubContext.Clients.Group(post.UserId.ToString()).SendAsync("ReceiveNotification", notificationData);
                }
            }

            await _context.SaveChangesAsync();

            var likeCount = await _context.Likes.CountAsync(l => l.PostId == postId);

            string showText;
            if (isLiked)
            {
                showText = likeCount > 1
                    ? $"Bạn và {likeCount - 1} người khác"
                    : "Bạn";
            }
            else
            {
                showText = $"{likeCount} lượt thích";
            }

            return Json(new
            {
                success = true,
                likes = likeCount,
                isLiked,
                showText
            });
        }

        // Chia sẻ bài viết
        [HttpPost]
        public async Task<IActionResult> Share(int postId, string? content)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var originalPost = await _context.Posts.FindAsync(postId);
            if (originalPost == null) return NotFound();

            // Nếu bài viết gốc là bài chia sẻ, thì lấy bài gốc của nó (tránh chia sẻ lồng nhau quá nhiều cấp)
            // Hoặc đơn giản là cho phép chia sẻ của chia sẻ. 
            // Facebook cho phép chia sẻ bài chia sẻ, nhưng nội dung gốc vẫn là bài gốc nhất.
            // Để đơn giản, ta sẽ trỏ thẳng tới bài gốc thực sự nếu postId là bài chia sẻ.
            int realOriginalId = originalPost.OriginalPostId ?? originalPost.PostId;

            var newPost = new Post
            {
                UserId = userId.Value,
                Content = content,
                Visibility = "Public", // Mặc định Public
                CreatedAt = DateTime.Now,
                OriginalPostId = realOriginalId
            };

            _context.Posts.Add(newPost);
            await _context.SaveChangesAsync();

            // Gửi thông báo cho chủ bài viết gốc
            // Lưu ý: realOriginalId là ID của bài gốc. Cần lấy User của bài đó.
            var rootPost = await _context.Posts.FindAsync(realOriginalId);
            if (rootPost != null && rootPost.UserId != userId)
            {
                var user = await _context.Users.FindAsync(userId);
                string message = $" đã chia sẻ bài viết của bạn";

                var notification = new Notification
                {
                    UserId = rootPost.UserId,
                    FromUserId = userId.Value,
                    PostId = newPost.PostId, // Link tới bài mới chia sẻ
                    Type = "share",
                    Message = message,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                var notificationData = new
                {
                    Id = notification.Id,
                    Type = "share",
                    Message = message,
                    PostId = newPost.PostId,
                    FriendRequestId = 0,
                    IsRead = false,
                    createdAt = notification.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                    fromUser = new
                    {
                        UserId = userId,
                        name = user?.FullName ?? "Ẩn danh",
                        avatar = string.IsNullOrEmpty(user?.ImageUrl)
                            ? "/Uploads/default-avatar.png"
                            : "/Uploads/" + user.ImageUrl
                    }
                };

                await _hubContext.Clients.Group(rootPost.UserId.ToString()).SendAsync("ReceiveNotification", notificationData);
            }

            return Json(new { success = true });
        }

        // Bình luận
        [HttpPost]
        public async Task<IActionResult> Comment([FromBody] CommentDto dto)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Content)) return BadRequest("Nội dung trống");

            var post = await _context.Posts.FirstOrDefaultAsync(p => p.PostId == dto.PostId);
            if (post == null) return NotFound();

            var comment = new Comment
            {
                PostId = dto.PostId,
                UserId = userId.Value,
                Content = dto.Content,
                CreatedAt = DateTime.Now
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            // Gửi thông báo cho chủ bài viết (nếu không phải chính họ comment)
            if (post.UserId != userId)
            {
                string message = $" đã bình luận bài viết của bạn: {dto.Content}";

                var notification = new Notification
                {
                    UserId = post.UserId,
                    FromUserId = userId.Value,
                    PostId = dto.PostId,
                    Type = "comment",
                    Message = message,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync(); // Lưu notification vào DB

                var notificationData = new
                {
                    Id = notification.Id,
                    Type = "comment",
                    Message = message,
                    PostId = dto.PostId,
                    FriendRequestId = 0,
                    IsRead = false,
                    createdAt = notification.CreatedAt.ToString("HH:mm dd/MM/yyyy"),
                    fromUser = new
                    {
                        UserId = userId,
                        name = user?.FullName ?? "Ẩn danh",
                        avatar = string.IsNullOrEmpty(user?.ImageUrl)
                            ? "/Uploads/default-avatar.png"
                            : "/Uploads/" + user.ImageUrl
                    }
                };

                // Gửi qua Group thay vì User
                await _hubContext.Clients.Group(post.UserId.ToString()).SendAsync("ReceiveNotification", notificationData);
            }

            var commentCount = await _context.Comments.CountAsync(c => c.PostId == dto.PostId);

            return Json(new
            {
                success = true,
                username = user?.FullName ?? "Ẩn danh",
                commentId = comment.CommentId,
                avatarUrl = string.IsNullOrEmpty(user?.ImageUrl)
                    ? "/Uploads/default-avatar.png"
                    : "/Uploads/" + user.ImageUrl,
                content = dto.Content,
                createdAt = comment.CreatedAt?.ToString("o"),
                comments = commentCount,
                canDelete = true
            });
        }

        public class CommentDto
        {
            public int PostId { get; set; }
            public string Content { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int postId, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var post = await _context.Posts
                .Include(p => p.Comments)
                .Include(p => p.Likes)
                .FirstOrDefaultAsync(p => p.PostId == postId);

            if (post == null)
                return NotFound();

            if (post.UserId != userId)
                return Forbid();

            // ✅ Xóa file media nếu có
            if (!string.IsNullOrEmpty(post.MediaUrl))
            {
                string filePath = Path.Combine(_env.WebRootPath, post.MediaUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            // ✅ Xóa liên kết con (tránh lỗi FK)
            if (post.Comments != null && post.Comments.Any())
                _context.Comments.RemoveRange(post.Comments);
            if (post.Likes != null && post.Likes.Any())
                _context.Likes.RemoveRange(post.Likes);

            // ✅ Xóa Notifications liên quan tới bài viết (tránh lỗi FK_Notifications_Post_PostId)
            var relatedNotifications = await _context.Notifications
                .Where(n => n.PostId == postId)
                .ToListAsync();
            if (relatedNotifications.Any())
                _context.Notifications.RemoveRange(relatedNotifications);

            _context.Posts.Remove(post);
            await _context.SaveChangesAsync();

            // ✅ AJAX Delete → trả JSON
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true });

            // ✅ Nếu có returnUrl → quay lại đúng trang trước
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // ✅ Mặc định quay về danh sách post
            return RedirectToAction("Index", "Post");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteComment([FromBody] int commentId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var comment = await _context.Comments.Include(c => c.Post).FirstOrDefaultAsync(c => c.CommentId == commentId);
            if (comment == null) return NotFound();

            // Kiểm tra quyền xóa: Nếu là chủ bình luận hoặc chủ bài viết
            if (comment.UserId != userId && comment.Post.UserId != userId)
            {
                return Forbid();
            }

            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync();

            // Trả về số comment còn lại để cập nhật UI
            var post = await _context.Posts
                .Include(p => p.Comments)
                .FirstOrDefaultAsync(p => p.PostId == comment.PostId);

            return Json(new { success = true, comments = post?.Comments.Count ?? 0, postId = post?.PostId });
        }

        // Chỉnh sửa bài viết - GET
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account");

            var post = await _context.Posts
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PostId == id);

            if (post == null)
                return NotFound();

            // Chỉ tác giả mới được sửa
            if (post.UserId != userId.Value)
                return Forbid();

            return View(post);
        }

        // Chỉnh sửa bài viết - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string content, string visibility, 
            IFormFile? media, bool removeMedia = false)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
                return RedirectToAction("Login", "Account");

            var post = await _context.Posts.FindAsync(id);
            if (post == null)
                return NotFound();

            // Chỉ tác giả mới được sửa
            if (post.UserId != userId.Value)
                return Forbid();

            // Cập nhật nội dung
            post.Content = content;
            post.Visibility = visibility;

            // Xử lý xóa media cũ
            if (removeMedia && !string.IsNullOrEmpty(post.MediaUrl))
            {
                var oldPath = Path.Combine(_env.WebRootPath, post.MediaUrl.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
                post.MediaUrl = null;
            }

            // Upload media mới
            if (media != null && media.Length > 0)
            {
                // Xóa media cũ nếu có
                if (!string.IsNullOrEmpty(post.MediaUrl))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, post.MediaUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                string uploadDir = Path.Combine(_env.WebRootPath, "uploads/posts");
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(media.FileName);
                string filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await media.CopyToAsync(stream);
                }

                post.MediaUrl = "/uploads/posts/" + fileName;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Đã cập nhật bài viết!";
            return RedirectToAction("Details", new { id = post.PostId });
        }
    }
}
