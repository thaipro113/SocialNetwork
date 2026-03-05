# Tính năng Phân quyền Bài viết và Chỉnh sửa

## Tổng quan
Tài liệu này mô tả các thay đổi cần thực hiện để:
1. Phân quyền xem bài viết (Public, Friends, Private)
2. Cho phép chỉnh sửa bài viết (nội dung, hình ảnh, quyền)

## 1. Cập nhật PostController.cs

### 1.1 Cập nhật Index() - Lọc bài viết theo quyền
```csharp
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
            // Friends: Chỉ bạn bè thấy
            (p.Visibility == "Friends" && userId.HasValue && 
             (p.UserId == userId.Value || friendIds.Contains(p.UserId))) ||
            // Private: Chỉ mình tác giả thấy
            (p.Visibility == "Private" && userId.HasValue && p.UserId == userId.Value)
        )
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync();

    return View(posts);
}
```

### 1.2 Thêm action Edit() - GET
```csharp
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
```

### 1.3 Thêm action Edit() - POST
```csharp
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
```

### 1.4 Cập nhật Details() - Kiểm tra quyền xem
```csharp
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
```

## 2. Tạo View Edit

### File: Views/Post/Edit.cshtml
```cshtml
@model SocialNetwork.Models.Post
@{
    ViewData["Title"] = "Chỉnh sửa bài viết";
}

<div class="max-w-2xl mx-auto mt-6 p-6 bg-white dark:bg-gray-800 rounded-2xl shadow-lg">
    <h2 class="text-2xl font-bold text-gray-800 dark:text-white mb-6">Chỉnh sửa bài viết</h2>

    <form asp-action="Edit" method="post" enctype="multipart/form-data">
        <input type="hidden" asp-for="PostId" />
        
        <!-- Nội dung -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Nội dung</label>
            <textarea asp-for="Content" 
                      class="w-full p-3 border border-gray-300 dark:border-gray-600 rounded-lg focus:ring-2 focus:ring-blue-500 dark:bg-gray-700 dark:text-white"
                      rows="5"></textarea>
        </div>

        <!-- Quyền riêng tư -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Quyền riêng tư</label>
            <select asp-for="Visibility" 
                    class="w-full p-3 border border-gray-300 dark:border-gray-600 rounded-lg focus:ring-2 focus:ring-blue-500 dark:bg-gray-700 dark:text-white">
                <option value="Public">🌍 Công khai</option>
                <option value="Friends">👥 Bạn bè</option>
                <option value="Private">🔒 Chỉ mình tôi</option>
            </select>
        </div>

        <!-- Ảnh/Video hiện tại -->
        @if (!string.IsNullOrEmpty(Model.MediaUrl))
        {
            <div class="mb-4">
                <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Ảnh/Video hiện tại</label>
                @if (Model.MediaUrl.EndsWith(".mp4") || Model.MediaUrl.EndsWith(".webm"))
                {
                    <video src="@Model.MediaUrl" controls class="max-w-full rounded-lg"></video>
                }
                else
                {
                    <img src="@Model.MediaUrl" class="max-w-full rounded-lg" />
                }
                <div class="mt-2">
                    <label class="flex items-center">
                        <input type="checkbox" name="removeMedia" value="true" class="mr-2" />
                        <span class="text-sm text-red-600">Xóa ảnh/video này</span>
                    </label>
                </div>
            </div>
        }

        <!-- Upload ảnh/video mới -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                @(string.IsNullOrEmpty(Model.MediaUrl) ? "Thêm ảnh/video" : "Thay đổi ảnh/video")
            </label>
            <input type="file" name="media" accept="image/*,video/*" 
                   class="w-full p-2 border border-gray-300 dark:border-gray-600 rounded-lg dark:bg-gray-700 dark:text-white" />
        </div>

        <!-- Buttons -->
        <div class="flex gap-3">
            <button type="submit" 
                    class="flex-1 bg-blue-600 text-white py-3 rounded-lg hover:bg-blue-700 transition font-semibold">
                💾 Lưu thay đổi
            </button>
            <a asp-action="Details" asp-route-id="@Model.PostId" 
               class="flex-1 bg-gray-300 text-gray-800 py-3 rounded-lg hover:bg-gray-400 transition font-semibold text-center">
                ❌ Hủy
            </a>
        </div>
    </form>
</div>
```

## 3. Cập nhật UI - Thêm nút Edit

### Trong Views/Post/Details.cshtml
Thêm nút Edit nếu là tác giả:
```cshtml
@if (ViewBag.UserId != null && Model.UserId == ViewBag.UserId)
{
    <div class="mt-4">
        <a asp-action="Edit" asp-route-id="@Model.PostId" 
           class="inline-block px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition">
            ✏️ Chỉnh sửa bài viết
        </a>
    </div>
}
```

### Trong Views/Post/Index.cshtml
Thêm nút Edit trong dropdown menu của mỗi bài viết:
```cshtml
@if (ViewBag.UserId != null && post.UserId == ViewBag.UserId)
{
    <a asp-action="Edit" asp-route-id="@post.PostId" 
       class="block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100">
        ✏️ Chỉnh sửa
    </a>
}
```

## 4. Hiển thị icon quyền riêng tư

Thêm vào mỗi bài viết để hiển thị quyền:
```cshtml
<span class="text-sm text-gray-500">
    @switch (post.Visibility)
    {
        case "Public":
            <span title="Công khai">🌍</span>
            break;
        case "Friends":
            <span title="Bạn bè">👥</span>
            break;
        case "Private":
            <span title="Chỉ mình tôi">🔒</span>
            break;
    }
</span>
```

## 5. Testing Checklist

- [ ] Bài viết Public: Ai cũng thấy
- [ ] Bài viết Friends: Chỉ bạn bè thấy
- [ ] Bài viết Private: Chỉ tác giả thấy
- [ ] Chỉnh sửa nội dung bài viết
- [ ] Thay đổi quyền riêng tư
- [ ] Thêm/xóa/thay đổi ảnh
- [ ] Chỉ tác giả mới thấy nút Edit
- [ ] Người khác không thể truy cập /Post/Edit/{id}

## 6. Lưu ý bảo mật

1. Luôn kiểm tra userId trong session
2. Kiểm tra quyền sở hữu bài viết trước khi cho phép edit
3. Validate input để tránh XSS
4. Xóa file cũ khi upload file mới để tránh rác
