using Microsoft.AspNetCore.Mvc;
using SocialNetwork.Models;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net; // Dùng cho mật khẩu

namespace SocialNetwork.Controllers
{
    public class AccountController : Controller
    {
        private readonly SocialNetworkDbContext _context;

        public AccountController(SocialNetworkDbContext context)
        {
            _context = context;
        }

        // ===================== LOGIN =====================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl; // Giữ lại URL để redirect sau đăng nhập
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Username và mật khẩu không được để trống!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                ViewBag.Error = "Sai username hoặc mật khẩu!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // ✅ Lưu session
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Username", user.Username ?? "");
            HttpContext.Session.SetString("FullName", user.FullName ?? user.Username);
            HttpContext.Session.SetString("Role", user.Role ?? "User");
            HttpContext.Session.SetString("ImageUrl", user.ImageUrl ?? "");

            // ✅ Nếu có returnUrl → quay lại trang trước
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            // Nếu không có → về trang Post
            return RedirectToAction("Index", "Post");
        }

        // ===================== REGISTER =====================
        [HttpGet]
        public IActionResult Register(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(
             string username,
             string fullName,
             string phoneNumber,
             string email,
             string password,
             string dateOfBirth,
             string confirmPassword,
             string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email)
                || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword)
                || string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(phoneNumber) || string.IsNullOrEmpty(dateOfBirth))
            {
                ViewBag.Error = "Vui lòng điền đầy đủ thông tin!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                ViewBag.Error = "Tên tài khoản đã được sử dụng!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                ViewBag.Error = "Email đã được đăng ký!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            if (await _context.Users.AnyAsync(u => u.PhoneNumber == phoneNumber))
            {
                ViewBag.Error = "Số điện thoại đã được đăng ký!";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // ✅ Hash mật khẩu bằng BCrypt
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            var user = new User
            {
                Username = username,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                Email = email,
                PasswordHash = hashedPassword,
                DateOfBirth = DateTime.Parse(dateOfBirth),
                Role = "User",
                CreatedAt = DateTime.Now,
                ImageUrl = "img-default.jpg"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // ✅ Lưu session
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Username", user.Username ?? "");
            HttpContext.Session.SetString("FullName", user.FullName ?? user.Username);
            HttpContext.Session.SetString("Role", user.Role ?? "User");
            HttpContext.Session.SetString("ImageUrl", user.ImageUrl ?? "");

            // ✅ Nếu có returnUrl → quay lại trang trước
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Post");
        }

        // ===================== LOGOUT =====================
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
