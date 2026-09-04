using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM ĐƠN HÀNG
//
//  QUY TẮC TRỪ KHO — đọc kỹ trước khi sửa bất cứ gì trong file này:
//
//    Pending    → chưa trừ kho (khách có thể bỏ giỏ hàng bất cứ lúc nào)
//    Confirmed  → TRỪ KHO tại đây, trong một transaction duy nhất
//    Preparing  → không động vào kho
//    Ready      → không động vào kho
//    Completed  → không động vào kho
//    Cancelled  → HOÀN KHO nếu trước đó đã trừ
//
//  Vì sao trừ ở Confirmed mà không phải lúc đặt hay lúc giao:
//    - Trừ lúc Pending: khách bỏ giỏ hàng thì kho bị trừ oan, phải viết thêm
//      cơ chế hết hạn giữ chỗ rất phức tạp.
//    - Trừ lúc Completed: trong lúc đang pha, hệ thống vẫn tưởng còn hàng nên
//      nhận thêm đơn, dẫn tới bán quá số lượng.
//    - Confirmed là đúng lúc quán cam kết làm món. Đơn giản và chính xác.
// ==============================================================================

/// <summary>
/// Đơn hàng của khách.
/// </summary>
public class Order : StoreScopedEntity
{
    /// <summary>
    /// Mã đơn hiển thị cho khách. Định dạng: "QC-YYMMDD-NNNN".
    /// VD: "QC-260810-0042" = đơn thứ 42 của ngày 10/08/2026.
    /// Duy nhất toàn hệ thống, dùng để tra cứu đơn không cần đăng nhập.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tài khoản đặt đơn. <c>null</c> nếu khách vãng lai không đăng nhập.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Tên người nhận — luôn bắt buộc kể cả khi có tài khoản.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại người nhận. Định dạng VN: bắt đầu 0 hoặc +84, 10 chữ số.
    /// Đây là dữ liệu cá nhân — phải có cơ chế xóa theo yêu cầu (Nghị định 13/2023).
    /// </summary>
    public string CustomerPhone { get; set; } = string.Empty;

    public OrderType OrderType { get; set; } = OrderType.Takeaway;

