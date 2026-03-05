using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;
using System.Security.Claims;

namespace SocialNetwork.Controllers
{
    public class StoryController : Controller
    {
        private readonly SocialNetworkDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public StoryController(SocialNetworkDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpPost]
        public async Task<IActionResult> Create(IFormFile media, string content = "")
        {
            var userIdStr = HttpContext.Session.GetInt32("UserId");
            if (userIdStr == null) return Unauthorized();
            int userId = userIdStr.Value;

            if (media == null || media.Length == 0) return BadRequest("Vui lòng chọn ảnh hoặc video.");

            // Upload file
            string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + media.FileName;
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await media.CopyToAsync(fileStream);
            }

            string mediaType = media.ContentType.StartsWith("video") ? "Video" : "Image";

            var story = new Story
            {
                UserId = userId,
                MediaUrl = "/uploads/" + uniqueFileName,
                Type = mediaType,
                Content = content,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(24)
            };

            _context.Stories.Add(story);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetFriendStories()
        {
            var userIdStr = HttpContext.Session.GetInt32("UserId");
            if (userIdStr == null) return Json(new { success = false });
            int userId = userIdStr.Value;

            // Lấy danh sách bạn bè
            var friendIds = await _context.Friendships
                .Where(f => (f.UserAId == userId || f.UserBId == userId))
                .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
                .ToListAsync();

            friendIds.Add(userId); // Thêm chính mình vào để xem story của mình

            var stories = await _context.Stories
                .Include(s => s.User)
                .Where(s => friendIds.Contains(s.UserId) && s.ExpiresAt > DateTime.Now)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            // Get views
            var storyIds = stories.Select(s => s.Id).ToList();
            var views = await _context.StoryViews
                .Where(sv => storyIds.Contains(sv.StoryId))
                .ToListAsync();

            // Nhóm theo User
            var groupedStories = stories.GroupBy(s => s.UserId).Select(g => new
            {
                userId = g.Key,
                username = g.First().User.FullName,
                avatar = g.First().User.ImageUrl ?? "default-avatar.png",
                allSeen = g.All(s => views.Any(v => v.StoryId == s.Id && v.UserId == userId)),
                stories = g.Select(s => new
                {
                    id = s.Id,
                    url = s.MediaUrl,
                    type = s.Type,
                    content = s.Content,
                    createdAt = s.CreatedAt,
                    isSeen = views.Any(v => v.StoryId == s.Id && v.UserId == userId),
                    viewCount = views.Count(v => v.StoryId == s.Id && v.UserId != s.UserId) // Exclude owner self-views
                }).ToList()
            }).ToList();

            return Json(new { success = true, data = groupedStories });
        }

        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] int id)
        {
            var userIdStr = HttpContext.Session.GetInt32("UserId");
            if (userIdStr == null) return Unauthorized();
            int userId = userIdStr.Value;

            var story = await _context.Stories.FindAsync(id);
            if (story == null) return NotFound();

            if (story.UserId != userId) return Forbid(); // Chỉ được xóa tin của mình

            // Xóa file (tùy chọn, để tiết kiệm dung lượng)
            // if (System.IO.File.Exists(Path.Combine(_environment.WebRootPath, story.MediaUrl.TrimStart('/')))) ...

            _context.Stories.Remove(story);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsSeen([FromBody] int storyId)
        {
            var userIdStr = HttpContext.Session.GetInt32("UserId");
            if (userIdStr == null) return Unauthorized();
            int userId = userIdStr.Value;

            var story = await _context.Stories.FindAsync(storyId);
            if (story == null) return NotFound();

            // Prevent owner from counting as a view
            if (story.UserId == userId) return Json(new { success = true });

            // Check if already viewed
            bool alreadyViewed = await _context.StoryViews.AnyAsync(sv => sv.StoryId == storyId && sv.UserId == userId);
            if (!alreadyViewed)
            {
                var view = new StoryView
                {
                    StoryId = storyId,
                    UserId = userId,
                    ViewedAt = DateTime.Now
                };
                _context.StoryViews.Add(view);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetStoryViewers(int id)
        {
            var userIdStr = HttpContext.Session.GetInt32("UserId");
            if (userIdStr == null) return Unauthorized();
            int currentUserId = userIdStr.Value;

            var story = await _context.Stories.FindAsync(id);
            if (story == null) return NotFound();

            if (story.UserId != currentUserId) return Forbid(); // Only owner can see viewers

            var viewers = await _context.StoryViews
                .Where(sv => sv.StoryId == id && sv.UserId != currentUserId) // Exclude self
                .Include(sv => sv.User)
                .OrderByDescending(sv => sv.ViewedAt)
                .Select(sv => new
                {
                    userId = sv.UserId,
                    username = sv.User.FullName,
                    avatar = sv.User.ImageUrl ?? "default-avatar.png",
                    viewedAt = sv.ViewedAt
                })
                .ToListAsync();

            return Json(new { success = true, data = viewers });
        }
    }
}
