using DoAnCoSo_Nhom2.Models;
using DoAnCoSo_Nhom2.Service.VnPay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoAnCoSo_Nhom2.Controllers
{
    public class PaymentController : Controller
    {
        private readonly IVnPayService _vnPayService;

        public PaymentController(IVnPayService vnPayService)
        {
            _vnPayService = vnPayService;
        }

        [HttpGet]
        [ValidateAntiForgeryToken]
        public IActionResult CreatePaymentUrlVnpay(PaymentInformationModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(string.Join("; ", errors));
            }

            var url = _vnPayService.CreatePaymentUrl(model, HttpContext);

            if (string.IsNullOrEmpty(url))
            {
                TempData["Error"] = "Không thể tạo URL thanh toán.";
                return RedirectToAction("Index", "Home");
            }

            return Redirect(url);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);

            if (response == null)
            {
                TempData["Error"] = "Không nhận được phản hồi từ VNPay.";
                return RedirectToAction("PaymentResult");
            }

            if (response.Success)
            {
                // Xử lý đặt phòng hoặc giỏ hàng theo OrderId (tempBooking hoặc cartItem)
                var orderIdParts = response.OrderId.Split('_');
                if (orderIdParts.Length < 1)
                {
                    TempData["Error"] = "Mã giao dịch không hợp lệ.";
                    return RedirectToAction("PaymentResult");
                }

                var db = HttpContext.RequestServices.GetService(typeof(DoAnCoSo_Nhom2.Data.ApplicationDbContext)) as DoAnCoSo_Nhom2.Data.ApplicationDbContext;
                if (db == null)
                {
                    TempData["Error"] = "Lỗi hệ thống.";
                    return RedirectToAction("PaymentResult");
                }

                int id;
                if (!int.TryParse(orderIdParts[0], out id))
                {
                    TempData["Error"] = "Mã giao dịch không hợp lệ.";
                    return RedirectToAction("PaymentResult");
                }

                // Kiểm tra đây là booking tạm hay cart item
                if (response.OrderType == "booking")
                {
                    var tempBooking = db.TempBookings.FirstOrDefault(t => t.Id == id);
                    if (tempBooking == null)
                    {
                        TempData["Error"] = "Không tìm thấy thông tin đặt phòng tạm.";
                        return RedirectToAction("PaymentResult");
                    }

                    var booking = new Booking
                    {
                        HomestayId = tempBooking.HomestayId,
                        UserId = tempBooking.UserId,
                        CheckInDate = tempBooking.CheckInDate,
                        CheckOutDate = tempBooking.CheckOutDate,
                        NumberOfGuests = tempBooking.NumberOfGuests,
                        TotalPrice = tempBooking.TotalPrice,
                        Status = "Confirmed",
                        UpdatedAt = DateTime.Now
                    };

                    db.Bookings.Add(booking);
                    db.TempBookings.Remove(tempBooking);
                    db.SaveChanges();

                    TempData["Message"] = "Đặt phòng và thanh toán thành công.";
                }
                else if (response.OrderType == "cart")
                {
                    var cartItem = db.Carts.FirstOrDefault(c => c.Id == id);
                    if (cartItem == null)
                    {
                        TempData["Error"] = "Không tìm thấy mục trong giỏ hàng.";
                        return RedirectToAction("PaymentResult");
                    }

                    var booking = new Booking
                    {
                        HomestayId = cartItem.HomestayId,
                        UserId = cartItem.UserId,
                        CheckInDate = cartItem.CheckInDate,
                        CheckOutDate = cartItem.CheckOutDate,
                        NumberOfGuests = cartItem.NumberOfGuests,
                        TotalPrice = cartItem.TotalPrice,
                        Status = "Confirmed",
                        UpdatedAt = DateTime.Now
                    };

                    db.Bookings.Add(booking);
                    db.Carts.Remove(cartItem);
                    db.SaveChanges();

                    TempData["Message"] = "Thanh toán giỏ hàng thành công.";
                }
                else
                {
                    TempData["Error"] = "Loại đơn hàng không xác định.";
                }
            }
            else
            {
                TempData["Error"] = $"Thanh toán thất bại. Mã lỗi: {response.VnPayResponseCode}";
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
