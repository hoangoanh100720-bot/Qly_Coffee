using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM KHO & NGUYÊN LIỆU
//
//  Đây là nhóm bảng quan trọng nhất của hệ thống. Ba khái niệm cần phân biệt rõ:
//
//    Ingredient    — ĐỊNH NGHĨA nguyên liệu (sữa tươi là gì, đơn vị gì, HSD bao lâu)
//    InventoryLot  — MỘT LÔ HÀNG cụ thể đã nhập (2 lít sữa mua ngày 08/08, hết hạn 15/08)
//    StockMovement — MỘT BÚT TOÁN trong sổ cái (đã xuất 200ml từ lô đó lúc 09:15)
//
//  Tồn kho thực tế = tổng RemainingQuantity của các lô đang Active.
//  Sổ cái là bằng chứng đối soát: cộng dồn QuantityDelta phải khớp với tồn kho.
// ==============================================================================

/// <summary>
/// Định nghĩa một loại nguyên liệu. Không chứa số lượng — số lượng nằm ở
/// <see cref="InventoryLot"/> vì mỗi lô có hạn dùng và giá vốn khác nhau.
/// </summary>
public class Ingredient : StoreScopedEntity
{
    /// <summary>Tên hiển thị tiếng Việt. VD: "Sữa tươi không đường".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã nội bộ, duy nhất trong toàn hệ thống. VD: "DAI-MILK-01".
    /// Quy ước: [NHÓM 3 KÝ TỰ]-[TÊN VIẾT TẮT]-[SỐ THỨ TỰ].
    /// Dùng để đối chiếu với chứng từ nhà cung cấp và tránh trùng tên.
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Nhóm phân loại — dùng để lọc và tô màu trong màn hình kho.</summary>
    public IngredientCategory Category { get; set; }

    /// <summary>
    /// Đơn vị cơ sở. MỌI số lượng liên quan tới nguyên liệu này đều theo đơn vị này:
    /// công thức định lượng, tồn kho, sổ cái, ngưỡng cảnh báo.
    /// </summary>
    public BaseUnit BaseUnit { get; set; }

    // --- Màu sắc & minh họa -----------------------------------------------------

    /// <summary>
    /// Màu THẬT của nguyên liệu, dạng hex (VD: "#F5EDE0" cho sữa tươi).
    /// <para>
    /// Đây không phải màu trang trí — nó là DỮ LIỆU. Giao diện dùng màu này để:
    /// (1) vẽ minh họa SVG của nguyên liệu, (2) tô chấm màu trong bảng kho,
    /// (3) dựng dải màu gợi ý cho ảnh món. Nhờ vậy bảng tồn kho đọc được bằng mắt
    /// mà không cần đọc chữ.
    /// </para>
    /// </summary>
    public string ColorHex { get; set; } = "#B8A38A";

    /// <summary>
    /// Mã hình minh họa SVG dựng sẵn ở frontend. VD: "milk", "coffee-beans", "boba".
    /// Không dùng file ảnh ngoài để giao diện luôn sắc nét và không phụ thuộc mạng.
    /// Danh sách mã hợp lệ nằm ở component <c>IngredientIcon.razor</c>.
    /// </summary>
    public string IconKey { get; set; } = "generic";

    // --- Ngưỡng cảnh báo --------------------------------------------------------

    /// <summary>
    /// Tồn kho tối thiểu, theo <see cref="BaseUnit"/>.
    /// Xuống dưới mức này thì hiện cảnh báo đỏ ở màn hình kho.
    /// </summary>
    public double MinStockLevel { get; set; }

    /// <summary>
    /// Điểm đặt hàng lại, theo <see cref="BaseUnit"/>.
    /// Xuống dưới mức này thì AI Engine sinh đề xuất <c>Restock</c>.
    /// Nên đặt = MinStockLevel + (tiêu thụ TB mỗi ngày × số ngày giao hàng của NCC).
    /// </summary>
    public double ReorderPoint { get; set; }

    // --- Hạn sử dụng ------------------------------------------------------------

    /// <summary>
    /// Số ngày sử dụng mặc định kể từ ngày nhập.
    /// Khi nhập kho mà không điền hạn cụ thể, hệ thống lấy ngày nhập + số ngày này.
    /// <c>null</c> = nguyên liệu không có hạn sử dụng (VD: đá viên, ly nhựa).
    /// </summary>
    public int? DefaultShelfLifeDays { get; set; }

    /// <summary>
    /// Bắt đầu coi là "cận hạn" khi còn bao nhiêu ngày.
    /// Nguyên liệu tươi (xoài, đào) nên để 2-3; nguyên liệu khô có thể để 14-30.
    /// </summary>
    public int ExpiryWarningDays { get; set; } = 7;

