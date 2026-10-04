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
//  CỬA HÀNG — thông tin công khai, dùng cho giỏ hàng và hóa đơn
// ------------------------------------------------------------------------------

/// <summary>
/// Thông tin cửa hàng mà trang bán hàng cần biết.
/// <para>
/// Có riêng một endpoint thay vì nhét vào MenuResponse vì hai lý do: giỏ hàng
/// cần cấu hình thuế nhưng KHÔNG cần cả thực đơn, và dữ liệu này gần như không
/// đổi nên trình duyệt cache lại được.
/// </para>
/// </summary>
public record StoreInfoDto(
    string Name,
    string Address,
    string Phone,
    string? Email,

    /// <summary>Mã số thuế. null hoặc rỗng thì hóa đơn bỏ dòng này.</summary>
    string? TaxCode,

    /// <summary>Cách xử lý thuế GTGT — giá trị của <see cref="TaxMode"/>.</summary>
    int TaxMode,

    /// <summary>Thuế suất GTGT phần trăm đang áp dụng.</summary>
    int VatRatePercent,

    string OpenTime,
    string CloseTime,

    /// <summary>Đang nhận đơn hay tạm nghỉ.</summary>
    bool IsOpen);

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

    /// <summary>
    /// Gợi ý thưởng thức — dùng kèm gì thì ngon hơn. Rỗng thì giao diện ẩn hẳn khối này.
    /// </summary>
    string? PairingNote,

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

/// <summary>
/// Các loại nhóm tùy chọn mà giao diện phải đối xử đặc biệt.
/// <para>
/// Giá trị được lưu thẳng vào cột <c>modifier_groups.kind</c>. Nhóm do quán tự
/// thêm thì để rỗng và được hiển thị như mọi nhóm bình thường.
/// </para>
/// </summary>
public static class ModifierGroupKinds
{
    public const string Topping = "topping";
    public const string Sugar   = "sugar";
    public const string Ice     = "ice";

    /// <summary>
    /// Nhóm "dùng nóng hay dùng đá". Chọn nóng thì nhóm <see cref="Ice"/> bị ẩn đi:
    /// hỏi khách "bao nhiêu phần trăm đá" cho một ly cà phê nóng là vô nghĩa.
    /// </summary>
    public const string Temperature = "temperature";

    /// <summary>
    /// Lựa chọn "dùng nóng" bên trong nhóm <see cref="Temperature"/> nhận ra bằng tên,
    /// vì tên là thứ nhân viên quán nhìn thấy và tự sửa được (VD "Nóng", "Uống nóng").
    /// </summary>
    public static bool IsHotChoice(string modifierName)
        => modifierName.Contains("nóng", StringComparison.OrdinalIgnoreCase);
}

public record ModifierGroupDto(
    Guid Id,
    string Name,
    int MinSelect,
    int MaxSelect,
    bool IsRequired,
    IReadOnlyList<ModifierDto> Modifiers,

    /// <summary>Loại nhóm — xem <see cref="ModifierGroupKinds"/>. Rỗng = nhóm thường.</summary>
    string Kind = "");

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

    /// <summary>
    /// Mã tham chiếu chuyển khoản do màn hình quầy sinh sẵn, VD "MCC7K2M9".
    /// <para>
    /// Vì sao cho phép client gửi lên: ở quầy, mã QR hiện NGAY khi nhân viên
    /// chọn "Chuyển khoản", trước lúc chốt đơn — khách quét và trả tiền trong
    /// lúc nhân viên còn đang bấm nốt món. Mã in trên QR đó phải chính là mã
    /// được lưu vào đơn, nếu không webhook sẽ không khớp được.
    /// </para>
    /// <para>
    /// Backend KHÔNG tin tuyệt đối: mã sai định dạng hoặc đã có đơn khác dùng
    /// thì bị bỏ và backend tự sinh mã mới.
    /// </para>
    /// </summary>
    public string? PaymentRef { get; set; }
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

/// <summary>
/// Một nguyên liệu không đủ cho đơn đang nhận — trả kèm lỗi INSUFFICIENT_STOCK.
/// <para>
/// Luôn trả CẢ DANH SÁCH, không dừng ở nguyên liệu đầu tiên: nhân viên cần biết
/// một lần là phải nhập sữa, trân châu VÀ đường, chứ không phải bấm lại ba lần
/// để lần lượt phát hiện từng thứ.
/// </para>
/// </summary>
/// <param name="Required">Lượng đơn này cần, theo đơn vị cơ sở.</param>
/// <param name="Available">Lượng dùng được = tồn kho − phần các đơn trong hàng pha đã giữ.</param>
/// <param name="Missing">Còn thiếu = Required − Available.</param>
/// <param name="IsPrepared">Bán thành phẩm (cốt trà, nước đường…): phải sơ chế, không phải đi mua.</param>
public record IngredientShortageDto(
    Guid IngredientId,
    string IngredientName,
    string UnitLabel,
    bool IsPrepared,
    double Required,
    double Available,
    double Missing);

/// <summary>Chi tiết lỗi thiếu nguyên liệu của máy quầy và màn hình xác nhận đơn.</summary>
public record StockShortageDetailsDto(
    IReadOnlyList<StockShortageDto> Products,
    IReadOnlyList<IngredientShortageDto> Ingredients);

// ------------------------------------------------------------------------------
//  DANH SÁCH CẦN NHẬP HÀNG — hiện trên trang Kế hoạch và trang Nhập kho
// ------------------------------------------------------------------------------

