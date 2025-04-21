using System.Security.Claims;
using DoAnCoSo_Nhom2.Data;
using DoAnCoSo_Nhom2.Models;
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

        public CartController(ApplicationDbContext context)
        {
            _context = context;
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
            Console.WriteLine($"Received: HomestayId={model.HomestayId}, CheckIn={model.CheckInDate}, CheckOut={model.CheckOutDate}, Guests={model.NumberOfGuests}");

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

        public IActionResult Checkout()
        {
            var identity = (ClaimsIdentity)User.Identity;
            var claim = identity.FindFirst(ClaimTypes.NameIdentifier);
            var userId = claim.Value;

            var cartItems = _context.Carts.Where(c => c.UserId == userId).ToList();
            if (!cartItems.Any())
            {
                return RedirectToAction("Index");
            }

            foreach (var cartItem in cartItems)
            {
                var booking = new Booking
                {
                    HomestayId = cartItem.HomestayId,
                    UserId = cartItem.UserId,
                    CheckInDate = cartItem.CheckInDate,
                    CheckOutDate = cartItem.CheckOutDate,
                    NumberOfGuests = cartItem.NumberOfGuests,
                    TotalPrice = cartItem.TotalPrice,
                    Status = "Pending"
                };
                _context.Bookings.Add(booking);
            }

            _context.Carts.RemoveRange(cartItems);
            _context.SaveChanges();

            return RedirectToAction("Index", "Booking");
        }
    }
}
