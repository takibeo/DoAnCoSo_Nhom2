using System.Security.Claims;
using DoAnCoSo_Nhom2.Data;
using DoAnCoSo_Nhom2.Models;
using DoAnCoSo_Nhom2.Service.VnPay;
using DoAnCoSo_Nhom2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoAnCoSo_Nhom2.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IVnPayService _vnPayService;


        public BookingController(ApplicationDbContext context, IVnPayService vnPayService)
        {
            _context = context;
            _vnPayService = vnPayService;

        }

        [HttpGet]
        public IActionResult Create(int homeStayId)
        {
            var homestay = _context.Homestays.FirstOrDefault(h => h.Id == homeStayId && h.IsApproved);
            if (homestay == null)
            {
                TempData["Error"] = "Homestay không tồn tại hoặc chưa được phê duyệt.";
                return RedirectToAction("Search", "Homestay");
            }

            ViewBag.HomeStayId = homeStayId;
            return View(new BookingCreateViewModel { HomestayId = homeStayId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(BookingCreateViewModel model)
        {
            var homestay = _context.Homestays.FirstOrDefault(h => h.Id == model.HomestayId && h.IsApproved);
            if (homestay == null)
            {
                ModelState.AddModelError("", "Homestay không tồn tại hoặc chưa được phê duyệt.");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var nights = (model.CheckOutDate - model.CheckInDate).Days;
            if (nights <= 0)
            {
                ModelState.AddModelError("", "Ngày trả phòng phải sau ngày nhận phòng.");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var overlappingBooking = _context.Bookings
                .Any(b =>
                    b.HomestayId == model.HomestayId &&
                    b.Status != "Cancelled" &&
                    model.CheckInDate < b.CheckOutDate &&
                    model.CheckOutDate > b.CheckInDate
                );

            if (overlappingBooking)
            {
                ModelState.AddModelError("", "Ngày bạn chọn đã có người đặt. Vui lòng chọn khoảng thời gian khác.");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var identity = (ClaimsIdentity)User.Identity;
            var claim = identity.FindFirst(ClaimTypes.NameIdentifier);
            var userId = claim.Value;

            var booking = new Booking
            {
                HomestayId = model.HomestayId,
                UserId = userId,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                NumberOfGuests = model.NumberOfGuests,
                TotalPrice = homestay.PricePerNight * nights,
                Status = "Pending"
            };

            try
            {
                _context.Bookings.Add(booking);
                _context.SaveChanges(); 

                var log = new UserActivityLog
                {
                    UserId = userId,
                    Action = $"Đặt phòng tại Homestay #{model.HomestayId} từ {model.CheckInDate:dd/MM/yyyy} đến {model.CheckOutDate:dd/MM/yyyy}",
                    Timestamp = DateTime.Now
                };
                _context.UserActivityLogs.Add(log);
                _context.SaveChanges();
                TempData["Message"] = "Đặt phòng thành công!";
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi khi lưu đặt phòng: {ex.Message}");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            return RedirectToAction("Index");
        }

        public IActionResult Index()
        {
            var identity = (ClaimsIdentity)User.Identity;
            var claim = identity.FindFirst(ClaimTypes.NameIdentifier);

            var bookings = _context.Bookings
                .Include(b => b.Homestay) 
                .Where(bk => bk.UserId == claim.Value) 
                .ToList();

            if (bookings == null || bookings.Count == 0)
            {
                TempData["Message"] = "Không có booking nào được tìm thấy.";
            }

            return View(bookings);
        }

        public IActionResult Cancel(int id)
        {
            var booking = _context.Bookings.Find(id);
            if (booking != null && booking.UserId == User.FindFirst(ClaimTypes.NameIdentifier).Value)
            {
                booking.Status = "Cancelled";
                _context.SaveChanges(); 

                var log = new UserActivityLog
                {
                    UserId = booking.UserId,
                    Action = $"Hủy đặt phòng tại Homestay #{booking.HomestayId} (Check-in: {booking.CheckInDate:dd/MM/yyyy})",
                    Timestamp = DateTime.Now
                };
                _context.UserActivityLogs.Add(log);
                _context.SaveChanges(); 

                TempData["Message"] = "Đơn đặt phòng đã được hủy.";
            }
            else
            {
                TempData["Error"] = "Không thể hủy đơn đặt phòng này.";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);

            return Json(response);
        }

        public IActionResult hehe()
        {
            return View();
        }
    }
}