/// <summary>Danh sách nguyên liệu cần nhập của một ngày.</summary>
/// <param name="IsToday">
/// true = số tồn kho là số của lúc này. Xem ngày cũ thì cột tồn kho vẫn là số
/// hiện tại (để biết đã nhập bù chưa), chỉ phần "bị chặn" là của ngày đó.
/// </param>
/// <param name="PrepItems">Bán thành phẩm đang thiếu — việc của bếp, không phải của người đi chợ.</param>
/// <param name="DoneItems">
/// Đã báo thiếu rồi đã nhập bù đủ — việc đã xong, không nằm trong Items nữa.
/// Giữ lại để màn hình nói được "hoàn tất" thay vì im lặng bỏ dòng đi.
/// </param>
public record RestockListDto(
    string BusinessDate,
    bool IsToday,
    DateTime ComputedAt,
    int CoverDays,
    IReadOnlyList<RestockItemDto> Items,
    IReadOnlyList<RestockItemDto> PrepItems,
    int TotalEstimatedCost,
    int BlockedIngredientCount,
    IReadOnlyList<RestockItemDto> DoneItems);

/// <summary>
/// Một dòng trong danh sách cần nhập.
/// </summary>
/// <param name="Status">
/// Mức độ, số nhỏ là gấp hơn: 0 hết hàng · 1 đã chặn đơn · 2 dưới mức tối thiểu ·
/// 3 không đủ bán ngày mai · 4 dưới điểm đặt hàng lại · 9 đã nhập bù xong.
/// </param>
/// <param name="OnHand">Tổng tồn các lô đang dùng.</param>
/// <param name="Reserved">Phần các đơn trong hàng pha sẽ dùng khi pha xong.</param>
/// <param name="Available">OnHand − Reserved, không âm.</param>
/// <param name="DaysOfCover">Đủ dùng bao nhiêu ngày theo tốc độ hiện tại; null nếu chưa có lịch sử tiêu thụ.</param>
/// <param name="SuggestedQuantity">Lượng nên nhập theo đơn vị cơ sở, đã làm tròn theo quy cách mua.</param>
/// <param name="SuggestedPurchaseText">VD "3 × Thùng 12 hộp 1L" hoặc "2 kg" — câu đọc được ngay khi gọi nhà cung cấp.</param>
/// <param name="EstimatedCost">Ước tính theo giá vốn bình quân, 0 nếu nguyên liệu chưa từng nhập.</param>
/// <param name="IsDone">Đã báo thiếu và đã nhập bù đủ — dòng chỉ để ghi nhận việc đã xong.</param>
/// <param name="ResolvedAt">Lúc đánh dấu xong; null khi việc còn treo.</param>
/// <param name="RestockedQuantity">Lượng đã nhập bù kể từ lần chặn đầu trong ngày.</param>
public record RestockItemDto(
    Guid IngredientId,
    string Name,
    string Sku,
    string CategoryLabel,
    string ColorHex,
    string IconKey,
    int BaseUnit,
    string UnitLabel,
    bool IsPrepared,
    int Status,
    string StatusLabel,
    double OnHand,
    double Reserved,
    double Available,
    double AvgDailyUsage,
    double? DaysOfCover,
    double MinStockLevel,
    double ReorderPoint,
    double SuggestedQuantity,
    string SuggestedPurchaseText,
    int AverageUnitCost,
    int EstimatedCost,
    int BlockedCount,
    double MaxMissingQuantity,
    string AffectedProducts,
    bool IsDone,
    DateTime? ResolvedAt,
    double RestockedQuantity);

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

    // --- Thuế GTGT đã chụp lại lúc đặt (xem Tax.cs) ---------------------------
    /// <summary>Chế độ thuế đã áp dụng, ứng với <see cref="TaxMode"/>.</summary>
    int TaxMode,
    /// <summary>Thuế suất phần trăm đã áp dụng cho đơn này.</summary>
    int TaxRatePercent,
    /// <summary>Tiền hàng chưa thuế. Luôn thỏa NetAmount + TaxAmount = GrandTotal.</summary>
    int NetAmount,
    /// <summary>Tiền thuế GTGT của đơn.</summary>
    int TaxAmount,

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
    string? Note,

    /// <summary>
    /// Ảnh riêng của món nếu quán đã tải lên. Rỗng thì giao diện dùng ảnh mẫu
    /// theo nhóm (<c>DrinkPhoto.PhotoFor</c>) — không bao giờ để trống ô ảnh.
    /// </summary>
    string? ImageUrl = null);

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

// ------------------------------------------------------------------------------
//  SƠ CHẾ — XUẤT NGUYÊN LIỆU THÔ ĐỂ LÀM BÁN THÀNH PHẨM
// ------------------------------------------------------------------------------

/// <summary>
/// Một công thức sơ chế kèm tình trạng hiện tại — tất cả những gì màn hình
/// Sơ chế cần để vẽ một thẻ, không phải gọi thêm lần nào nữa.
/// <list type="bullet">
/// <item><c>OutputStock</c> — còn bao nhiêu cốt, theo đơn vị cơ sở.</item>
/// <item><c>ServingsLeft</c> — quy ra còn pha được mấy ly, con số nhân viên thật sự cần.</item>
/// <item><c>MaxBatches</c> — còn nguyên liệu thô để làm mấy mẻ nữa.</item>
/// <item><c>BlockingIngredient</c> — thiếu thứ gì thì không ủ được. null = còn đủ.</item>
/// </list>
/// </summary>
public record PrepRecipeDto(
    Guid Id,
    string Code,
    string Name,
    string? Instructions,

    Guid OutputIngredientId,
    string OutputName,
    string OutputColorHex,
    string OutputIconKey,
    string UnitLabel,

    /// <summary>Sản lượng một mẻ chuẩn, theo đơn vị cơ sở của bán thành phẩm.</summary>
    double OutputQuantity,
    int ShelfLifeHours,
    int PrepMinutes,

    double OutputStock,
    int ServingsLeft,
    double MaxBatches,
    string? BlockingIngredient,

    IReadOnlyList<PrepInputDto> Inputs,
    IReadOnlyList<PrepBatchDto> ActiveBatches);

