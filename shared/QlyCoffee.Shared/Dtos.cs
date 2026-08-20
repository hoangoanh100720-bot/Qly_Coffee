namespace QlyCoffee.Shared;

// ==============================================================================
//  DTO DÙNG CHUNG GIỮA BACKEND VÀ FRONTEND
//
//  Dự án tham chiếu thư viện này từ CẢ HAI phía, nên hợp đồng dữ liệu luôn khớp:
//  đổi tên trường ở đây thì frontend không biên dịch được ngay, thay vì lỗi
//  âm thầm lúc chạy.
//
//  Quy ước:
//    - Dùng `record` cho DTO chỉ đọc (phản hồi từ API).
//    - Dùng `class` cho DTO có ràng buộc form hai chiều (Blazor cần setter).
//    - Tiền luôn là int (đồng). Số lượng nguyên liệu là double.
//    - Không bao giờ trả về entity trực tiếp — tránh lộ trường nội bộ và vòng lặp JSON.
// ==============================================================================

// ------------------------------------------------------------------------------
//  PHẢN HỒI CHUNG
// ------------------------------------------------------------------------------

/// <summary>Bọc mọi phản hồi API để frontend xử lý lỗi thống nhất.</summary>
public record ApiResponse<T>(bool Success, T? Data, ApiError? Error)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null);
    public static ApiResponse<T> Fail(string code, string message, object? details = null)
        => new(false, default, new ApiError(code, message, details));
}

/// <summary>
/// Lỗi trả về từ API. <c>Code</c> để frontend xử lý theo nhánh,
/// <c>Message</c> là câu tiếng Việt hiển thị thẳng cho người dùng.
/// </summary>
public record ApiError(string Code, string Message, object? Details = null);

/// <summary>Kết quả phân trang.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;
}

// ------------------------------------------------------------------------------
//  MENU — dữ liệu trang bán hàng
// ------------------------------------------------------------------------------

public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    /// <summary>Màu đại diện danh mục — dùng tô chip lọc.</summary>
    string ColorHex,
    string IconKey,
    int ProductCount);

/// <summary>Món hiển thị trên lưới menu.</summary>
public record ProductCardDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,

    /// <summary>Màu thân nước — truyền vào DrinkGlass để vẽ minh họa.</summary>
    string ColorPrimaryHex,
    /// <summary>Màu lớp kem/foam.</summary>
    string ColorAccentHex,

    int BasePrice,
    /// <summary>Giá sau khuyến mãi. Bằng BasePrice nếu không có khuyến mãi.</summary>
    int EffectivePrice,
    /// <summary>Phần trăm giảm đang áp dụng. 0 nếu không giảm.</summary>
    int DiscountPercent,

    /// <summary>Còn bán được không — đọc từ Product.IsAvailable.</summary>
    bool IsAvailable,
    /// <summary>Số ly tối đa còn làm được với tồn kho hiện tại.</summary>
    int MaxServings,
    /// <summary>Lý do hết hàng. VD: "Hết Sữa tươi không đường".</summary>
    string? UnavailableReason,

    string CategoryName,
    IReadOnlyList<string> Tags);

/// <summary>Chi tiết món — dùng ở trang chọn size và topping.</summary>
public record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    string ColorPrimaryHex,
    string ColorAccentHex,
    int BasePrice,
    int EffectivePrice,
    int DiscountPercent,
    bool IsAvailable,
    int MaxServings,
    string? UnavailableReason,
    string CategoryName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<ModifierGroupDto> ModifierGroups);

public record VariantDto(
    Guid Id,
    string Name,
    int PriceDelta,
    bool IsDefault,
    /// <summary>Số ly tối đa còn làm được RIÊNG cho biến thể này (size L tốn nhiều hơn nên ít hơn).</summary>
    int MaxServings);

public record ModifierGroupDto(
    Guid Id,
    string Name,
    int MinSelect,
    int MaxSelect,
    bool IsRequired,
    IReadOnlyList<ModifierDto> Modifiers);

public record ModifierDto(
    Guid Id,
    string Name,
    int PriceDelta,
    /// <summary>Màu thật của topping — hiện chấm màu cạnh tên.</summary>
    string ColorHex,
    /// <summary>Topping có còn nguyên liệu không.</summary>
    bool IsAvailable);

