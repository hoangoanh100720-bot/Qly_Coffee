using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  CA LÀM VIỆC & KÉT TIỀN
//
//  Một ca là khoảng thời gian MỘT người chịu trách nhiệm két tiền: từ lúc nhận
//  két (mở ca) tới lúc đếm két và giao cho người sau (đóng ca).
//
//  VÌ SAO DOANH THU CỦA CA TÍNH THEO KHOẢNG THỜI GIAN, KHÔNG GẮN KHÓA VÀO ĐƠN:
//    Mỗi chi nhánh tại một thời điểm chỉ có TỐI ĐA MỘT ca đang mở — ràng buộc
//    bằng unique index có điều kiện trong database, không chỉ bằng code. Vì
//    vậy "tiền thu từ lúc mở tới lúc đóng ca" chỉ thuộc về đúng một ca, không
//    cần sửa mọi luồng thanh toán (quầy, web, webhook ngân hàng) để gắn ShiftId.
//    Khoản thu rơi vào lúc KHÔNG có ca nào mở vẫn được báo cáo riêng là "ngoài
//    ca" — đó chính là thứ quản lý cần thấy.
//
//  MỌI CON SỐ ĐƯỢC CHỤP LẠI LÚC ĐÓNG CA. Đơn bị sửa hay hoàn tiền tuần sau
//  không được làm đổi con số mà hai nhân viên đã cùng đếm và ký nhận hôm nay.
// ==============================================================================

/// <summary>Một ca đứng két.</summary>
public class CashShift : StoreScopedEntity
{
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    // ---- Mở ca ----------------------------------------------------------------

    /// <summary>Người nhận ca — người đứng két suốt ca này.</summary>
    public Guid OpenedById { get; set; }

    /// <summary>Tên người nhận ca, chụp lại để tài khoản bị xoá vẫn đọc được lịch sử.</summary>
    public string OpenedByName { get; set; } = string.Empty;

    /// <summary>
    /// Nhân viên (hồ sơ nhân sự) giữ két, khi nhận ca qua quầy chấm công. Null nếu
    /// ca được mở thẳng bằng tài khoản đăng nhập (quản lý). OpenedById vẫn là
    /// tài khoản của MÁY đã thao tác — hai thứ khác nhau, giữ cả hai.
    /// </summary>
    public Guid? OpenedByEmployeeId { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tiền mặt đếm được trong két lúc nhận ca, đồng.</summary>
    public int OpeningCash { get; set; }

    /// <summary>
    /// Ca trước bàn giao lại bao nhiêu (HandoverCash của ca liền trước). Null nếu
    /// đây là ca đầu tiên. Khác OpeningCash nghĩa là két bị hụt/dư GIỮA hai ca.
    /// </summary>
    public int? ExpectedOpeningCash { get; set; }

    /// <summary>Người giao ca cho ca này (người đóng ca liền trước). Chụp lại tên.</summary>
    public string? ReceivedFromName { get; set; }

    public string? OpeningNote { get; set; }

    // ---- Đóng ca --------------------------------------------------------------

    public Guid? ClosedById { get; set; }
    public string? ClosedByName { get; set; }
    public Guid? ClosedByEmployeeId { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>Người được giao ca tiếp theo. Null nếu đóng cửa cuối ngày.</summary>
    public Guid? HandedOverToId { get; set; }
    public string? HandedOverToName { get; set; }
    public Guid? HandedOverToEmployeeId { get; set; }

    /// <summary>Tiền mặt THỰC ĐẾM trong két lúc đóng ca, đồng.</summary>
    public int? CountedCash { get; set; }

    /// <summary>Số tiền để lại trong két cho ca sau (tiền lẻ thối), đồng.</summary>
    public int? HandoverCash { get; set; }

    /// <summary>Số tiền rút ra nộp quản lý = CountedCash − HandoverCash.</summary>
    public int? DepositedCash { get; set; }

    public string? ClosingNote { get; set; }

    // ---- Số liệu chụp lại lúc đóng ca ----------------------------------------

    /// <summary>Tiền mặt thu trong ca.</summary>
    public int? CashSales { get; set; }

    /// <summary>Chuyển khoản nhận trong ca — không vào két, nhưng là doanh thu của ca.</summary>
    public int? TransferSales { get; set; }

    /// <summary>Số đơn đã thu tiền trong ca.</summary>
    public int? PaidOrderCount { get; set; }

    /// <summary>Tiền mặt LẼ RA có trong két = OpeningCash + CashSales.</summary>
    public int? ExpectedCash { get; set; }

    /// <summary>Chênh lệch = CountedCash − ExpectedCash. Âm là hụt két, dương là dư.</summary>
    public int? CashDifference { get; set; }
}