/// <summary>Một dòng nguyên liệu thô của công thức sơ chế, kèm tồn kho hiện có.</summary>
public record PrepInputDto(
    Guid IngredientId,
    string Name,
    string ColorHex,
    string IconKey,
    string UnitLabel,
    /// <summary>Lượng cần cho MỘT mẻ chuẩn.</summary>
    double QuantityPerBatch,
    double StockAvailable,
    string? Note);

/// <summary>
/// Một mẻ đang còn dùng được.
/// <para>
/// <c>MinutesLeft</c> tính bằng PHÚT chứ không phải ngày: câu nhân viên cần trả
/// lời là "bình này còn dùng được bao lâu nữa", và với cốt trà thì đơn vị ngày
/// không nói lên điều gì. Số âm nghĩa là đã quá hạn mà job chưa kịp quét.
/// </para>
/// </summary>
public record PrepBatchDto(
    Guid LotId,
    string LotCode,
    double RemainingQuantity,
    double ReceivedQuantity,
    int UnitCost,
    DateTime ProducedAt,
    DateTime? ExpiresAt,
    int MinutesLeft,
    string? Note);

/// <summary>Dữ liệu form chạy một mẻ sơ chế.</summary>
public class ProduceBatchRequest
{
    public Guid PrepRecipeId { get; set; }

    /// <summary>
    /// Số mẻ. Cho phép số lẻ (0,5 mẻ) vì giờ vắng nhân viên hay ủ nửa bình —
    /// ủ cả bình rồi đổ đi một nửa là lãng phí có thật.
    /// </summary>
    public double BatchCount { get; set; } = 1;

    public string? Note { get; set; }
}

/// <summary>Kết quả một mẻ vừa chạy — đủ để báo lại cho nhân viên mà không phải tải lại trang.</summary>
public record PrepBatchResultDto(
    Guid LotId,
    string LotCode,
    string OutputName,
    double OutputQuantity,
    string UnitLabel,
    DateTime ExpiresAt,
    /// <summary>Tổng giá vốn nguyên liệu thô đã tiêu tốn cho mẻ này (đồng).</summary>
    int TotalInputCost,
    int UnitCost,
    IReadOnlyList<PrepConsumedDto> Consumed);

/// <summary>Một dòng nguyên liệu thô đã bị trừ khi chạy mẻ.</summary>
public record PrepConsumedDto(
    string IngredientName,
    double Quantity,
    string UnitLabel,
    int Cost);

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

    /// <summary>Ảnh chụp thật. Rỗng thì giao diện dùng ảnh mẫu theo nhóm món.</summary>
    string? ImageUrl,

    // --- Định giá -------------------------------------------------------------

    /// <summary>0 = đồ uống đá · 1 = đồ uống nóng · 2 = đồ ăn.</summary>
    int ServeStyle,

    /// <summary>
    /// Tỷ lệ giá vốn mục tiêu của nhóm này: đồ uống 30%, đồ ăn 45%.
    /// Xem <see cref="Pricing"/> để biết vì sao hai con số khác nhau.
    /// </summary>
    int TargetCostRatioPercent,

    /// <summary>
    /// Giá bán ĐỀ NGHỊ tính từ giá vốn, làm tròn lên bội số 1.000đ.
    /// Chỉ là gợi ý — người dùng luôn được đặt giá khác.
    /// </summary>
    int SuggestedPrice,

    /// <summary>Tỷ lệ giá vốn thực tế trên giá đang bán, phần trăm.</summary>
    int CostRatioPercent,

    /// <summary>-1 đang bán lỗ · 0 biên mỏng · 1 lành mạnh · 2 cao hơn mặt bằng.</summary>
    int PriceVerdict,

    /// <summary>Số topping đang bán mà món này nhận. 0 = món không cho gọi thêm topping.</summary>
    int ToppingCount = 0)
{
    /// <summary>Món này là đồ ăn — trang quản lý tách riêng khu vực chỉnh giá cho nhóm này.</summary>
    public bool IsFood => ServeStyle == 2;
}

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

/// <summary>
/// Một topping khách gọi thêm được cho món, kèm định lượng nó trừ kho.
/// <para>
/// Hiện ở trang quản lý món để chủ quán thấy món nào nhận topping gì và mỗi phần
/// topping tốn bao nhiêu — topping không nhân hệ số size nên định lượng là cố định.
/// </para>
/// </summary>
public record ProductToppingDto(
    Guid Id,
    string Name,
    int PriceDelta,
    string ColorHex,
    bool IsActive,
    List<RecipeLineDto> Lines)
{
    /// <summary>Giá vốn một phần topping.</summary>
    public int Cost => Lines.Sum(l => l.LineCost);
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
    int Revenue,

    /// <summary>Ảnh riêng của món. Rỗng thì giao diện dùng ảnh mẫu theo nhóm.</summary>
    string? ImageUrl = null);

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
    bool IsRunning,

    /// <summary>
    /// Ảnh riêng của món được khuyến mãi. Rỗng thì giao diện dùng ảnh mẫu theo
    /// nhóm; khuyến mãi toàn menu (ProductId null) không có ảnh món nào cả.
    /// </summary>
    string? ProductImageUrl = null);

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

