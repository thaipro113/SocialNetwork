using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using SocialNetwork.Models;
using System.Diagnostics;

namespace SocialNetwork.Controllers
{
    public class UserController : Controller
    {
        private readonly SocialNetworkDbContext _context;
        private readonly IWebHostEnvironment _env; // ✅ thêm dòng này

        public UserController(SocialNetworkDbContext context, IWebHostEnvironment env)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _env = env;
        }

        // Hiển thị trang hồ sơ người dùng (kèm bài viết)
        // Nếu có id → xem hồ sơ người đó; nếu không → xem hồ sơ của chính mình
        [HttpGet]
        public async Task<IActionResult> Profile(int? id)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Profile", "User") });
            }

            var targetUserId = id ?? sessionUserId.Value;

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == targetUserId);

            if (user == null) return NotFound();

            // Kiểm tra xem có phải bạn bè không
            var isFriend = await _context.Friendships
                .AnyAsync(f => 
                    (f.UserAId == sessionUserId && f.UserBId == targetUserId) ||
                    (f.UserAId == targetUserId && f.UserBId == sessionUserId));

            // Lọc bài viết theo quyền
            var posts = await _context.Posts
                .Include(p => p.Comments)
                    .ThenInclude(c => c.User)
                .Include(p => p.Likes)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op.User)
                .Where(p => p.UserId == targetUserId &&
                    (p.Visibility == "Public" || // Công khai: ai cũng thấy
                     (p.Visibility == "Friends" && (targetUserId == sessionUserId || isFriend)) || // Bạn bè: chủ nhân hoặc bạn bè thấy
                     (p.Visibility == "Private" && targetUserId == sessionUserId))) // Riêng tư: chỉ chủ nhân thấy
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            user.Posts = posts;

            // Chỉ đồng bộ session khi xem hồ sơ của chính mình
            if (targetUserId == sessionUserId)
            {
                HttpContext.Session.SetString("FullName", user.FullName ?? string.Empty);
                HttpContext.Session.SetString("ImageUrl", user.ImageUrl ?? string.Empty);
            }

            // Đếm số bạn bè của user đang xem
            var friendCount = _context.Friendships.Count(f => f.UserAId == targetUserId || f.UserBId == targetUserId);
            ViewBag.FriendCount = friendCount;

            return View(user);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteComment([FromBody] int commentId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var comment = await _context.Comments
                .Include(c => c.Post)
                .FirstOrDefaultAsync(c => c.CommentId == commentId);

            if (comment == null) return NotFound();

            // Chỉ cho phép xóa nếu là chủ comment hoặc chủ post hoặc admin
            var role = HttpContext.Session.GetString("Role");
            if (comment.UserId != userId && comment.Post.UserId != userId && role != "Admin")
                return Forbid();

            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync();

            // trả về số comment còn lại
            var commentCount = await _context.Comments
                .CountAsync(c => c.PostId == comment.PostId);

            return Json(new { success = true, comments = commentCount, postId = comment.PostId, commentId });
        }

        // POST: Cập nhật hồ sơ (bao gồm upload ảnh)
        [HttpPost]
        public async Task<IActionResult> Profile(User user, IFormFile ImageFile)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            var existingUser = await _context.Users.FindAsync(userId);
            if (existingUser == null) return NotFound();

            // Cập nhật thông tin cơ bản
            existingUser.FullName = user.FullName;
            existingUser.Email = user.Email;
            existingUser.PhoneNumber = user.PhoneNumber;
            existingUser.DateOfBirth = user.DateOfBirth;

            // Xử lý upload ảnh
            if (ImageFile != null && ImageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(ImageFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ImageFile.CopyToAsync(stream);
                }

                // ✅ Xóa ảnh cũ nếu có và KHÔNG phải img-default.jpg
                if (!string.IsNullOrEmpty(existingUser.ImageUrl) && existingUser.ImageUrl != "img-default.jpg")
                {
                    var oldFilePath = Path.Combine(uploadsFolder, existingUser.ImageUrl);
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                existingUser.ImageUrl = fileName;
            }

            try
            {
                _context.Users.Update(existingUser);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Cập nhật hồ sơ thành công!";

                // Đồng bộ lại session
                HttpContext.Session.SetString("FullName", existingUser.FullName ?? string.Empty);
                HttpContext.Session.SetString("ImageUrl", existingUser.ImageUrl ?? string.Empty);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi: {ex.Message}";
            }

            return RedirectToAction(nameof(Profile));
        }


        // GET: Chỉnh sửa hồ sơ
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return View(user);
        }
    }
}
