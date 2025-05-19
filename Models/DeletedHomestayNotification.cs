namespace DoAnCoSo_Nhom2.Models
{
    public class DeletedHomestayNotification
    {
        public int Id { get; set; }
        public string HomestayName { get; set; }
        public string StaffId { get; set; }
        public DateTime DeletedAt { get; set; }
    }
}