// ------------------------------------------------------------------------------
//  BÁN HÀNG TẠI QUẦY & MÀN HÌNH PHA CHẾ
// ------------------------------------------------------------------------------

public record PosCategoryDto(Guid Id, string Name, string Slug, string ColorHex);

public record PosVariantDto(Guid Id, string Name, int PriceDelta, bool IsDefault);

/// <summary>
/// Một topping khách có thể gọi thêm ở quầy.
/// <para>
/// CHỈ gồm các nhóm tùy chọn KHÔNG bắt buộc. Mức đường và mức đá là nhóm bắt
/// buộc, giá bằng 0 và ở quầy đã có nút bấm nhanh ("Ít đá", "Không ngọt") —
/// đưa chúng vào đây nữa là bắt nhân viên chọn hai lần cho cùng một thứ.
/// </para>
/// <para>
/// Topping thì khác: nó CÓ tiền và CÓ trừ kho, nên bắt buộc phải ghi vào đơn
/// chứ không thể để trong ghi chú — ghi chú không cộng tiền và không trừ kho.
/// </para>
/// </summary>
/// <param name="IsAvailable">
/// Còn đủ nguyên liệu để làm hay không. Hết thì giao diện quầy vẫn HIỆN nhưng
/// khóa nút — nhân viên cần thấy để nói với khách "hôm nay hết trân châu", chứ
/// topping tự biến mất thì họ tưởng mình bấm nhầm chỗ.
/// </param>
/// <param name="PrepRecipeId">
/// Công thức sơ chế làm ra nguyên liệu đang thiếu, nếu có. Quầy dùng id này để
/// bấm nấu thêm ngay tại chỗ, không phải bỏ khách chạy sang trang Sơ chế.
/// </param>
/// <param name="PrepMinutes">
/// Nấu một mẻ mất bao nhiêu phút. Chính là con số nhân viên nói với khách —
/// "trân châu hết rồi, chờ em 15 phút nhé".
/// </param>
public record PosToppingDto(
    Guid Id, string Name, int PriceDelta, string ColorHex, bool IsAvailable,
    Guid? PrepRecipeId = null, string? PrepName = null, int PrepMinutes = 0);

/// <summary>
/// Một món trên lưới bấm đơn.
/// <para>
/// <c>MaxServings</c> ở đây ĐÃ TRỪ phần đang nằm trong hàng pha — khác với
/// <c>Product.MaxServings</c> trong database chỉ nhìn tồn kho. Nhân viên cần con
/// số sau khi trừ, vì mấy ly đang chờ pha đã chiếm nguyên liệu rồi.
/// </para>
/// </summary>
public record PosProductDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    int BasePrice,
    string ColorPrimaryHex,
    string ColorAccentHex,
    string? ImageUrl,
    int MaxServings,
    int QuantityInQueue,
    bool IsAvailable,
    int PrepSeconds,
    List<PosVariantDto> Variants,

    /// <summary>
    /// Id của những topping áp dụng được cho MÓN NÀY. Tra tên và giá trong
    /// <c>PosMenuDto.Toppings</c>.
    /// <para>
    /// Trả về danh sách id thay vì lặp lại cả tên và giá ở từng món: 56 món ×
    /// 7 topping là 392 bản sao của cùng bảy dòng dữ liệu. Rỗng nghĩa là món
    /// không nhận topping — bánh và đồ ăn nằm nhóm này.
    /// </para>
    /// </summary>
    List<Guid> ToppingIds,

    // --- Món hết hàng: nấu thêm được hay phải ngưng bán? ----------------------
    //
    //  "Hết" là câu trả lời cụt. Nhân viên đứng trước khách cần biết TIẾP THEO
    //  LÀM GÌ: nấu thêm mẻ trân châu 15 phút rồi bán tiếp, hay xin lỗi khách vì
    //  hôm nay hết hẳn. Ba trường dưới đây trả lời đúng câu đó.

    /// <summary>
    /// Số phút chờ nếu nấu thêm ngay bây giờ. 0 = không cần sơ chế gì.
    /// Đây là con số nói với khách.
    /// </summary>
    int PrepWaitMinutes = 0,

    /// <summary>Công thức sơ chế cần chạy để bán lại được món này.</summary>
    Guid? PrepRecipeId = null,

    /// <summary>
    /// Hết hàng và KHÔNG nấu nhanh được — phải nhập thêm hoặc ủ mẻ dài.
    /// Giao diện hiện "Tạm ngưng" thay vì "Hết", vì hai chuyện khác nhau:
    /// một cái chờ 15 phút là có, một cái hôm nay đừng bán nữa.
    /// </summary>
    bool IsSuspended = false);

/// <param name="Toppings">
/// Bảng topping dùng chung cho cả thực đơn, đã khử trùng lặp.
/// </param>
public record PosMenuDto(
    List<PosCategoryDto> Categories,
    List<PosProductDto> Products,
    List<PosToppingDto> Toppings);

public record EstimateItem(Guid ProductId, int Quantity);
public record EstimateRequest(List<EstimateItem> Items);

/// <summary>
/// Thời gian ước tính, kèm số liệu đã dùng để tính ra nó.
/// Hiện cả <c>QueuedItemsAhead</c> để nhân viên giải thích được với khách vì sao
/// hôm nay chờ lâu — "đang có 8 ly trước bạn" thuyết phục hơn một con số trơ trọi.
/// </summary>
public record EtaDto(
    int TotalSeconds,
    int Minutes,
    int QueuedItemsAhead,
    int WorkAheadSeconds,
    int Stations);

