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
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IVnPayService _vnPayService;

        public CartController(ApplicationDbContext context, IVnPayService vnPayService)
        {
            _context = context;
            _vnPayService = vnPayService;
        }

        public IActionResult AddToCart(int homeStayId)
        {
            ViewBag.HomeStayId = homeStayId;
            return View(new BookingCreateViewModel { HomestayId = homeStayId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToCart(BookingCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var identity = (ClaimsIdentity)User.Identity;
            var claim = identity.FindFirst(ClaimTypes.NameIdentifier);
            var userId = claim.Value;

            var homestay = _context.Homestays.Find(model.HomestayId);
            if (homestay == null)
            {
                ModelState.AddModelError("", "Homestay không tồn tại.");
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
                .Where(b => b.HomestayId == model.HomestayId &&
                            b.Status != "Cancelled" &&
                            model.CheckInDate < b.CheckOutDate &&
                            model.CheckOutDate > b.CheckInDate)
                .FirstOrDefault();

            if (overlappingBooking != null)
            {
                ModelState.AddModelError("", "Ngày bạn chọn đã có người đặt. Vui lòng chọn khoảng thời gian khác.");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            var cart = new Cart
            {
                HomestayId = model.HomestayId,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                NumberOfGuests = model.NumberOfGuests,
                TotalPrice = homestay.PricePerNight * nights,
                UserId = userId
            };

            try
            {
                _context.Carts.Add(cart);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi khi thêm vào giỏ hàng: {ex.Message}");
                ViewBag.HomeStayId = model.HomestayId;
                return View(model);
            }

            return RedirectToAction("Index");
        }

        public IActionResult Index()
        {
            var identity = (ClaimsIdentity)User.Identity;
            var claim = identity.FindFirst(ClaimTypes.NameIdentifier);
            var userId = claim.Value;

            var cartItems = _context.Carts
                .Include(c => c.Homestay)
                .Where(c => c.UserId == userId)
                .ToList();

            return View(cartItems);
        }

        public IActionResult Remove(int id)
        {
            var cartItem = _context.Carts.Find(id);
            if (cartItem != null && cartItem.UserId == User.FindFirst(ClaimTypes.NameIdentifier).Value)
            {
                _context.Carts.Remove(cartItem);
                _context.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult Pay(int id)
        {
            var cartItem = _context.Carts.FirstOrDefault(c => c.Id == id && c.UserId == User.FindFirst(ClaimTypes.NameIdentifier).Value);
            if (cartItem == null)
            {
                TempData["Error"] = "Không tìm thấy mục trong giỏ hàng.";
                return RedirectToAction("Index");
            }

            var txnRef = $"{cartItem.Id}_{DateTime.Now.Ticks}";
            var paymentInfo = new PaymentInformationModel
            {
                Name = "Thanh toán đặt phòng Homestay",
                OrderDescription = $"Đặt phòng Homestay #{cartItem.HomestayId}",
                Amount = (double)cartItem.TotalPrice,
                OrderType = "cart",
                OrderId = txnRef
            };

            Console.WriteLine($"Creating payment URL with OrderId: {txnRef}");

            var paymentUrl = _vnPayService.CreatePaymentUrl(paymentInfo, HttpContext);

            if (string.IsNullOrEmpty(paymentUrl))
            {
                TempData["Error"] = "Không thể tạo URL thanh toán.";
                return RedirectToAction("Index");
            }

            Console.WriteLine($"Redirecting to payment URL: {paymentUrl}");
            return Redirect(paymentUrl);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult PaymentCallbackVnpay()
        {
            Console.WriteLine("==== VNPay Callback Received ====");

            foreach (var item in Request.Query)
            {
                Console.WriteLine($"{item.Key} = {item.Value}");
            }

            var response = _vnPayService.PaymentExecute(Request.Query);

            if (response == null)
            {
                TempData["Error"] = "Không nhận được phản hồi từ VNPay.";
                return RedirectToAction("PaymentResult");
            }

            Console.WriteLine($"Response: Success={response.Success}, VnPayResponseCode={response.VnPayResponseCode}, OrderId={response.OrderId}");

            if (response.Success)
            {
                var orderIdParts = response.OrderId.Split('_');
                if (orderIdParts.Length > 0 && int.TryParse(orderIdParts[0], out int cartItemId))
                {
                    var cartItem = _context.Carts.FirstOrDefault(c => c.Id == cartItemId);
                    if (cartItem != null)
                    {
                        var booking = new Booking
                        {
                            HomestayId = cartItem.HomestayId,
                            UserId = cartItem.UserId,
                            CheckInDate = cartItem.CheckInDate,
                            CheckOutDate = cartItem.CheckOutDate,
                            NumberOfGuests = cartItem.NumberOfGuests,
                            TotalPrice = cartItem.TotalPrice,
                            Status = "Confirmed",
                        };

                        _context.Bookings.Add(booking);
                        _context.Carts.Remove(cartItem);
                        _context.SaveChanges();

                        TempData["Message"] = "Thanh toán thành công!";
                    }
                    else
                    {
                        TempData["Error"] = "Không tìm thấy mục trong giỏ hàng.";
                    }
                }
                else
                {
                    TempData["Error"] = "Mã giao dịch không hợp lệ.";
                }
            }
            else
            {
                string error = "Thanh toán thất bại.";
                if (!string.IsNullOrEmpty(response.VnPayResponseCode))
                {
                    error += $" Mã lỗi: {response.VnPayResponseCode}.";
                }
                if (!string.IsNullOrEmpty(response.OrderDescription))
                {
                    error += $" Thông tin: {response.OrderDescription}";
                }
                TempData["Error"] = error;
            }

            return RedirectToAction("PaymentResult");
        }
        [AllowAnonymous]
        public IActionResult PaymentResult()
        {
            ViewBag.Message = TempData["Message"];
            ViewBag.Error = TempData["Error"];

            return View();
        }


    }
}