// ------------------------------------------------------------------------------
//  GIỎ HÀNG & ĐẶT MÓN
// ------------------------------------------------------------------------------

/// <summary>Một dòng trong giỏ hàng (lưu ở localStorage phía client).</summary>
public class CartItem
{
    /// <summary>Khóa duy nhất của dòng giỏ hàng = productId + variantId + danh sách modifier đã sắp xếp.
    /// Nhờ vậy hai lần thêm cùng cấu hình sẽ cộng dồn số lượng thay vì tạo hai dòng.</summary>
    public string LineKey { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string ColorPrimaryHex { get; set; } = "#4A2C17";
    public string ColorAccentHex { get; set; } = "#C89968";

    public Guid? VariantId { get; set; }
    public string? VariantName { get; set; }

    public List<CartModifier> Modifiers { get; set; } = new();

    public int Quantity { get; set; } = 1;

    /// <summary>Đơn giá một ly đã gồm phụ thu topping và đã trừ khuyến mãi.</summary>
    public int UnitPrice { get; set; }

    /// <summary>Giá gốc trước khuyến mãi — để hiển thị giá gạch ngang.</summary>
    public int OriginalUnitPrice { get; set; }

    public string? Note { get; set; }

    /// <summary>Trần số lượng theo tồn kho. Giao diện chặn tăng vượt số này.</summary>
    public int MaxServings { get; set; } = 9999;

    public int LineTotal => UnitPrice * Quantity;
}

public class CartModifier
{
    public Guid ModifierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceDelta { get; set; }
    public string ColorHex { get; set; } = "#B8A38A";
}

/// <summary>Dữ liệu gửi lên khi khách bấm đặt món.</summary>
public class CreateOrderRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    /// <summary>0 = uống tại quán, 1 = mang đi.</summary>
    public int OrderType { get; set; } = 1;
    /// <summary>0 = tiền mặt, 1 = chuyển khoản.</summary>
    public int PaymentMethod { get; set; }
    public string? Note { get; set; }
    public List<CreateOrderItem> Items { get; set; } = new();
}

public class CreateOrderItem
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public List<Guid> ModifierIds { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public string? Note { get; set; }
}

public record CreateOrderResult(Guid OrderId, string Code, int GrandTotal, string Status);

/// <summary>
/// Chi tiết một món bị thiếu nguyên liệu — trả kèm lỗi INSUFFICIENT_STOCK
/// để giao diện chỉ đúng món nào không làm được.
/// </summary>
public record StockShortageDto(
    Guid ProductId,
    string ProductName,
    int MaxServings,
    string Reason);

// ------------------------------------------------------------------------------
//  ĐƠN HÀNG
// ------------------------------------------------------------------------------

public record OrderDto(
    Guid Id,
    string Code,
    string CustomerName,
    string CustomerPhone,
    int OrderType,
    int Status,
    string StatusLabel,
    int PaymentMethod,
    int PaymentStatus,
    int Subtotal,
    int DiscountTotal,
    int GrandTotal,
    int CostTotal,
    string? Note,
    DateTime PlacedAt,
    DateTime? ConfirmedAt,
    DateTime? ReadyAt,
    DateTime? CompletedAt,
    IReadOnlyList<OrderItemDto> Items);

public record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? VariantName,
    string ColorPrimaryHex,
    string ColorAccentHex,
    int Quantity,
    int UnitPrice,
    int LineTotal,
    IReadOnlyList<CartModifier> Modifiers,
    string? Note);

// ------------------------------------------------------------------------------
//  KHO
// ------------------------------------------------------------------------------

/// <summary>Nguyên liệu kèm tồn kho tổng hợp — dùng ở bảng quản lý kho.</summary>
public record IngredientStockDto(
    Guid Id,
    string Name,
    string Sku,
    int Category,
    string CategoryLabel,
    int BaseUnit,
    /// <summary>Ký hiệu đơn vị hiển thị: "g", "ml", "cái".</summary>
    string UnitLabel,

    /// <summary>Màu thật — vẽ icon và chấm màu.</summary>
    string ColorHex,
    string IconKey,

    /// <summary>Tổng tồn = Σ RemainingQuantity của các lô Active.</summary>
    double TotalStock,
    double MinStockLevel,
    double ReorderPoint,
    int AverageUnitCost,
    /// <summary>Giá trị tồn kho = TotalStock × AverageUnitCost.</summary>
    int StockValue,

    /// <summary>Số lô đang còn hàng.</summary>
    int ActiveLotCount,
    /// <summary>Số ngày tới hạn của lô gần hạn nhất. null = không có lô nào có hạn.</summary>
    int? NearestExpiryDays,
    /// <summary>Lượng đang nằm ở các lô cận hạn.</summary>
    double QuantityNearExpiry,

    bool IsBelowMinimum,
    bool IsBelowReorderPoint);