public record PosOrderResultDto(
    Guid Id,
    string Code,
    int GrandTotal,
    int EtaMinutes,
    DateTime? EstimatedReadyAt,
    int TotalCups);

/// <summary>
/// Phiếu pha của MỘT món trong đơn: định lượng cho MỘT ly, đã nhân hệ số size
/// và đã áp mức đá / mức đường khách chọn. Hiện trên thẻ ở màn hình Pha chế.
/// </summary>
/// <param name="Quantity">Số ly của dòng đơn này — định lượng trong Lines là cho một ly.</param>
public record BarRecipeDto(
    Guid OrderItemId,
    string ProductName,
    string? VariantName,
    int Quantity,
    string Choices,
    IReadOnlyList<BarRecipeLineDto> Lines);

/// <param name="Quantity">Lượng cho một ly, theo đơn vị cơ sở. 0 khi dòng bị bỏ.</param>
/// <param name="IsTopping">Dòng đến từ topping khách chọn, không phải công thức gốc.</param>
/// <param name="SkipReason">Có giá trị = KHÔNG cho thứ này vào ly (VD "Khách chọn không đá").</param>
public record BarRecipeLineDto(
    string IngredientName,
    string ColorHex,
    double Quantity,
    string UnitLabel,
    bool IsOptional,
    bool IsTopping,
    string? Note,
    string? SkipReason);

public record BarQueueLineDto(
    Guid OrderId,
    Guid OrderItemId,
    string OrderCode,
    int QueuePosition,
    string CustomerName,
    int Channel,
    string ProductName,
    string? VariantName,
    string ColorPrimaryHex,
    string ColorAccentHex,
    string? ImageUrl,
    int Quantity,
    string? Note,
    string Modifiers,
    int Status,
    DateTime QueuedAt,
    DateTime? EstimatedReadyAt,
    int WaitedSeconds,
    bool IsLate);

public record SoldTodayDto(
    Guid ProductId,
    string ProductName,
    string ColorPrimaryHex,
    string? ImageUrl,
    int QuantitySold,
    int QuantityInQueue,
    int Revenue);

public record BarQueueDto(
    string BusinessDate,
    List<BarQueueLineDto> Queue,
    List<SoldTodayDto> SoldToday,
    int TotalCupsInQueue,
    int TotalCupsSoldToday,
    int TotalOrdersToday,
    int RevenueToday,
    int Stations,
    int EstimatedClearSeconds);

/// <summary>
/// Kết quả sau khi bấm Hoàn tất.
/// <para>
/// <c>PromisedVsActualSeconds</c> ÂM nghĩa là xong sớm hơn lời hứa, DƯƠNG là trễ.
/// Đây là thước đo duy nhất cho biết <c>PrepSeconds</c> của các món đang đặt sát
/// thực tế hay không.
/// </para>
/// </summary>
public record CompleteOrderResultDto(
    Guid Id,
    string Code,
    int Status,
    int CostTotal,
    int GrossProfit,
    int? PromisedVsActualSeconds);

/// <summary>
/// Năm con số hiện trên thanh điều hướng quản lý.
/// <para>
/// <c>CupsInQueue</c> đếm theo LY chứ không theo đơn: người pha cần biết còn
/// phải làm bao nhiêu ly, còn "3 đơn" thì có thể là 3 ly mà cũng có thể là 15.
/// </para>
/// </summary>
/// <para>
/// <c>PrepNeeded</c> đếm số bán thành phẩm đã hết hoặc sắp hết — tức số mẻ cần
/// ủ lại ngay. Việc này phải làm TRƯỚC khi có khách, nên nó cần một con số trên
/// thanh điều hướng chứ không phải chờ tới lúc món hiện "tạm hết" mới biết.
/// </para>
public record NavBadgesDto(
    int PendingOrders,
    int CupsInQueue,
    int CriticalLots,
    int UndecidedSuggestions,
    int PrepNeeded = 0);

// ------------------------------------------------------------------------------
//  THANH TOÁN CHUYỂN KHOẢN (SePay)
// ------------------------------------------------------------------------------

/// <summary>
/// Mọi thứ giao diện cần để hiện màn hình "quét mã trả tiền" cho một đơn.
/// <para>
/// Ảnh QR do BACKEND dựng đường dẫn chứ không phải frontend tự ghép, vì số tiền
/// và nội dung chuyển khoản in trên mã phải khớp tuyệt đối với thứ mà webhook
/// sẽ đối soát. Hai nơi cùng ghép chuỗi là hai nơi có thể lệch nhau.
/// </para>
/// </summary>
public record PaymentInfoDto(
    string OrderCode,

    /// <summary>0 = tiền mặt, 1 = chuyển khoản.</summary>
    int PaymentMethod,

    /// <summary>0 = chưa thanh toán, 1 = đã thanh toán, 2 = đã hoàn tiền.</summary>
    int PaymentStatus,

    string PaymentStatusLabel,

    /// <summary>Số tiền phải trả, đơn vị đồng.</summary>
    int Amount,

    /// <summary>Số tiền đã thực nhận. Lớn hơn Amount nghĩa là khách chuyển dư.</summary>
    int PaidAmount,

    DateTime? PaidAt,

    /// <summary>Nội dung chuyển khoản khách phải ghi, VD "MCC7K2M9".</summary>
    string? Reference,

    string BankCode,
    string AccountNumber,
    string AccountName,

    /// <summary>Đường dẫn ảnh QR đã nhúng sẵn số tiền và nội dung. Rỗng nếu chưa cấu hình.</summary>
    string QrImageUrl,

    /// <summary>
    /// Đã khai báo đủ tài khoản để dựng QR chưa. Giao diện phải hỏi cờ này TRƯỚC
    /// khi vẽ thẻ img — chưa cấu hình mà vẫn vẽ thì khách thấy ảnh vỡ ngay tại
    /// bước trả tiền.
    /// </summary>
    bool IsConfigured,

    /// <summary>
    /// Webhook SePay đã bật chưa. Tắt thì khách vẫn quét QR trả được, chỉ là
    /// nhân viên phải bấm xác nhận tay — giao diện cần nói rõ điều đó.
    /// </summary>
    bool AutoConfirm);