    // --- Chi phí ----------------------------------------------------------------

    /// <summary>
    /// Tỷ lệ hao hụt tự nhiên khi chế biến. VD: 0.03 nghĩa là hao 3%.
    /// <para>
    /// Lượng thực trừ = lượng công thức × (1 + WastageRate).
    /// Trái cây tươi phải gọt vỏ nên hao nhiều (0.10–0.15); bột và siro hao rất ít (0.01).
    /// </para>
    /// </summary>
    public double WastageRate { get; set; }

    /// <summary>
    /// Giá vốn bình quân gia quyền, tính bằng ĐỒNG cho 1 đơn vị cơ sở.
    /// <para>
    /// Công thức: Σ(RemainingQuantity × UnitCost) / Σ(RemainingQuantity) trên các lô Active.
    /// Được cập nhật tự động mỗi lần nhập kho. Dùng để tính giá vốn món ăn.
    /// </para>
    /// Kiểu <c>int</c> vì tiền Việt không có phần thập phân — dùng double sẽ sai số tích lũy.
    /// </summary>
    public int AverageUnitCost { get; set; }

    /// <summary>Đang sử dụng hay đã ngừng dùng.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// BÁN THÀNH PHẨM — không mua ngoài mà do quán tự nấu/ủ từ nguyên liệu thô.
    /// VD: cốt hồng trà, cà phê phin cốt, nước đường, kem muối.
    /// <para>
    /// Nguyên liệu như vậy chỉ vào kho qua màn hình <c>Sơ chế</c>, và mỗi mẻ là
    /// một lô riêng có hạn dùng tính bằng GIỜ (xem <see cref="PrepRecipe"/>).
    /// </para>
    /// <para>
    /// Hết bán thành phẩm thì món dùng nó phải báo "chưa sơ chế" chứ không phải
    /// "hết hàng" — hai câu đó dẫn nhân viên tới hai hành động khác hẳn nhau:
    /// một bên là đi ủ mẻ mới, một bên là gọi nhà cung cấp.
    /// </para>
    /// </summary>
    public bool IsPrepared { get; set; }

    /// <summary>Ghi chú vận hành. VD: "Bảo quản ngăn mát 2-6°C".</summary>
    public string? Note { get; set; }

    // --- Quan hệ ----------------------------------------------------------------

    public Store? Store { get; set; }
    public ICollection<InventoryLot> Lots { get; set; } = new List<InventoryLot>();
    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
    public ICollection<RecipeItem> RecipeItems { get; set; } = new List<RecipeItem>();
    public ICollection<PurchaseUnit> PurchaseUnits { get; set; } = new List<PurchaseUnit>();
}

/// <summary>
/// Đơn vị mua hàng và hệ số quy đổi sang đơn vị cơ sở.
/// <para>
/// Ví dụ: sữa tươi có <see cref="BaseUnit"/> là Milliliter, nhưng mua theo thùng.
/// Tạo một PurchaseUnit tên "Thùng 12 hộp 1L" với ConversionQuantity = 12000.
/// Người nhập kho chọn "3 thùng" → hệ thống lưu 36000 ml.
/// </para>
/// </summary>
public class PurchaseUnit : BaseEntity
{
    public Guid IngredientId { get; set; }

    /// <summary>Tên đơn vị hiển thị khi nhập kho. VD: "Thùng 12 hộp 1L", "Bao 5kg".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Số đơn vị cơ sở trong MỘT đơn vị mua này.
    /// Thùng 12 hộp × 1000ml → 12000. Bao 5kg → 5000 (gram).
    /// </summary>
    public double ConversionQuantity { get; set; }

    /// <summary>Đơn vị mặc định hiện sẵn trên form nhập kho.</summary>
    public bool IsDefault { get; set; }

    public Ingredient? Ingredient { get; set; }
}

/// <summary>
/// Một lô hàng cụ thể đã nhập kho. Mỗi lần nhập tạo một lô mới.
/// <para>
/// LÝ DO PHẢI TÁCH LÔ: hai lần nhập sữa có hạn dùng và giá khác nhau. Nếu gộp
/// chung một con số tồn kho thì không thể biết bao nhiêu sắp hết hạn, và không
/// thể áp dụng nguyên tắc FEFO — vốn là nền tảng của tính năng cảnh báo cận hạn.
/// </para>
/// </summary>
public class InventoryLot : StoreScopedEntity
{
    public Guid IngredientId { get; set; }