    /// <summary>Ghi chú của khách cho cả đơn. VD: "Ít đá tất cả các món".</summary>
    public string? Note { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Đơn này do khách tự đặt online hay nhân viên bấm tại quầy.
    /// Đơn tại quầy bỏ qua bước chờ xác nhận — nhân viên đã đứng trước mặt khách rồi.
    /// </summary>
    public OrderChannel Channel { get; set; } = OrderChannel.Online;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    // --- Đối soát chuyển khoản tự động (SePay) ----------------------------------

    /// <summary>
    /// Mã tham chiếu dùng làm NỘI DUNG CHUYỂN KHOẢN, VD "MCC7K2M9".
    /// <para>
    /// Vì sao không dùng thẳng <see cref="Code"/>: mã đơn có dấu gạch ngang và
    /// dài 14 ký tự, nhiều app ngân hàng cắt bớt hoặc bỏ ký tự đặc biệt trong
    /// nội dung. Mã này chỉ gồm chữ in và số, ngắn, và cố tình bỏ các ký tự dễ
    /// đọc nhầm (0/O, 1/I/L, 5/S, 8/B) để nhân viên đọc qua điện thoại không sai.
    /// </para>
    /// <para>
    /// Đây là chìa khóa để webhook SePay tìm ngược ra đơn — có chỉ mục duy nhất.
    /// </para>
    /// </summary>
    public string? PaymentRef { get; set; }

    /// <summary>Thời điểm ghi nhận đã trả đủ tiền.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Số tiền thực nhận, đơn vị đồng. Có thể LỚN HƠN <see cref="GrandTotal"/>
    /// khi khách chuyển dư — giữ nguyên số thật để đối soát sổ ngân hàng khớp.
    /// </summary>
    public int PaidAmount { get; set; }

    /// <summary>
    /// Id giao dịch SePay đã thanh toán cho đơn này.
    /// Có giá trị nghĩa là tiền được xác nhận TỰ ĐỘNG từ ngân hàng, không phải
    /// nhân viên bấm tay — khác biệt quan trọng khi có tranh chấp.
    /// </summary>
    public long? PaymentGatewayId { get; set; }

    // --- Tiền (tất cả tính bằng ĐỒNG, kiểu int) ---------------------------------

    /// <summary>Tổng tiền hàng trước giảm giá.</summary>
    public int Subtotal { get; set; }

    /// <summary>Tổng giảm giá đã áp dụng.</summary>
    public int DiscountTotal { get; set; }

    /// <summary>Số tiền khách phải trả = Subtotal − DiscountTotal (+ thuế nếu giá chưa gồm thuế).</summary>
    public int GrandTotal { get; set; }

    // --- Thuế GTGT (chụp lại tại thời điểm đặt) ---------------------------------
    //
    //  BA CỘT NÀY LÀ SNAPSHOT, KHÔNG ĐỌC LẠI TỪ BẢNG Stores.
    //  Thuế suất thay đổi theo nghị quyết của Quốc hội — 10% rồi 8% rồi có thể
    //  lại 10%. Hóa đơn in lại sau một năm phải ra ĐÚNG con số đã giao cho khách
    //  hôm đó, nếu không thì sổ sách và chứng từ lệch nhau.
    //  Cùng lý do với việc OrderItem chụp lại tên món và đơn giá.

    /// <summary>Chế độ thuế đã áp dụng, ứng với <c>QlyCoffee.Shared.TaxMode</c>.</summary>
    public int TaxMode { get; set; } = 1;

    /// <summary>Thuế suất phần trăm đã áp dụng cho đơn này.</summary>
    public int TaxRatePercent { get; set; }

    /// <summary>Tiền hàng CHƯA thuế. Với giá đã gồm thuế thì đây là số tách ngược ra.</summary>
    public int NetAmount { get; set; }

    /// <summary>Tiền thuế GTGT của đơn. Luôn thỏa <c>NetAmount + TaxAmount = GrandTotal</c>.</summary>
    public int TaxAmount { get; set; }

    /// <summary>
    /// Tổng giá vốn nguyên liệu THỰC TẾ đã trừ khỏi kho.
    /// <para>
    /// Được điền khi đơn chuyển sang Confirmed, tính từ giá vốn của đúng những lô
    /// đã bị trừ (không phải giá vốn bình quân). Nhờ vậy báo cáo lãi gộp chính xác
    /// tới từng đơn.
    /// </para>
    /// </summary>
    public int CostTotal { get; set; }

    /// <summary>Lãi gộp của đơn — thuộc tính tính toán.</summary>
    public int GrossProfit => GrandTotal - CostTotal;

    /// <summary>Chương trình khuyến mãi đã áp dụng, nếu có.</summary>
    public Guid? AppliedPromotionId { get; set; }

    // --- Cờ chống xử lý trùng ---------------------------------------------------

    /// <summary>
    /// Đã trừ kho cho đơn này chưa. Chống trường hợp gọi xác nhận hai lần
    /// (người dùng bấm nhanh, hoặc client retry khi mạng chập chờn).
    /// </summary>
    public bool StockDeducted { get; set; }

    /// <summary>Đã hoàn kho chưa. Chống hoàn kho hai lần khi hủy đơn.</summary>
    public bool StockReturned { get; set; }

    // --- Mốc thời gian ----------------------------------------------------------

    /// <summary>Thời điểm khách bấm đặt.</summary>
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm nhân viên xác nhận, tức là lúc đơn vào hàng pha.</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Thời điểm hệ thống HỨA với khách là món xong, tính lúc đơn vào hàng pha.
    /// <para>
    /// Đây là con số đã nói ra miệng với khách nên KHÔNG tính lại về sau. Giữ
    /// nguyên để đối chiếu với <see cref="CompletedAt"/> mà biết quán đang hứa
    /// sát hay hứa hão — nếu luôn xong sớm hơn hứa 3 phút thì <c>PrepSeconds</c>
    /// của các món đang đặt quá cao.
    /// </para>
    /// </summary>
    public DateTime? EstimatedReadyAt { get; set; }

    /// <summary>Thời điểm pha xong.</summary>
    public DateTime? ReadyAt { get; set; }

    /// <summary>Thời điểm giao cho khách.</summary>
    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    /// <summary>Lý do hủy — bắt buộc nhập, dùng để phân tích nguyên nhân mất đơn.</summary>
    public string? CancelReason { get; set; }

    public User? User { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

/// <summary>
/// Một dòng món trong đơn hàng.
/// <para>
/// LƯU Ý: các trường tên và giá được SAO CHÉP (snapshot) tại thời điểm đặt,
/// không đọc từ bảng Product. Lý do: nếu sau này quán đổi giá hoặc đổi tên món,
/// đơn cũ vẫn phải hiển thị đúng những gì khách đã mua và đã trả.
/// </para>
/// </summary>
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }

    /// <summary>Tên món tại thời điểm đặt (snapshot).</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Tên biến thể tại thời điểm đặt (snapshot). VD: "Size L".</summary>
    public string? VariantName { get; set; }

    /// <summary>Số ly. Giới hạn 1–20 để chặn spam.</summary>
    public int Quantity { get; set; }

    /// <summary>Đơn giá một ly ĐÃ GỒM phụ thu topping (snapshot, tính bằng đồng).</summary>
    public int UnitPrice { get; set; }

    /// <summary>Thành tiền dòng = UnitPrice × Quantity.</summary>
    public int LineTotal { get; set; }

    /// <summary>
    /// Danh sách topping đã chọn, lưu dạng JSON.
    /// Cấu trúc: <c>[{"modifierId":"...","name":"Trân châu đen","priceDelta":8000}]</c>
    /// <para>
    /// Lưu JSON thay vì bảng riêng vì đây là snapshot bất biến, không cần truy vấn
    /// theo topping, và tránh thêm một lần join khi hiển thị đơn.
    /// </para>
    /// </summary>
    public string ModifiersJson { get; set; } = "[]";

    /// <summary>Ghi chú riêng cho món này. VD: "Không đường".</summary>
    public string? Note { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
    public ProductVariant? Variant { get; set; }
}