/// <summary>
/// Payload webhook SePay gửi khi tài khoản có biến động số dư.
/// <para>
/// Tên trường giữ ĐÚNG như tài liệu SePay (camelCase) — đây là hợp đồng của bên
/// thứ ba, không phải chỗ để đặt lại tên cho hợp gu.
/// </para>
/// <para>
/// Mọi trường đều nullable vì đây là dữ liệu từ NGOÀI vào: thiếu trường thì
/// phải trả lỗi có kiểm soát, không được để ném NullReference giữa chừng rồi
/// SePay gửi lại bảy lần.
/// </para>
/// </summary>
public class SePayWebhookPayload
{
    /// <summary>Id giao dịch trên SePay. Khóa chống xử lý trùng.</summary>
    public long Id { get; set; }

    /// <summary>Tên ngân hàng. VD: "Vietcombank".</summary>
    public string? Gateway { get; set; }

    /// <summary>Thời điểm giao dịch, dạng "yyyy-MM-dd HH:mm:ss" theo GIỜ VIỆT NAM.</summary>
    public string? TransactionDate { get; set; }

    public string? AccountNumber { get; set; }

    /// <summary>
    /// Mã do SePay tự bóc tách theo cấu hình tiền tố ở trang quản trị.
    /// Thường null nếu chưa khai báo tiền tố — khi đó ta tự dò trong Content.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Nội dung chuyển khoản.</summary>
    public string? Content { get; set; }

    /// <summary>"in" = tiền vào, "out" = tiền ra.</summary>
    public string? TransferType { get; set; }

    /// <summary>Số tiền giao dịch. SePay gửi kiểu số, có thể có phần thập phân.</summary>
    public decimal TransferAmount { get; set; }

    /// <summary>Số dư lũy kế sau giao dịch. Ghi lại để đối chiếu, không dùng vào nghiệp vụ.</summary>
    public decimal Accumulated { get; set; }

    public string? SubAccount { get; set; }

    /// <summary>Mã tham chiếu của ngân hàng, VD "MBVCB.3278907687".</summary>
    public string? ReferenceCode { get; set; }

    public string? Description { get; set; }
}

/// <summary>Kết quả xử lý một lần webhook — trả về cho SePay và ghi vào log.</summary>
public record SePayWebhookResult(
    bool Success,
    string Message,

    /// <summary>Mã đơn đã khớp, null nếu không khớp đơn nào.</summary>
    string? OrderCode,

    /// <summary>Giá trị của <c>PaymentMatchStatus</c>.</summary>
    int MatchStatus,

    /// <summary>true nếu giao dịch này đã được xử lý ở lần gửi trước.</summary>
    bool Duplicate);

// ------------------------------------------------------------------------------
//  BÓNG ĐÁ TRỰC TIẾP
// ------------------------------------------------------------------------------

/// <summary>
/// Địa chỉ hub và tên sự kiện SignalR. Để ở thư viện dùng chung vì hai phía
/// phải khớp TỪNG CHỮ — lệch một ký tự là client vẫn nối được nhưng không bao
/// giờ nhận được gì, và cũng không có lỗi nào báo ra.
/// </summary>
public static class LiveScoreChannel
{
    public const string HubPath = "hubs/live-score";
    public const string BoardUpdated = "BoardUpdated";
}

/// <summary>
/// Giai đoạn của một trận, đã quy về năm nhóm mà giao diện cần phân biệt.
/// <para>
/// Nguồn dữ liệu có hơn chục trạng thái (TIMED, IN_PLAY, PAUSED, EXTRA_TIME…).
/// Giao diện không cần biết từng cái — nó chỉ cần biết xếp trận vào nhóm nào.
/// Nhãn chi tiết ("Hiệp phụ", "Hoãn") đi riêng trong <c>StatusLabel</c>.
/// </para>
/// </summary>
public static class MatchPhase
{
    public const string Upcoming = "upcoming";
    public const string Live     = "live";
    /// <summary>Nghỉ giữa hiệp — vẫn tính là đang diễn ra.</summary>
    public const string Break    = "break";
    public const string Finished = "finished";
    /// <summary>Hoãn, hủy, tạm dừng.</summary>
    public const string Off      = "off";
}

/// <summary>Một trận trên bảng tỉ số.</summary>
public record LiveMatchDto(
    long Id,
    string Competition,
    string? CompetitionEmblem,
    string HomeTeam,
    string? HomeCrest,
    string AwayTeam,
    string? AwayCrest,

    /// <summary>Null khi trận chưa đá.</summary>
    int? HomeScore,
    int? AwayScore,

    /// <summary>Giá trị của <see cref="MatchPhase"/>.</summary>
    string Phase,

    /// <summary>Nhãn tiếng Việt: "Đang đá", "Nghỉ giữa hiệp", "Hoãn"…</summary>
    string StatusLabel,

    /// <summary>
    /// Phút đang đá. Null khi nguồn không cung cấp — gói miễn phí có thể không
    /// có. Giao diện khi đó hiện "Đang đá" chứ không bịa ra một con số.
    /// </summary>
    int? Minute,
    int? InjuryTime,

    DateTime KickoffUtc);

