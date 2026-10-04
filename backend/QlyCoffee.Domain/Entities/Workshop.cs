using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM WORKSHOP — ĐẶT LỊCH KHÔNG CẦN GỌI ĐIỆN
//
//  VẤN ĐỀ NÓ GIẢI QUYẾT
//
//  Trang giới thiệu workshop trước đây kết thúc bằng một nút "Gọi đặt lịch".
//  Nút đó đẩy toàn bộ phần khó sang phía khách:
//    · Khách phải gọi trong giờ quán mở, mà phần lớn người đọc trang lúc tối.
//    · Khách không biết còn buổi nào trống, giá bao nhiêu, cho tới khi gọi được.
//    · Quán phải có người nghe máy, ghi tay vào sổ, và tự nhớ buổi nào đã đầy.
//  Kết quả là mất khách ở đúng bước họ đã muốn mua.
//
//  CÁCH LÀM
//
//  Ba bảng, đúng ba câu hỏi khách hỏi theo thứ tự:
//
//    WorkshopSession   "hôm nào có buổi, mấy giờ, còn chỗ không, bao nhiêu tiền"
//    WorkshopDiscount  "tôi là sinh viên thì được giảm bao nhiêu"
//    WorkshopBooking   "giữ chỗ cho tôi"  (+ WorkshopBookingLine: ai được giảm)
//
//  VÌ SAO GIÁ NẰM Ở TỪNG BUỔI CHỨ KHÔNG PHẢI MỘT GIÁ CHUNG
//
//  Khung giờ khác nhau có giá khác nhau là chuyện bình thường: buổi sáng thứ Bảy
//  đông nên giá nguyên, buổi chiều thứ Sáu vắng nên hạ giá để lấp chỗ. Nếu giá
//  nằm ở cấu hình chung thì quán không có cách nào làm việc đó mà không sửa code.
//
//  VÌ SAO ƯU ĐÃI LÀ DỮ LIỆU CHỨ KHÔNG PHẢI ENUM CỨNG
//
//  "Sinh viên giảm 30%", "U22 giảm 20%", "đi bốn người giảm thêm 10%" đều là
//  quyết định kinh doanh, tháng sau có thể đổi. Viết cứng trong code thì mỗi lần
//  đổi khuyến mãi phải build lại và deploy lại cả hệ thống. Để trong bảng thì
//  chủ quán sửa được, và lịch sử đặt chỗ cũ vẫn giữ nguyên con số lúc đặt.
//
//  TIỀN LUÔN LÀ int, ĐƠN VỊ ĐỒNG — giống Order/OrderItem. Không dùng decimal:
//  tiền Việt không có phần lẻ, mà decimal mở đường cho sai số làm tròn.
// ==============================================================================

/// <summary>
/// Một BUỔI workshop cụ thể: ngày nào, mấy giờ, học gì, mấy chỗ, bao nhiêu tiền.
/// <para>
/// Đây là buổi THẬT trên lịch chứ không phải "mẫu buổi lặp hằng tuần". Lịch lặp
/// được sinh ra thành từng bản ghi rời (xem <c>WorkshopSeed</c>) vì buổi nào cũng
/// có thể bị dời giờ, đổi chủ đề, hạ giá hay hủy riêng nó — mà một quy tắc lặp
/// thì không diễn tả được những ngoại lệ đó.
/// </para>
/// </summary>
public class WorkshopSession : StoreScopedEntity
{
    /// <summary>Chủ đề buổi học. VD: "Matcha đánh tay".</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Một hai câu tả buổi học, hiện ngay dưới tên khung giờ trên trang đặt lịch.
    /// Khách chọn giờ dựa vào câu này chứ không mở trang khác để đọc.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Ngày diễn ra.
    /// <para>
    /// Dùng <see cref="DateOnly"/> (ánh xạ sang kiểu <c>date</c> của PostgreSQL)
    /// chứ không phải <see cref="DateTime"/>: một buổi học diễn ra vào "thứ Bảy
    /// ngày 3" theo giờ quán, không phải vào một mốc UTC. Lưu DateTime sẽ khiến
    /// buổi 8 giờ sáng giờ VN nhảy sang ngày hôm trước khi đọc ở múi giờ khác —
    /// đúng lỗi mà <c>DailyPlan.BusinessDate</c> đã phải né bằng cách lưu chuỗi.
    /// </para>
    /// </summary>
    public DateOnly SessionDate { get; set; }