    /// <summary>
    /// Mã lô, duy nhất trong chi nhánh. VD: "DAI-MILK-01-260810-A".
    /// Tự sinh theo quy tắc [SKU]-[YYMMDD]-[chữ cái] nếu người dùng không nhập.
    /// </summary>
    public string LotCode { get; set; } = string.Empty;

    /// <summary>Số lượng nhập ban đầu, theo đơn vị cơ sở của nguyên liệu.</summary>
    public double ReceivedQuantity { get; set; }

    /// <summary>
    /// Số lượng còn lại, theo đơn vị cơ sở.
    /// <para>
    /// CHỈ được thay đổi bên trong transaction có khóa hàng (SELECT ... FOR UPDATE).
    /// Sửa trực tiếp mà không khóa sẽ gây tồn kho âm khi có hai đơn cùng lúc.
    /// </para>
    /// </summary>
    public double RemainingQuantity { get; set; }

    /// <summary>
    /// Giá vốn của lô này, ĐỒNG cho 1 đơn vị cơ sở, tại thời điểm nhập.
    /// Không đổi theo thời gian — đây là giá lịch sử, dùng để tính giá vốn hàng bán chính xác.
    /// </summary>
    public int UnitCost { get; set; }

    /// <summary>Thời điểm nhập kho thực tế.</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Hạn sử dụng. <c>null</c> = không có hạn.
    /// <para>
    /// Đây là trường quan trọng nhất của bảng: nó quyết định thứ tự FEFO và là
    /// đầu vào của toàn bộ thuật toán cảnh báo lãng phí.
    /// </para>
    /// </summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Trạng thái lô. Chỉ lô <c>Active</c> mới được lấy ra dùng.</summary>
    public LotStatus Status { get; set; } = LotStatus.Active;

    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderItemId { get; set; }

    /// <summary>
    /// Công thức sơ chế đã tạo ra lô này. <c>null</c> = lô mua từ nhà cung cấp.
    /// <para>
    /// Có cột này thì truy được ngược: mẻ cốt trà hôm nay đắng bất thường là do
    /// công thức nào, ai làm, lúc mấy giờ, ăn hết bao nhiêu lá trà của lô nào.
    /// </para>
    /// </summary>
    public Guid? PrepRecipeId { get; set; }

    /// <summary>Ghi chú riêng của lô. VD: "Hàng khuyến mãi, cận hạn".</summary>
    public string? Note { get; set; }

    // --- Thuộc tính tính toán (không lưu DB) ------------------------------------

    /// <summary>Số ngày còn lại tới hạn. Âm nghĩa là đã quá hạn.</summary>
    public int? DaysUntilExpiry =>
        ExpiryDate.HasValue
            ? (int)(ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays
            : null;

    /// <summary>Giá trị tồn còn lại của lô, tính bằng đồng.</summary>
    public int RemainingValue => (int)Math.Round(RemainingQuantity * UnitCost);

    public Ingredient? Ingredient { get; set; }
    public Supplier? Supplier { get; set; }
    public PrepRecipe? PrepRecipe { get; set; }
    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
}

/// <summary>
/// SỔ CÁI KHO — bảng chỉ ghi thêm, không bao giờ sửa hay xóa.
/// <para>
/// Mỗi lần kho thay đổi đều sinh một bút toán ở đây. Đây là nguồn chân lý duy nhất
/// để đối soát khi tồn kho lệch. Nếu ghi sai thì ghi thêm bút toán đảo, KHÔNG sửa
/// bút toán cũ — giống hệt nguyên tắc kế toán kép.
/// </para>
/// </summary>
public class StockMovement : BaseEntity
{
    public Guid StoreId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>
    /// Lô bị tác động. <c>null</c> chỉ trong trường hợp điều chỉnh kiểm kê
    /// mà chưa xác định được lô cụ thể.
    /// </summary>
    public Guid? LotId { get; set; }

    /// <summary>Loại bút toán — quyết định dấu của QuantityDelta.</summary>
    public MovementType Type { get; set; }

    /// <summary>
    /// Lượng thay đổi, theo đơn vị cơ sở.
    /// <para>DƯƠNG = nhập kho. ÂM = xuất kho. Không bao giờ bằng 0.</para>
    /// </summary>
    public double QuantityDelta { get; set; }

    /// <summary>Giá vốn 1 đơn vị tại thời điểm phát sinh bút toán (đồng).</summary>
    public int UnitCost { get; set; }

    /// <summary>Tổng giá trị bút toán = |QuantityDelta| × UnitCost (đồng).</summary>
    public int TotalCost { get; set; }

