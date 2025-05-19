using System.Security.Claims;
using DoAnCoSo_Nhom2.Data;
using DoAnCoSo_Nhom2.Models;
using DoAnCoSo_Nhom2.Service.VnPay;
using DoAnCoSo_Nhom2.ViewModels;
using Humanizer.Localisation;
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
                ModelState.AddModelError("", "Ngày bạn chọn đã có người đặt.");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var tempBooking = new TempBooking
            {
                HomestayId = model.HomestayId,
                UserId = userId,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                NumberOfGuests = model.NumberOfGuests,
                TotalPrice = homestay.PricePerNight * nights
            };

            _context.TempBookings.Add(tempBooking);
            _context.SaveChanges();

            var txnRef = $"{tempBooking.Id}_{DateTime.Now.Ticks}";

            var paymentInfo = new PaymentInformationModel
            {
                Name = "Thanh toán đặt phòng",
                OrderDescription = $"Đặt phòng Homestay #{tempBooking.HomestayId}",
                Amount = (double)tempBooking.TotalPrice,
                OrderType = "booking",
                OrderId = txnRef
            };

            var paymentUrl = _vnPayService.CreatePaymentUrl(paymentInfo, HttpContext);

            return RedirectToAction("CreatePaymentUrlVnpay", "Payment", paymentInfo);
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
                booking.UpdatedAt = DateTime.Now;
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

        public IActionResult PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);
            if (response?.Success == true)
            {
                var orderIdParts = response.OrderId.Split('_');
                if (orderIdParts.Length > 0 && int.TryParse(orderIdParts[0], out int tempBookingId))
                {
                    var temp = _context.TempBookings.Include(t => t.HomestayId).FirstOrDefault(t => t.Id == tempBookingId);
                    if (temp != null)
                    {
                        var booking = new Booking
                        {
                            HomestayId = temp.HomestayId,
                            UserId = temp.UserId,
                            CheckInDate = temp.CheckInDate,
                            CheckOutDate = temp.CheckOutDate,
                            NumberOfGuests = temp.NumberOfGuests,
                            TotalPrice = temp.TotalPrice,
                            Status = "Confirmed",
                            UpdatedAt = DateTime.Now
                        };

                        _context.Bookings.Add(booking);
                        _context.TempBookings.Remove(temp);
                        _context.SaveChanges();

                        TempData["Message"] = "Đặt phòng và thanh toán thành công.";
                        return RedirectToAction("Index");
                    }
                }

                TempData["Error"] = "Không tìm thấy thông tin đặt phòng tạm.";
            }
            else
            {
                TempData["Error"] = "Thanh toán thất bại.";
            }

            return RedirectToAction("Index");
        }
        public IActionResult hehe()
        {
            return View();
        }
    }
}