    /// <summary>Giờ bắt đầu, giờ địa phương của quán.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Giờ kết thúc, giờ địa phương của quán.</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Số chỗ tối đa.
    /// <para>
    /// Workshop giới hạn bởi số BỘ DỤNG CỤ, không phải số ghế — bài giới thiệu
    /// hứa "mỗi người một bộ dụng cụ riêng". Đặt số này lớn hơn số bộ đang có là
    /// tự hứa điều không giữ được.
    /// </para>
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>Giá MỘT CHỖ, giá thường chưa ưu đãi, đơn vị đồng.</summary>
    public int BasePrice { get; set; }

    /// <summary>Tình trạng buổi. Xem <see cref="WorkshopSessionStatus"/>.</summary>
    public WorkshopSessionStatus Status { get; set; } = WorkshopSessionStatus.Open;

    /// <summary>Ghi chú nội bộ của quán, KHÔNG hiện cho khách.</summary>
    public string? Note { get; set; }

    /// <summary>Lý do hủy buổi — hiện cho khách đã đặt chỗ của buổi này.</summary>
    public string? CancelReason { get; set; }

    public ICollection<WorkshopBooking> Bookings { get; set; } = new List<WorkshopBooking>();

    /// <summary>Thời điểm bắt đầu, ghép ngày với giờ. Dùng để so với "bây giờ".</summary>
    public DateTime StartsAtLocal => SessionDate.ToDateTime(StartTime);
}

/// <summary>
/// Một ưu đãi workshop.
/// <para>
/// <see cref="Percent"/> là phần trăm giảm trên <see cref="WorkshopSession.BasePrice"/>,
/// KHÔNG phải số tiền cố định: giá mỗi buổi một khác, nên ưu đãi ghi bằng tiền
/// sẽ thành giảm 90% ở buổi rẻ và 10% ở buổi đắt.
/// </para>
/// </summary>
public class WorkshopDiscount : StoreScopedEntity
{
    /// <summary>Mã ưu đãi, duy nhất trong chi nhánh. VD: "HSSV". Dùng để seed lại không trùng.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên hiện cho khách. VD: "Học sinh – sinh viên".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Điều kiện, viết cho khách đọc. VD: "Xuất trình thẻ học sinh hoặc sinh viên còn hạn".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Áp cho từng chỗ hay cho cả lượt đặt. Xem <see cref="WorkshopDiscountScope"/>.</summary>
    public WorkshopDiscountScope Scope { get; set; }

    /// <summary>Phần trăm giảm, 0–100.</summary>
    public int Percent { get; set; }

    /// <summary>
    /// Chỉ với <see cref="WorkshopDiscountScope.Booking"/>: số chỗ tối thiểu của
    /// cả lượt thì mới được áp. 0 = không ràng buộc.
    /// </summary>
    public int MinSeats { get; set; }

    /// <summary>
    /// Chỉ với <see cref="WorkshopDiscountScope.Booking"/>: phải đặt trước buổi
    /// học ít nhất bao nhiêu NGÀY. 0 = không ràng buộc.
    /// </summary>
    public int MinDaysAhead { get; set; }

    /// <summary>
    /// Có phải trình giấy tờ khi tới quán không.
    /// <para>
    /// Hệ thống KHÔNG xác minh được khách có đúng là sinh viên hay không — và
    /// cũng không nên giả vờ làm được. Cờ này để giao diện nói thẳng với khách
    /// rằng giảm giá này cần trình thẻ, tránh cảnh tới nơi mới bị tính lại tiền.
    /// </para>
    /// </summary>
    public bool RequiresProof { get; set; }

    /// <summary>Câu nhắc về giấy tờ, hiện ngay cạnh ô chọn. VD: "Nhớ mang thẻ khi tới".</summary>
    public string? ProofNote { get; set; }

    /// <summary>Tắt ưu đãi mà không xóa — đơn cũ vẫn tham chiếu tới nó.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Thứ tự hiện trên giao diện. Nhỏ hơn thì lên trước.</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Một lượt đặt chỗ workshop.
/// <para>
/// Mọi con số tiền ở đây đều được CHỤP LẠI lúc đặt, không tính lại khi đọc —
/// giống <c>OrderItem.UnitPrice</c>. Quán đổi giá hay dừng ưu đãi vào tuần sau
/// thì lượt đặt tuần này vẫn giữ đúng số tiền đã báo cho khách.
/// </para>
/// </summary>
public class WorkshopBooking : StoreScopedEntity
{
    public Guid SessionId { get; set; }
    public WorkshopSession? Session { get; set; }