/// <summary>
/// Toàn bộ bảng tỉ số — máy chủ đẩy NGUYÊN KHỐI mỗi lần có thay đổi.
/// <para>
/// Gửi cả bảng thay vì từng trận vừa đổi vì bảng chỉ vài KB, mà client khỏi
/// phải tự ghép: lỡ mất một gói lúc mạng chập chờn thì gói sau tự sửa lại hết.
/// </para>
/// </summary>
public record LiveScoreBoardDto(
    /// <summary>Đã khai báo FOOTBALL_API_KEY chưa. false thì giao diện chỉ hiện hướng dẫn.</summary>
    bool Enabled,

    /// <summary>
    /// Lần cuối DỮ LIỆU THAY ĐỔI (không phải lần cuối hỏi nguồn), giờ UTC.
    /// Null nghĩa là máy chủ chưa lấy được lần nào.
    /// </summary>
    DateTime? UpdatedAtUtc,

    IReadOnlyList<LiveMatchDto> Matches,

    /// <summary>Lời nhắn khi nguồn lỗi. Tỉ số cũ vẫn giữ nguyên bên dưới.</summary>
    string? Notice = null);

// ------------------------------------------------------------------------------
//  WORKSHOP PHA CHẾ — ĐẶT LỊCH
//
//  Hợp đồng dữ liệu của trang /dat-lich-workshop.
//
//  NGÀY GIỜ TRUYỀN BẰNG CHUỖI "yyyy-MM-dd" VÀ "HH:mm", KHÔNG PHẢI DateTime.
//  Buổi học diễn ra vào "thứ Bảy 8 giờ sáng theo giờ quán", không phải vào một
//  mốc UTC. Truyền DateTime thì trình duyệt của khách ở múi giờ khác sẽ hiện
//  sang ngày hôm trước — mà khách đó vẫn tới quán vào đúng thứ Bảy.
//
//  GIÁ DO MÁY CHỦ TÍNH. Giao diện có tính lại để hiện ngay khi khách bấm, nhưng
//  con số đó chỉ để xem: lúc đặt, máy chủ tính lại từ đầu và lấy kết quả của
//  chính nó. Tin số tiền do trình duyệt gửi lên là mở cửa cho người sửa giá.
// ------------------------------------------------------------------------------

/// <summary>Một diện ưu đãi, hiện cho khách chọn hoặc để giải thích ưu đãi tự động.</summary>
public record WorkshopDiscountDto(
    Guid Id,
    string Code,
    string Name,
    string Description,

    /// <summary>0 = khách tự chọn cho từng chỗ; 1 = hệ thống tự áp cho cả lượt.</summary>
    int Scope,

    int Percent,
    int MinSeats,
    int MinDaysAhead,

    /// <summary>true thì giao diện phải nói rõ là cần trình thẻ khi tới quán.</summary>
    bool RequiresProof,
    string? ProofNote);

/// <summary>Một khung giờ trên lịch: đủ thông tin để khách chọn mà không mở thêm trang.</summary>
public record WorkshopSlotDto(
    Guid Id,
    string Topic,
    string Summary,

    /// <summary>Ngày diễn ra, "yyyy-MM-dd" theo giờ quán.</summary>
    string Date,

    /// <summary>Giờ bắt đầu, "HH:mm".</summary>
    string StartTime,

    /// <summary>Giờ kết thúc, "HH:mm".</summary>
    string EndTime,

    int Capacity,

    /// <summary>
    /// Số chỗ CÒN LẠI, tính lúc đọc. Có thể về 0 giữa lúc khách đang điền form —
    /// nên máy chủ vẫn kiểm tra lại một lần nữa lúc đặt.
    /// </summary>
    int SeatsLeft,

    /// <summary>Giá một chỗ, giá thường, đồng.</summary>
    int BasePrice,

    /// <summary>Giá một chỗ theo mức ưu đãi TỐT NHẤT — dùng cho dòng "chỉ từ ... đ".</summary>
    int BestPrice,

    /// <summary>
    /// Vì sao không đặt được. null = đặt được.
    /// Có chuỗi thì giao diện hiện thẳng câu này thay vì tự đoán lý do.
    /// </summary>
    string? UnavailableReason);

/// <summary>Các buổi của MỘT ngày, gom lại để lịch vẽ theo ô ngày.</summary>
public record WorkshopDayDto(
    /// <summary>"yyyy-MM-dd".</summary>
    string Date,

    IReadOnlyList<WorkshopSlotDto> Slots)
{
    /// <summary>Ngày này còn chỗ nào không — lịch tô màu ô ngày theo cờ này.</summary>
    public bool HasSeats => Slots.Any(s => s.UnavailableReason is null && s.SeatsLeft > 0);

    /// <summary>Giá thấp nhất trong ngày, để ô ngày hiện "từ ...đ".</summary>
    public int FromPrice => Slots.Count == 0 ? 0 : Slots.Min(s => s.BestPrice);
}