    // --- Truy vết nguồn gốc -----------------------------------------------------

    /// <summary>Loại chứng từ nguồn: "ORDER" | "PURCHASE" | "PREP" | "COUNT" | "WASTE" | "EXPIRY".</summary>
    public string? ReferenceType { get; set; }

    /// <summary>Id của chứng từ nguồn (đơn hàng, phiếu nhập, phiếu kiểm kê...).</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>Lý do — BẮT BUỘC với bút toán hao hụt và điều chỉnh.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Khóa chống ghi trùng. Rất quan trọng: khi mạng chập chờn và client gửi lại
    /// cùng một yêu cầu, khóa này đảm bảo kho chỉ bị trừ một lần.
    /// <para>Quy ước đặt tên: "ORDER:{orderId}:ING:{ingredientId}:LOT:{lotId}".</para>
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Người thực hiện. <c>null</c> nếu do job tự động sinh.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Thời điểm nghiệp vụ thực sự xảy ra (có thể khác lúc ghi vào DB).</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public Ingredient? Ingredient { get; set; }
    public InventoryLot? Lot { get; set; }
    public User? Actor { get; set; }
}

/// <summary>Nhà cung cấp nguyên liệu.</summary>
public class Supplier : StoreScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary>
    /// Số ngày từ lúc đặt tới lúc nhận hàng.
    /// Dùng để tính điểm đặt hàng lại và cảnh báo "phải đặt hôm nay kẻo hết".
    /// </summary>
    public int LeadTimeDays { get; set; } = 1;

    /// <summary>Giá trị đơn tối thiểu nhà cung cấp chấp nhận (đồng).</summary>
    public int MinOrderValue { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

/// <summary>Phiếu đặt mua hàng gửi nhà cung cấp.</summary>
public class PurchaseOrder : StoreScopedEntity
{
    public Guid SupplierId { get; set; }

    /// <summary>Mã phiếu. VD: "PO-260810-001".</summary>
    public string Code { get; set; } = string.Empty;

    public DateTime? OrderedAt { get; set; }
    public DateTime? ExpectedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    /// <summary>Tổng giá trị phiếu (đồng).</summary>
    public int TotalCost { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// <c>true</c> nếu phiếu do AI Engine tự sinh từ đề xuất <c>Restock</c>.
    /// Dùng để đo hiệu quả của tính năng gợi ý nhập hàng.
    /// </summary>
    public bool IsAiGenerated { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

/// <summary>Một dòng trong phiếu đặt mua.</summary>
public class PurchaseOrderItem : BaseEntity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Số lượng đặt, theo đơn vị cơ sở.</summary>
    public double Quantity { get; set; }

    /// <summary>Đơn giá thỏa thuận (đồng / đơn vị cơ sở).</summary>
    public int UnitCost { get; set; }

    /// <summary>Số lượng đã nhận thực tế. Dùng để phát hiện giao thiếu.</summary>
    public double ReceivedQuantity { get; set; }

    /// <summary>Hạn sử dụng ghi trên hàng nhận được.</summary>
    public DateTime? ExpiryDate { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public Ingredient? Ingredient { get; set; }
}

/// <summary>
/// Phiếu kiểm kê — so sánh số hệ thống với số đếm thực tế.
/// Chênh lệch sinh ra bút toán <c>AdjustIn</c> hoặc <c>AdjustOut</c>.
/// </summary>
public class StockCount : StoreScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public DateTime CountedAt { get; set; } = DateTime.UtcNow;
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Đã chốt sổ chưa. Phiếu chưa chốt có thể sửa; đã chốt thì khóa vĩnh viễn
    /// và đã sinh bút toán điều chỉnh.
    /// </summary>
    public bool IsFinalized { get; set; }

    public User? Actor { get; set; }
    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

/// <summary>Một dòng kiểm kê cho một nguyên liệu.</summary>
public class StockCountLine : BaseEntity
{
    public Guid StockCountId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Số lượng hệ thống đang ghi nhận tại thời điểm kiểm kê.</summary>
    public double SystemQuantity { get; set; }

    /// <summary>Số lượng đếm thực tế.</summary>
    public double CountedQuantity { get; set; }

    /// <summary>Chênh lệch = CountedQuantity − SystemQuantity. Âm là thiếu hụt.</summary>
    public double VarianceQuantity { get; set; }

    /// <summary>Giá trị chênh lệch (đồng). Âm là tổn thất.</summary>
    public int VarianceCost { get; set; }

    public string? Note { get; set; }

    public StockCount? StockCount { get; set; }
    public Ingredient? Ingredient { get; set; }
}