/// <summary>Một lô hàng — dùng ở bảng theo dõi hạn sử dụng.</summary>
public record InventoryLotDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    string IngredientColorHex,
    string IngredientIconKey,
    string UnitLabel,

    string LotCode,
    double ReceivedQuantity,
    double RemainingQuantity,
    int UnitCost,
    /// <summary>Giá trị còn lại = RemainingQuantity × UnitCost.</summary>
    int RemainingValue,

    DateTime ReceivedAt,
    DateTime? ExpiryDate,
    /// <summary>Số ngày còn lại. Âm nghĩa là đã quá hạn.</summary>
    int? DaysUntilExpiry,
    int Status,
    string StatusLabel,
    /// <summary>Mức độ cảnh báo: 0=Low 1=Medium 2=High 3=Critical.</summary>
    int Severity,
    string? SupplierName);

/// <summary>Dữ liệu form nhập kho.</summary>
public class ReceiveStockRequest
{
    public Guid IngredientId { get; set; }
    /// <summary>Số lượng theo ĐƠN VỊ MUA đã chọn (nếu có), ngược lại theo đơn vị cơ sở.</summary>
    public double Quantity { get; set; }
    /// <summary>Đơn vị mua. null = nhập thẳng theo đơn vị cơ sở.</summary>
    public Guid? PurchaseUnitId { get; set; }
    /// <summary>Giá vốn cho 1 ĐƠN VỊ CƠ SỞ (đồng).</summary>
    public int UnitCost { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? LotCode { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Dữ liệu form ghi nhận hao hụt.</summary>
public class RecordWasteRequest
{
    public Guid IngredientId { get; set; }
    public Guid? LotId { get; set; }
    public double Quantity { get; set; }
    /// <summary>Lý do — BẮT BUỘC. Không cho ghi hao hụt mà không giải trình.</summary>
    public string Reason { get; set; } = string.Empty;
}

// ------------------------------------------------------------------------------
//  MÓN & CÔNG THỨC ĐỊNH LƯỢNG (màn hình quản lý)
// ------------------------------------------------------------------------------

public record ProductAdminDto(
    Guid Id,
    string Name,
    string Slug,
    string CategoryName,
    string ColorPrimaryHex,
    string ColorAccentHex,
    int BasePrice,
    int ComputedCost,
    int MarginPercent,
    bool IsActive,
    bool IsAvailable,
    int MaxServings,
    string? UnavailableReason,
    int RecipeItemCount,

    /// <summary>Ảnh chụp thật. Rỗng thì giao diện dùng hình vẽ SVG.</summary>
    string? ImageUrl);

/// <summary>Một dòng công thức khi sửa món.</summary>
public class RecipeLineDto
{
    public Guid? Id { get; set; }
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string IngredientColorHex { get; set; } = "#B8A38A";
    public string IngredientIconKey { get; set; } = "generic";
    public string UnitLabel { get; set; } = "g";

    /// <summary>Lượng cho MỘT ly size mặc định, theo đơn vị cơ sở.</summary>
    public double Quantity { get; set; }

    public bool IsOptional { get; set; }
    public string? Note { get; set; }

    /// <summary>Giá vốn nguyên liệu (đồng / đơn vị cơ sở) — để tính chi phí dòng.</summary>
    public int UnitCost { get; set; }
    public double WastageRate { get; set; }

    /// <summary>Chi phí dòng này = Quantity × UnitCost × (1 + WastageRate).</summary>
    public int LineCost => (int)Math.Round(Quantity * UnitCost * (1 + WastageRate));

    /// <summary>Tồn kho hiện tại của nguyên liệu — cảnh báo ngay khi soạn công thức.</summary>
    public double CurrentStock { get; set; }
}

// ------------------------------------------------------------------------------
//  KẾ HOẠCH AI ⭐
// ------------------------------------------------------------------------------

/// <summary>Bản kế hoạch hằng ngày.</summary>
public record DailyPlanDto(
    Guid Id,
    string BusinessDate,
    DateTime GeneratedAt,

    // Số liệu — do backend tính
    int TotalValueAtRisk,
    int CriticalCount,
    int WarningCount,
    int LowStockCount,
    int Revenue,
    int OrderCount,
    int WasteValue,

    // Diễn giải — do Claude viết
    string? Headline,
    string? Summary,
    string? AiModel,
    string? AiError,

    int Status,
    IReadOnlyList<LotRiskDto> Risks,
    IReadOnlyList<PlanSuggestionDto> Suggestions);

/// <summary>
/// Kết quả phân tích rủi ro của một lô hàng.
/// Toàn bộ các số ở đây do backend tính, AI chỉ đọc để viết diễn giải.
/// </summary>
public record LotRiskDto(
    Guid LotId,
    string LotCode,
    Guid IngredientId,
    string IngredientName,
    string IngredientColorHex,
    string IngredientIconKey,
    string UnitLabel,

    double RemainingQuantity,
    int UnitCost,
    DateTime ExpiryDate,
    int DaysUntilExpiry,

    /// <summary>Tiêu thụ trung bình mỗi ngày, ước lượng bằng EWMA 28 ngày.</summary>
    double AvgDailyUsage,
    /// <summary>Dự kiến dùng được bao nhiêu trước khi hết hạn.</summary>
    double ProjectedUsage,
    /// <summary>Lượng có nguy cơ phải bỏ = max(0, còn lại − dự kiến dùng).</summary>
    double QuantityAtRisk,
    /// <summary>Giá trị có nguy cơ mất (đồng).</summary>
    int ValueAtRisk,

    int Severity,
    string SeverityLabel,

    /// <summary>Các món dùng nguyên liệu này, sắp theo mức tiêu thụ giảm dần.</summary>
    IReadOnlyList<CarrierProductDto> Carriers);

/// <summary>Món "chở hàng" — món tiêu thụ nhiều nguyên liệu đang cận hạn.</summary>
public record CarrierProductDto(
    Guid ProductId,
    string ProductName,
    string ColorPrimaryHex,
    /// <summary>Lượng nguyên liệu dùng cho 1 ly.</summary>
    double QuantityPerServing,
    int Price,
    int Cost,
    int MarginPercent,
    /// <summary>Số ly bán trung bình mỗi ngày (28 ngày gần nhất).</summary>
    double AvgDailyUnits);

/// <summary>Một đề xuất hành động trong bản kế hoạch.</summary>
public record PlanSuggestionDto(
    Guid Id,
    int Type,
    string TypeLabel,
    int Priority,
    string PriorityLabel,

    Guid? IngredientId,
    string? IngredientName,
    Guid? ProductId,
    string? ProductName,
    string? ProductColorHex,

    // Do AI viết
    string Title,
    string Reasoning,
    string? BannerCopy,

    // Do backend tính
    int? DiscountPercent,
    int? TargetUnits,
    int? ExpectedWasteAvoided,
    int? ExpectedNetBenefit,
    int? DaysUntilExpiry,
    double? QuantityAtRisk,

    DateTime? SuggestedFrom,
    DateTime? SuggestedTo,

    /// <summary>Đã có quyết định chưa. null = chưa xử lý.</summary>
    int? DecisionAction,
    string? DecisionNote);

/// <summary>Duyệt hoặc từ chối một đề xuất.</summary>
public class DecideSuggestionRequest
{
    public Guid SuggestionId { get; set; }
    /// <summary>0 = duyệt, 1 = từ chối, 2 = duyệt có sửa.</summary>
    public int Action { get; set; }
    public string? Note { get; set; }

    // Chỉ dùng khi Action = 2 (sửa)
    public int? OverrideDiscountPercent { get; set; }
    public DateTime? OverrideStartsAt { get; set; }
    public DateTime? OverrideEndsAt { get; set; }
}

// ------------------------------------------------------------------------------
//  DASHBOARD
// ------------------------------------------------------------------------------

public record DashboardDto(
    int TodayRevenue,
    int TodayOrderCount,
    int TodayGrossProfit,
    /// <summary>Tổng giá trị nguyên liệu đang cận hạn — con số quan trọng nhất.</summary>
    int ValueAtRisk,
    int CriticalLotCount,
    int LowStockIngredientCount,
    int PendingOrderCount,

    /// <summary>Doanh thu 7 ngày gần nhất để vẽ biểu đồ.</summary>
    IReadOnlyList<DailyRevenuePoint> RevenueTrend,
    /// <summary>Món bán chạy hôm nay.</summary>
    IReadOnlyList<TopProductDto> TopProducts,
    /// <summary>Bản kế hoạch AI mới nhất, nếu có.</summary>
    DailyPlanSummaryDto? LatestPlan);

public record DailyRevenuePoint(string BusinessDate, int Revenue, int OrderCount);

public record TopProductDto(
    Guid ProductId,
    string Name,
    string ColorPrimaryHex,
    int UnitsSold,
    int Revenue);

public record DailyPlanSummaryDto(
    Guid Id,
    string BusinessDate,
    string? Headline,
    int TotalValueAtRisk,
    int CriticalCount,
    int SuggestionCount,
    int UndecidedCount,
    int Status);

// ------------------------------------------------------------------------------
//  KHUYẾN MÃI
// ------------------------------------------------------------------------------

/// <summary>Chương trình khuyến mãi kèm số liệu hiệu quả thực tế.</summary>
public record PromotionDto(
    Guid Id,
    string Name,
    string? Description,
    int Type,
    int Value,
    int Status,
    string StatusLabel,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid? ProductId,
    string? ProductName,
    string? ProductColorHex,
    string? BannerText,

    /// <summary>
    /// Khuyến mãi này sinh từ đề xuất của AI hay do người tự tạo.
    /// Tỷ lệ khuyến mãi từ AI chạy hiệu quả là thước đo chất lượng của
    /// tính năng kế hoạch hằng ngày.
    /// </summary>
    bool IsFromAi,

    /// <summary>Số ly đã bán trong thời gian khuyến mãi chạy.</summary>
    int UnitsSold,
    /// <summary>Doanh thu sinh ra trong thời gian khuyến mãi chạy (đồng).</summary>
    int Revenue,

    /// <summary>Đang thực sự áp dụng ngay lúc này (Active và trong khoảng thời gian).</summary>
    bool IsRunning);

// ------------------------------------------------------------------------------
//  BÁO CÁO
// ------------------------------------------------------------------------------

/// <summary>Báo cáo tổng hợp theo khoảng ngày.</summary>
public record ReportDto(
    string FromDate,
    string ToDate,

    int Revenue,
    int Cost,
    int GrossProfit,
    int OrderCount,
    int AverageOrderValue,

    int WasteValue,
    /// <summary>Hao hụt trên doanh thu (%). Dưới 3% là tốt theo mặt bằng ngành.</summary>
    double WastePercent,

    IReadOnlyList<DailyRevenuePoint> RevenueTrend,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<WasteBreakdownDto> WasteBreakdown);

/// <summary>Hao hụt theo từng nguyên liệu.</summary>
public record WasteBreakdownDto(
    Guid IngredientId,
    string IngredientName,
    string ColorHex,
    string IconKey,
    double Quantity,
    int TotalCost,
    /// <summary>
    /// Phần hao hụt do QUÁ HẠN — tách riêng vì đây chính là loại tổn thất mà
    /// tính năng kế hoạch xả hàng cận hạn được xây để giảm bớt.
    /// </summary>
    int ExpiredCost);

// ------------------------------------------------------------------------------
//  XÁC THỰC
// ------------------------------------------------------------------------------

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record UserDto(Guid Id, string Email, string FullName, int Role, string RoleLabel, string? AvatarUrl);

/// <summary>
/// Kết quả sau khi tải hoặc gán ảnh cho món.
/// <para>
/// <c>ReplacedOld</c> cho biết ảnh cũ nào vừa bị thay — hữu ích để giao diện
/// báo rõ "đã thay ảnh" thay vì "đã thêm ảnh".
/// </para>
/// </summary>
public record ProductImageResult(
    string? Url,
    long SizeKb = 0,
    string? ReplacedOld = null);