    /// <summary>
    /// Mã đặt chỗ khách dùng để tra cứu và hủy. VD: "WS-3K7QP2".
    /// Duy nhất toàn hệ thống — xem chỉ mục duy nhất trong AppDbContext.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại. Vừa là cách quán liên hệ, vừa là "mật khẩu" để tra cứu và
    /// hủy: biết mã đặt chỗ thôi chưa đủ để hủy chỗ của người khác.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>
    /// Tổng số chỗ, bằng tổng <see cref="WorkshopBookingLine.Quantity"/>.
    /// <para>
    /// Lưu dư ra ở đây là CỐ Ý: câu hỏi nóng nhất của trang đặt lịch là "buổi này
    /// còn mấy chỗ", hỏi mỗi giây vài lần. Có cột này thì chỉ cần cộng một cột số
    /// nguyên; không có thì phải nối thêm bảng dòng đặt chỗ cho mọi lần hỏi.
    /// </para>
    /// </summary>
    public int Seats { get; set; }

    /// <summary>Tổng tiền các dòng, đã trừ ưu đãi theo từng chỗ, chưa trừ ưu đãi cả lượt.</summary>
    public int Subtotal { get; set; }

    /// <summary>Ưu đãi áp cho cả lượt, nếu có. Null = không đủ điều kiện ưu đãi nào.</summary>
    public Guid? BookingDiscountId { get; set; }

    /// <summary>Tên ưu đãi cả lượt, chụp lại lúc đặt để hóa đơn cũ không đổi chữ.</summary>
    public string? BookingDiscountName { get; set; }

    /// <summary>Số tiền giảm của ưu đãi cả lượt, đồng.</summary>
    public int BookingDiscountAmount { get; set; }

    /// <summary>Số tiền khách phải trả = <see cref="Subtotal"/> − <see cref="BookingDiscountAmount"/>.</summary>
    public int GrandTotal { get; set; }

    public WorkshopBookingStatus Status { get; set; } = WorkshopBookingStatus.Pending;

    /// <summary>Ghi chú của khách. VD: "đi cùng trẻ nhỏ", "dị ứng sữa".</summary>
    public string? Note { get; set; }

    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CheckedInAt { get; set; }

    public ICollection<WorkshopBookingLine> Lines { get; set; } = new List<WorkshopBookingLine>();

    /// <summary>
    /// Lượt đặt này có đang CHIẾM CHỖ không.
    /// <para>
    /// Đây là định nghĩa DUY NHẤT của "chiếm chỗ" trong toàn hệ thống. Mọi phép
    /// đếm chỗ còn lại phải đi qua đây, nếu không sẽ có chỗ nơi thì coi NoShow là
    /// trống, nơi lại coi là đầy.
    /// </para>
    /// </summary>
    public bool HoldsSeat =>
        Status is WorkshopBookingStatus.Pending
               or WorkshopBookingStatus.Confirmed
               or WorkshopBookingStatus.CheckedIn
               or WorkshopBookingStatus.NoShow;
}

/// <summary>
/// Một dòng trong lượt đặt: mấy chỗ, theo diện ưu đãi nào, giá mỗi chỗ bao nhiêu.
/// <para>
/// VÌ SAO PHẢI TÁCH DÒNG: nhóm bốn người đi cùng nhau có thể gồm hai sinh viên và
/// hai người đi làm. Gán một diện ưu đãi cho cả lượt thì hoặc quán giảm cho cả
/// bốn (mất tiền), hoặc không giảm cho ai (mất khách).
/// </para>
/// </summary>
public class WorkshopBookingLine : BaseEntity
{
    public Guid BookingId { get; set; }
    public WorkshopBooking? Booking { get; set; }

    /// <summary>Ưu đãi áp cho các chỗ ở dòng này. Null = giá thường.</summary>
    public Guid? DiscountId { get; set; }

    /// <summary>Tên diện giá, chụp lại lúc đặt. "Giá thường" khi không có ưu đãi.</summary>
    public string TierName { get; set; } = string.Empty;

    /// <summary>Phần trăm giảm đã áp, chụp lại lúc đặt.</summary>
    public int Percent { get; set; }

    /// <summary>Số chỗ theo diện này.</summary>
    public int Quantity { get; set; }

    /// <summary>Giá một chỗ SAU ưu đãi theo chỗ, đồng.</summary>
    public int UnitPrice { get; set; }

    /// <summary>Thành tiền dòng = <see cref="UnitPrice"/> × <see cref="Quantity"/>.</summary>
    public int LineTotal { get; set; }
}
