using DoAnCoSo_Nhom2.Models;

namespace DoAnCoSo_Nhom2.ViewModels
{
    public class ThongKeBookingViewModel
    {
        public List<StatusStatistic> StatusStatistics { get; set; }
        public List<RevenueStatistic> RevenueStatistics { get; set; }
        public List<Booking> Bookings { get; set; }
    }

    public class StatusStatistic
    {
        public string Status { get; set; }
        public int Count { get; set; }
    }

    public class RevenueStatistic
    {
        public string Month { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}
