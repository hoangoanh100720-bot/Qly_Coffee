using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM THANH TOÁN CHUYỂN KHOẢN (SePay)
//
//  CÁCH ĐỐI SOÁT HOẠT ĐỘNG — đọc trước khi sửa file này:
//
//    1. Lúc tạo đơn, hệ thống sinh MÃ THAM CHIẾU ngắn (VD "MCC7K2M9") và lưu
//       vào Order.PaymentRef.
//    2. Mã QR nhúng sẵn SỐ TIỀN và mã tham chiếu đó làm nội dung chuyển khoản,
//       nên khách chỉ quét rồi bấm xác nhận, không gõ tay chữ nào.
//    3. Tiền về tài khoản → SePay gọi webhook /hooks/sepay-payment.
//    4. Hệ thống dò mã tham chiếu trong nội dung chuyển khoản, tìm đúng đơn,
//       so số tiền rồi đánh dấu đã thanh toán.
//
//  VÌ SAO PHẢI LƯU LẠI TỪNG LẦN WEBHOOK (bảng payment_transactions):
//    - Chống xử lý trùng: SePay gửi lại tối đa 7 lần khi server trả lỗi. Không
//      có khóa duy nhất trên GatewayId thì một lần chuyển tiền có thể được ghi
//      nhận nhiều lần.
//    - Đối soát: giao dịch không khớp đơn nào (khách gõ sai nội dung, người lạ
//      chuyển nhầm) vẫn phải nằm lại đâu đó để chủ quán tra, chứ không được
//      lặng lẽ biến mất.
//    - Bằng chứng: khi khách bảo "em chuyển rồi", mở đúng bản ghi ra là xong.
// ==============================================================================

/// <summary>
/// Nhật ký một lần SePay báo có biến động số dư.
/// Mỗi bản ghi là MỘT giao dịch ngân hàng, dù có khớp được đơn hàng hay không.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    /// <summary>
    /// Id giao dịch do SePay cấp (trường <c>id</c> trong webhook).
    /// CÓ CHỈ MỤC DUY NHẤT — đây là toàn bộ cơ chế chống ghi nhận trùng.
    /// </summary>
    public long GatewayId { get; set; }

    /// <summary>Tên ngân hàng SePay báo về. VD: "Vietcombank", "MBBank".</summary>
    public string Gateway { get; set; } = string.Empty;

    /// <summary>Số tài khoản nhận tiền.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Tài khoản phụ (SePay dùng cho VA), thường null.</summary>
    public string? SubAccount { get; set; }

    /// <summary>"in" = tiền vào, "out" = tiền ra. Chỉ "in" mới xét thanh toán.</summary>
    public string TransferType { get; set; } = "in";

    /// <summary>Số tiền của giao dịch, đơn vị ĐỒNG.</summary>
    public int Amount { get; set; }

    /// <summary>Nội dung chuyển khoản nguyên văn từ ngân hàng.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Mã tham chiếu của ngân hàng (VD "MBVCB.3278907687") — dùng khi khiếu nại.</summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// Mã tham chiếu của quán rút ra được từ nội dung chuyển khoản, VD "MCC7K2M9".
    /// <c>null</c> nghĩa là không dò ra mã nào — giao dịch này không phải trả đơn.
    /// </summary>
    public string? DetectedRef { get; set; }

    /// <summary>Đơn hàng khớp được, nếu có.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Thời điểm giao dịch theo ngân hàng (đã quy về UTC).</summary>
    public DateTime TransactionDate { get; set; }

    public PaymentMatchStatus MatchStatus { get; set; } = PaymentMatchStatus.Unmatched;

    /// <summary>Diễn giải tiếng Việt vì sao ra kết quả đó — hiện thẳng cho chủ quán đọc.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Payload webhook nguyên văn (jsonb).
    /// <para>
    /// Lưu nguyên vì đây là dữ liệu TIỀN BẠC do bên thứ ba gửi tới: khi đối soát
    /// lệch, thứ duy nhất phân xử được là bản gốc chưa qua diễn giải của mình.
    /// SePay cũng có thể thêm trường mới mà mình chưa ánh xạ.
    /// </para>
    /// </summary>
    public string RawPayload { get; set; } = "{}";

    public Order? Order { get; set; }
}
