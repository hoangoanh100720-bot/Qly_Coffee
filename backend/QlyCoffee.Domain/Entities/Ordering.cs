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
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    // --- Tiền (tất cả tính bằng ĐỒNG, kiểu int) ---------------------------------

    /// <summary>Tổng tiền hàng trước giảm giá.</summary>
    public int Subtotal { get; set; }

    /// <summary>Tổng giảm giá đã áp dụng.</summary>
    public int DiscountTotal { get; set; }

    /// <summary>Số tiền khách phải trả = Subtotal − DiscountTotal.</summary>
    public int GrandTotal { get; set; }

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

    /// <summary>Thời điểm nhân viên xác nhận — cũng là lúc kho bị trừ.</summary>
    public DateTime? ConfirmedAt { get; set; }

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
