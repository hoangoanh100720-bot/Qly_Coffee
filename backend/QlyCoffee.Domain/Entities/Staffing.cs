namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÂN SỰ & CHẤM CÔNG
//
//  NHÂN VIÊN KHÁC TÀI KHOẢN ĐĂNG NHẬP. Máy ở quầy đăng nhập bằng MỘT tài khoản
//  nhân viên chung (User, vai trò Staff); từng người chấm công bằng mã NV + mã
//  PIN riêng ngay trên máy đó. Lý do: quán có thể có 8 bạn part-time xoay ca,
//  bắt mỗi bạn đăng xuất/đăng nhập lại trên máy POS mỗi lần đổi ca là không
//  ai làm — và rồi cả quán dùng chung một mật khẩu, chấm công thành vô nghĩa.
//
//  KHUNG CA CỐ ĐỊNH 4 TIẾNG. Giờ bắt đầu do quản lý đặt; độ dài thì không đổi,
//  để "trễ bao nhiêu phút" và "làm được bao nhiêu giờ" luôn so trên cùng một mốc.
//
//  MỌI CON SỐ TIỀN LƯƠNG ĐƯỢC CHỤP LẠI lúc chấm công ra. Tăng lương tháng sau
//  không được làm đổi số tiền của những ca đã làm tháng này.
// ==============================================================================

/// <summary>Một nhân viên của quán (hồ sơ nhân sự, không phải tài khoản đăng nhập).</summary>
public class Employee : StoreScopedEntity
{
    /// <summary>Mã nhân viên, VD "NV001". Duy nhất trong chi nhánh, sinh tự động.</summary>
    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    /// <summary>Vị trí: Pha chế, Thu ngân, Phục vụ… Chỉ để hiển thị.</summary>
    public string Position { get; set; } = "Nhân viên";

    /// <summary>Lương theo giờ, đồng.</summary>
    public int HourlyWage { get; set; }

    /// <summary>Mã PIN chấm công đã băm BCrypt. Không bao giờ lưu PIN gốc.</summary>
    public string PinHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateOnly? HiredOn { get; set; }

    public string? Note { get; set; }

    // ---- Chống dò PIN ---------------------------------------------------------
    // PIN chỉ 4–6 số: không khoá thì đứng ở quầy thử 10.000 tổ hợp là ra.

    public int FailedPinAttempts { get; set; }

    public DateTime? PinLockedUntil { get; set; }
}

/// <summary>Khung ca 4 tiếng. VD "Ca sáng" 06:00–10:00.</summary>
public class WorkSlot : StoreScopedEntity
{
    /// <summary>Độ dài cố định của mọi khung ca.</summary>
    public const int DurationMinutes = 240;

    public string Name { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu theo giờ quán (Việt Nam).</summary>
    public TimeOnly StartTime { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Một lần chấm công: vào ca, (về ca), trễ bao lâu, làm bao lâu, được bao nhiêu.</summary>
public class Attendance : StoreScopedEntity
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>Ngày làm theo giờ Việt Nam.</summary>
    public DateOnly WorkDate { get; set; }

    public Guid SlotId { get; set; }
    public string SlotName { get; set; } = string.Empty;

    /// <summary>Mốc bắt đầu / kết thúc khung ca, UTC — chụp lại để đổi giờ ca sau này không làm sai lịch sử.</summary>
    public DateTime SlotStartAt { get; set; }
    public DateTime SlotEndAt { get; set; }

    public DateTime CheckInAt { get; set; }

    /// <summary>Số phút trễ so với giờ bắt đầu ca. 0 nếu đúng giờ hoặc tới sớm.</summary>
    public int LateMinutes { get; set; }

    public DateTime? CheckOutAt { get; set; }

    /// <summary>Số phút về sớm so với giờ kết thúc ca.</summary>
    public int EarlyLeaveMinutes { get; set; }

    /// <summary>Số phút được tính lương = phần giao giữa [vào, ra] và khung ca.</summary>
    public int WorkedMinutes { get; set; }

    /// <summary>Lương giờ chụp lại lúc chấm công vào.</summary>
    public int HourlyWage { get; set; }

    /// <summary>Tiền công của lần này, đồng. Tính lúc chấm ra.</summary>
    public int Pay { get; set; }

    /// <summary>Ca két mà người này giữ trong lúc làm (nếu có).</summary>
    public Guid? CashShiftId { get; set; }

    public Employee? Employee { get; set; }
}
