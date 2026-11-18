namespace QueueSystem.Models;

public enum QueueStatus
{
    Waiting,    // รอเรียก
    Called,     // เรียกแล้ว
    Completed,  // เสร็จสิ้น
    Cancelled   // ยกเลิก
}

public record ServiceType(string Code, string Name, string Icon);

public class QueueTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>ลำดับการสร้างคิวภายในวัน (ใช้ตัดสินเมื่อเวลาสร้างเท่ากัน)</summary>
    public int Sequence { get; init; }

    /// <summary>เลขคิว เช่น A001</summary>
    public string Number { get; init; } = "";

    public ServiceType Service { get; init; } = null!;
    public string CustomerName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? CalledAt { get; set; }
    public QueueStatus Status { get; set; } = QueueStatus.Waiting;
    public string? Remark { get; set; }
}
