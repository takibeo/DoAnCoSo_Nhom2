using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using DoAnCoSo_Nhom2.Data;
using DoAnCoSo_Nhom2.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using DoAnCoSo_Nhom2.Extensions;
using DoAnCoSo_Nhom2.ViewModels;


namespace DoAnCoSo_Nhom2.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;

        public AdminController(UserManager<User> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult ManageUsers() => View(_userManager.Users.ToList());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            if (new[] { "Customer", "Staff", "Admin" }.Contains(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }

            return RedirectToAction("ManageUsers");
        }

        [HttpGet]
        public IActionResult ManageHomestays()
        {
            var homestays = _context.Homestays
                .Include(h => h.HomestayAmenities).ThenInclude(ha => ha.Amenity)
                .Include(h => h.Staff)
                .ToList();
            return View(homestays);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveHomestay(int id)
        {
            var homestay = _context.Homestays.Find(id);
            if (homestay == null) return NotFound();

            homestay.IsApproved = true;
            _context.SaveChanges();

            return RedirectToAction("ManageHomestays");
        }




        [HttpGet]
        public IActionResult EditHomestay(int id)
        {
            var homestay = _context.Homestays
                .Include(h => h.HomestayAmenities).ThenInclude(ha => ha.Amenity)
                .FirstOrDefault(h => h.Id == id);
            if (homestay == null) return NotFound();

            ViewBag.Amenities = _context.Amenities.ToList();
            return View(homestay);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditHomestay(Homestay homestay, List<int> amenityIds, IFormFile[] images)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Amenities = _context.Amenities.ToList();
                return View(homestay);
            }

            var existingHomestay = _context.Homestays
                .Include(h => h.HomestayAmenities)
                .FirstOrDefault(h => h.Id == homestay.Id);

            if (existingHomestay == null) return NotFound();

            var newImages = SaveImages(images);
            existingHomestay.UpdateDetails(homestay, newImages, amenityIds);
            _context.SaveChanges();

            return RedirectToAction("ManageHomestays");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteHomestay(int id)
        {
            var homestay = _context.Homestays.Include(h => h.Staff).FirstOrDefault(h => h.Id == id);
            if (homestay == null) return NotFound();

            var notification = new DeletedHomestayNotification
            {
                HomestayName = homestay.Name,
                StaffId = homestay.StaffId,
                DeletedAt = DateTime.Now
            };
            _context.DeletedHomestayNotifications.Add(notification);

            _context.Homestays.Remove(homestay);
            _context.SaveChanges();

            return RedirectToAction("ManageHomestays");
        }


        [HttpGet]
        public IActionResult ManageBookings()
        {
            var bookings = _context.Bookings
                .Include(b => b.Homestay)
                .Include(b => b.User)
                .ToList();
            return View(bookings);
        }

        [HttpGet]
        public IActionResult ThongKeBooking(string homestayName, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.Bookings
                .Include(b => b.Homestay)
                .Include(b => b.User)
                .AsQueryable();

            if (!string.IsNullOrEmpty(homestayName))
                query = query.Where(b => b.Homestay.Name.Contains(homestayName));

            if (fromDate.HasValue)
                query = query.Where(b => b.CheckInDate >= fromDate);

            if (toDate.HasValue)
                query = query.Where(b => b.CheckOutDate <= toDate);

            var statusStatistics = query.GroupBy(b => b.Status)
                .Select(g => new StatusStatistic { Status = g.Key, Count = g.Count() })
                .ToList();

            var revenueStatistics = query
                .Where(b => b.Status == "Confirmed")
                .GroupBy(b => new { b.CheckInDate.Year, b.CheckInDate.Month })
                .Select(g => new RevenueStatistic { Month = g.Key.Month + "/" + g.Key.Year, TotalRevenue = g.Sum(b => b.TotalPrice) })
                .ToList();

            var bookings = query.ToList();

            ViewBag.Homestays = _context.Homestays.ToList();

            var model = new ThongKeBookingViewModel
            {
                StatusStatistics = statusStatistics,
                RevenueStatistics = revenueStatistics,
                Bookings = bookings
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveBooking(int id)
        {
            var booking = _context.Bookings.Find(id);
            if (booking == null) return NotFound();

            booking.Status = "Confirmed";
            _context.SaveChanges();
            return RedirectToAction("ManageBookings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CancelBooking(int id)
        {
            var booking = _context.Bookings.Find(id);
            if (booking == null) return NotFound();

            booking.Status = "Cancelled";
            _context.SaveChanges();
            return RedirectToAction("ManageBookings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KhoaTK(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            user.LockoutEnd = DateTime.UtcNow.AddYears(100);
            await _userManager.UpdateAsync(user);
            return RedirectToAction("ManageUsers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoKhoaTK(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            user.LockoutEnd = null;
            await _userManager.UpdateAsync(user);
            return RedirectToAction("ManageUsers");
        }

        [HttpGet]
        public IActionResult RevenueReport(DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue)
                startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (!endDate.HasValue)
                endDate = DateTime.Now;

            var revenueData = _context.Bookings
                .Where(b => b.CheckInDate >= startDate && b.CheckOutDate <= endDate)
                .AsEnumerable()
                .GroupBy(b => b.CheckInDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalRevenue = g.Where(b => b.Status == "Completed").Sum(b => b.TotalPrice),
                    TotalBookings = g.Count(),
                    SuccessfulBookings = g.Count(b => b.Status == "Completed"),
                    CancelledBookings = g.Count(b => b.Status == "Cancelled")
                })
                .OrderBy(x => x.Date)
                .ToList();

            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            return View(revenueData);
        }

        [HttpGet]
        public IActionResult ManageReviews()
        {
            var reviews = _context.Reviews
                .Include(r => r.User)
                .Include(r => r.Homestay)
                .Include(r => r.Reports)
                .ThenInclude(report => report.User)
                .Where(r => r.Reports.Any())
                .ToList();

            return View(reviews);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteReview(int id)
        {
            var review = _context.Reviews.Find(id);
            if (review == null) return NotFound();

            _context.Reviews.Remove(review);
            _context.SaveChanges();
            return RedirectToAction("ManageReviews");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == currentUserId)
            {
                TempData["ErrorMessage"] = "Không thể xóa chính bạn.";
                return RedirectToAction("ManageUsers");
            }
            var isStaff = await _userManager.IsInRoleAsync(user, "Staff");
            if (isStaff)
            {
                var homestays = _context.Homestays.Any(h => h.StaffId == userId);
                if (homestays)
                {
                    TempData["ErrorMessage"] = "Không thể xóa tài khoản này vì người dùng đang quản lý một hoặc nhiều homestay.";
                    return RedirectToAction("ManageUsers");
                }
            }
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Không thể xóa tài khoản.");
                return RedirectToAction("ManageUsers");
            }

            TempData["SuccessMessage"] = "Xóa tài khoản thành công.";
            return RedirectToAction("ManageUsers");
        }

        private List<string> SaveImages(IFormFile[] images)
        {
            var imagePaths = new List<string>();
            if (images == null || images.Length == 0) return imagePaths;

            var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/homestays");
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            foreach (var image in images)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(directoryPath, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    image.CopyTo(stream);
                }
                imagePaths.Add("/images/homestays/" + fileName);
            }
            return imagePaths;
        }
    }
}