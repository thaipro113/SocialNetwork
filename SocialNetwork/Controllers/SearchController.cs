using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialNetwork.Models;

namespace SocialNetwork.Controllers
{
    public class SearchController : Controller
    {
        private readonly SocialNetworkDbContext _db;

        public SearchController(SocialNetworkDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string keyword)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            List<User> users = new();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                users = await _db.Users
                    .Where(u => u.FullName.ToLower().Contains(keyword) && u.UserId != userId)
                    .ToListAsync();
            }

            ViewBag.Keyword = keyword;
            return View(users);
        }
    }
}