/// <summary>Toàn bộ dữ liệu trang đặt lịch cần cho một khoảng ngày.</summary>
public record WorkshopCalendarDto(
    /// <summary>Ngày đầu khoảng, "yyyy-MM-dd".</summary>
    string From,

    /// <summary>Ngày cuối khoảng, "yyyy-MM-dd".</summary>
    string To,

    /// <summary>Hôm nay theo giờ quán — để giao diện không tự tính từ đồng hồ máy khách.</summary>
    string Today,

    IReadOnlyList<WorkshopDayDto> Days,

    /// <summary>Ưu đãi theo từng chỗ, khách tự chọn.</summary>
    IReadOnlyList<WorkshopDiscountDto> SeatDiscounts,

    /// <summary>Ưu đãi cả lượt, hệ thống tự áp. Hiện ra để khách biết mà gom nhóm.</summary>
    IReadOnlyList<WorkshopDiscountDto> BookingDiscounts);

// ------------------------------------------------------------------------------
//  SỬA GIÁ VÀ SỨC CHỨA MỘT BUỔI — chỉ app quản lý dùng
// ------------------------------------------------------------------------------

/// <summary>
/// Yêu cầu sửa một buổi workshop từ trang quản lý.
/// <para>
/// KHÔNG đổi ngày giờ ở đây. Dời giờ một buổi đã có người đặt là việc phải báo
/// lại từng khách, không phải sửa một ô rồi lưu — nên giao diện chỉ cho hủy buổi
/// rồi mở buổi mới, để quán buộc phải nhìn thấy danh sách người cần gọi.
/// </para>
/// </summary>
public class UpdateWorkshopSessionRequest
{
    /// <summary>Giá một chỗ, giá thường, đồng. 0 = buổi miễn phí.</summary>
    public int BasePrice { get; set; }

    /// <summary>Số chỗ tối đa. Không hạ được xuống dưới số chỗ đã có người giữ.</summary>
    public int Capacity { get; set; }

    /// <summary>Đổi chủ đề. Bỏ trống = giữ nguyên.</summary>
    public string? Topic { get; set; }

    /// <summary>Đổi câu mô tả hiện cho khách. Bỏ trống = giữ nguyên.</summary>
    public string? Summary { get; set; }

    /// <summary>Ghi chú nội bộ, khách không thấy.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// true = áp cùng giá và sức chứa cho MỌI buổi tương lai trùng thứ và trùng
    /// giờ bắt đầu.
    /// <para>
    /// Lịch được sinh tự động tám tuần một lượt, nên một khung giờ có tới bốn
    /// mươi buổi. Bắt chủ quán sửa giá từng buổi một là biến một quyết định
    /// kinh doanh ("từ nay sáng thứ Bảy 420k") thành bốn mươi lần gõ phím, và
    /// chỉ cần bỏ sót một buổi là giá trên lịch mâu thuẫn nhau.
    /// </para>
    /// </summary>
    public bool ApplyToSameSlot { get; set; }
}

/// <summary>Kết quả sau khi lưu — đủ để giao diện cập nhật tại chỗ và báo rõ đã sửa mấy buổi.</summary>
public record WorkshopSessionSavedDto(
    Guid Id,
    int BasePrice,
    int Capacity,

    /// <summary>Số chỗ đang có người giữ ở buổi vừa sửa.</summary>
    int SeatsTaken,

    /// <summary>Tổng số buổi thực sự đã đổi, tính cả buổi đang mở.</summary>
    int UpdatedCount,

    /// <summary>
    /// Số buổi bị BỎ QUA khi áp hàng loạt vì sức chứa mới thấp hơn số chỗ đã
    /// bán ở buổi đó. Khác 0 thì giao diện phải nói ra, không được lặng lẽ.
    /// </summary>
    int SkippedCount,

    /// <summary>Câu tóm tắt tiếng Việt để hiện thẳng lên thông báo.</summary>
    string Message);

/// <summary>Một dòng khách chọn: mấy chỗ theo diện nào.</summary>
public class WorkshopBookingLineRequest
{
    /// <summary>Ưu đãi áp cho các chỗ này. Null = giá thường.</summary>
    public Guid? DiscountId { get; set; }

    public int Quantity { get; set; }
}

/// <summary>Yêu cầu đặt chỗ khách gửi lên.</summary>
public class CreateWorkshopBookingRequest
{
    public Guid SessionId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Note { get; set; }

    public List<WorkshopBookingLineRequest> Lines { get; set; } = new();
}

/// <summary>Một dòng trong lượt đặt, đã chốt giá.</summary>
public record WorkshopBookingLineDto(
    string TierName,
    int Percent,
    int Quantity,
    int UnitPrice,
    int LineTotal);

/// <summary>Kết quả đặt chỗ, cũng là dữ liệu trang tra cứu.</summary>
public record WorkshopBookingDto(
    Guid Id,
    string Code,
    string CustomerName,
    string Phone,
    string? Email,
    string? Note,

    /// <summary>0 chờ xác nhận, 1 đã xác nhận, 2 đã tới, 3 đã hủy, 4 không tới.</summary>
    int Status,
    string StatusLabel,

    Guid SessionId,
    string Topic,
    string Date,
    string StartTime,
    string EndTime,

    int Seats,
    IReadOnlyList<WorkshopBookingLineDto> Lines,
    int Subtotal,
    string? BookingDiscountName,
    int BookingDiscountAmount,
    int GrandTotal,

    /// <summary>Những gì khách cần mang theo — gom từ các diện ưu đãi cần trình thẻ.</summary>
    IReadOnlyList<string> ProofReminders,

    string? CancelReason);

/// <summary>Yêu cầu hủy chỗ. Cần cả mã lẫn số điện thoại đã dùng khi đặt.</summary>
public class CancelWorkshopBookingRequest
{
    public string Phone { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
