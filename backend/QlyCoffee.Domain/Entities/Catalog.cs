using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM DANH MỤC — CỬA HÀNG, MÓN, CÔNG THỨC ĐỊNH LƯỢNG
//
//  Chuỗi quan hệ cần nắm:
//
//    Category ──< Product ──< ProductVariant        (size M / L)
//                    │
//                    ├──< RecipeItem ──> Ingredient  ⭐ CÔNG THỨC ĐỊNH LƯỢNG
//                    │
//                    └──< ProductModifier ──> ModifierGroup ──< Modifier
//                                                                   │
//                                                    ModifierRecipeItem ──> Ingredient
//
//  Khi khách đặt một ly, hệ thống cộng gộp:
//    (công thức gốc × hệ số size) + (công thức của từng topping đã chọn)
//  rồi nhân hao hụt và số ly, ra được danh sách nguyên liệu cần trừ kho.
// ==============================================================================

/// <summary>
/// Thông tin chi nhánh. MVP chỉ có một bản ghi, nhưng thiết kế sẵn cho nhiều chi nhánh.
/// </summary>
public class Store : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Đường dẫn thân thiện, dùng cho URL. VD: "qly-coffee".</summary>
    public string Slug { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>Giờ mở cửa, dạng "HH:mm". Hiển thị trên trang chủ và dùng để chặn đặt món ngoài giờ.</summary>
    public string OpenTime { get; set; } = "07:00";

    /// <summary>Giờ đóng cửa, dạng "HH:mm".</summary>
    public string CloseTime { get; set; } = "22:00";

    /// <summary>Múi giờ IANA. Mọi phép tính "theo ngày kinh doanh" phải dùng múi giờ này.</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// Giờ kết thúc ngày kinh doanh (0-23). Job sinh kế hoạch chạy sau mốc này.
    /// Quán mở tới 22h nhưng chốt sổ lúc 23h để kịp gom hết đơn cuối ngày.
    /// </summary>
    public int BusinessDayEndHour { get; set; } = 23;

    /// <summary>Đang nhận đơn hay tạm nghỉ. Tắt thì trang bán hàng hiện thông báo.</summary>
    public bool IsOpen { get; set; } = true;

    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

/// <summary>Danh mục món trên menu. VD: "Cà phê", "Trà sữa", "Đá xay".</summary>
public class Category : StoreScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Mô tả ngắn hiển thị dưới tên danh mục ở trang menu.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Màu đại diện của danh mục, dạng hex. Lấy từ màu đặc trưng của nhóm đồ uống:
    /// cà phê nâu đậm, trà sữa nâu sữa, matcha xanh lá, trà trái cây cam.
    /// Dùng làm nền chip lọc và viền thẻ món.
    /// </summary>
    public string ColorHex { get; set; } = "#8B5A2B";

    /// <summary>Mã icon SVG dựng sẵn ở frontend. VD: "cup-hot", "bubble-tea", "leaf".</summary>
    public string IconKey { get; set; } = "cup";

    /// <summary>Thứ tự hiển thị. Số nhỏ lên trước.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public Store? Store { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>
/// Một món trên menu.
/// </summary>
public class Product : StoreScopedEntity
{
    public Guid CategoryId { get; set; }

    /// <summary>Tên món tiếng Việt. VD: "Cà phê muối".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Đường dẫn thân thiện, duy nhất. VD: "ca-phe-muoi".</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Mô tả bán hàng, hiển thị ở trang chi tiết món.</summary>
    public string? Description { get; set; }

    /// <summary>Đường dẫn ảnh món. Nếu trống, giao diện tự dựng minh họa từ dải màu bên dưới.</summary>
    public string? ImageUrl { get; set; }

    // --- Màu sắc thật của đồ uống ----------------------------------------------

    /// <summary>
    /// Màu chủ đạo THẬT của ly nước, dạng hex. VD cà phê muối: "#4A2C17".
    /// Dùng để dựng minh họa ly nước bằng SVG khi chưa có ảnh chụp, và để tô nền
    /// thẻ món. Đây là dữ liệu, không phải trang trí.
    /// </summary>
    public string ColorPrimaryHex { get; set; } = "#4A2C17";

    /// <summary>
    /// Màu lớp trên hoặc lớp kem, dạng hex. VD cà phê muối có lớp kem muối: "#F7F2E8".
    /// Kết hợp với ColorPrimaryHex tạo ra hình minh họa ly nước hai lớp giống thật.
    /// </summary>
    public string ColorAccentHex { get; set; } = "#C89968";

    // --- Giá & chi phí ----------------------------------------------------------

    /// <summary>Giá bán của size mặc định, tính bằng ĐỒNG (int, không thập phân).</summary>
    public int BasePrice { get; set; }

    /// <summary>
    /// Giá vốn TÍNH TOÁN từ công thức, bằng đồng.
    /// <para>
    /// Công thức: Σ(RecipeItem.Quantity × Ingredient.AverageUnitCost × (1 + WastageRate)).
    /// Được tính lại mỗi khi sửa công thức hoặc mỗi khi nhập hàng làm đổi giá vốn nguyên liệu.
    /// KHÔNG nhập tay — luôn là số dẫn xuất.
    /// </para>
    /// </summary>
    public int ComputedCost { get; set; }

    /// <summary>Biên lợi nhuận phần trăm — thuộc tính tính toán, không lưu DB.</summary>
    public int MarginPercent =>
        BasePrice > 0 ? (int)Math.Round((BasePrice - ComputedCost) * 100.0 / BasePrice) : 0;

    // --- Trạng thái hiển thị ----------------------------------------------------

    public bool IsActive { get; set; } = true;

    /// <summary>Có hiện ở khu vực "Món nổi bật" trang chủ không.</summary>
    public bool IsFeatured { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Còn bán được hay không — do <c>AvailabilityService</c> tính lại sau mỗi
    /// thay đổi kho. Frontend đọc trường này để hiện nhãn "Tạm hết".
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Lý do hết hàng, hiển thị cho nhân viên. VD: "Hết Sữa tươi không đường".</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>
    /// Số ly tối đa còn làm được với tồn kho hiện tại.
    /// <para>
    /// Tính bằng: min( tồn kho nguyên liệu / lượng cần cho 1 ly ) trên mọi nguyên liệu bắt buộc.
    /// Giao diện dùng để chặn khách tăng số lượng vượt quá và hiện "Chỉ còn N ly".
    /// </para>
    /// </summary>
    public int MaxServings { get; set; } = 9999;

    /// <summary>
    /// Nhãn hiển thị trên thẻ món, ngăn cách bởi dấu phẩy.
    /// VD: "best-seller,mới,ít đường". Dùng để lọc và tô badge.
    /// </summary>
    public string Tags { get; set; } = string.Empty;

    // --- Quan hệ ----------------------------------------------------------------

    public Category? Category { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<RecipeItem> RecipeItems { get; set; } = new List<RecipeItem>();
    public ICollection<ProductModifier> ModifierLinks { get; set; } = new List<ProductModifier>();
}

/// <summary>
/// Biến thể của món — chủ yếu là size.
/// </summary>
public class ProductVariant : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>Tên biến thể. VD: "Size M", "Size L", "Nóng".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Chênh lệch giá so với BasePrice, tính bằng đồng. Có thể âm.</summary>
    public int PriceDelta { get; set; }

    /// <summary>
    /// Hệ số nhân công thức. Size L = 1.4 nghĩa là mọi nguyên liệu trong công thức
    /// gốc đều nhân 1.4.
    /// <para>
    /// LƯU Ý QUAN TRỌNG: hệ số này CHỈ áp dụng cho công thức gốc của món,
    /// KHÔNG áp dụng cho topping. Thêm trân châu vào size L vẫn là 40g trân châu,
    /// không phải 56g. Đây là lỗi rất dễ mắc khi cài đặt.
    /// </para>
    /// </summary>
    public double RecipeMultiplier { get; set; } = 1.0;

    /// <summary>Biến thể được chọn sẵn khi khách mở trang món.</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Product? Product { get; set; }
}

/// <summary>
/// ⭐ CÔNG THỨC ĐỊNH LƯỢNG — một dòng nguyên liệu trong công thức của món.
/// <para>
/// Đây là bảng nối giữa MENU và KHO. Không có bảng này thì không thể tự động
/// trừ tồn kho, không tính được giá vốn, và không biết món nào còn làm được.
/// </para>
/// </summary>
public class RecipeItem : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>
    /// Lượng nguyên liệu cần cho MỘT ly ở SIZE MẶC ĐỊNH,
    /// tính theo <see cref="Ingredient.BaseUnit"/> của nguyên liệu đó.
    /// <para>VD: cà phê muối cần 20 (gram cà phê), 45 (ml kem muối), 180 (gram đá).</para>
    /// </summary>
    public double Quantity { get; set; }

    /// <summary>
    /// Nguyên liệu tùy chọn — khách có thể yêu cầu bỏ (VD: đá, đường).
    /// Nguyên liệu tùy chọn KHÔNG được tính vào phép kiểm tra "còn làm được bao nhiêu ly".
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>Hướng dẫn pha chế cho dòng này. VD: "Đánh bông trước khi rót lên mặt".</summary>
    public string? Note { get; set; }

    /// <summary>Thứ tự các bước trong công thức, hiển thị cho barista.</summary>
    public int SortOrder { get; set; }

    public Product? Product { get; set; }
    public Ingredient? Ingredient { get; set; }
}

/// <summary>
/// Nhóm tùy chọn khi đặt món. VD: "Topping", "Mức đường", "Mức đá".
/// </summary>
public class ModifierGroup : StoreScopedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Số lựa chọn tối thiểu. 1 nghĩa là bắt buộc chọn.</summary>
    public int MinSelect { get; set; }

    /// <summary>Số lựa chọn tối đa. Topping thường cho chọn tới 3.</summary>
    public int MaxSelect { get; set; } = 1;

    /// <summary>Bắt buộc chọn thì không cho thêm vào giỏ nếu chưa chọn.</summary>
    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public ICollection<Modifier> Modifiers { get; set; } = new List<Modifier>();
    public ICollection<ProductModifier> ProductLinks { get; set; } = new List<ProductModifier>();
}

/// <summary>Một lựa chọn cụ thể trong nhóm tùy chọn. VD: "Trân châu đen".</summary>
public class Modifier : BaseEntity
{
    public Guid ModifierGroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Phụ thu, tính bằng đồng. 0 nếu miễn phí (như mức đường).</summary>
    public int PriceDelta { get; set; }

    /// <summary>Màu hex của topping — dùng để vẽ chấm màu bên cạnh tên khi chọn.</summary>
    public string ColorHex { get; set; } = "#B8A38A";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ModifierGroup? Group { get; set; }
    public ICollection<ModifierRecipeItem> RecipeItems { get; set; } = new List<ModifierRecipeItem>();
}

/// <summary>
/// Công thức riêng của một topping.
/// <para>VD: chọn "Trân châu đen" thì trừ thêm 40g trân châu khỏi kho.</para>
/// <para>
/// Các lựa chọn không tiêu tốn nguyên liệu (mức đường, mức đá) thì không có dòng nào ở đây.
/// </para>
/// </summary>
public class ModifierRecipeItem : BaseEntity
{
    public Guid ModifierId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Lượng nguyên liệu, theo đơn vị cơ sở. KHÔNG nhân hệ số size.</summary>
    public double Quantity { get; set; }

    public Modifier? Modifier { get; set; }
    public Ingredient? Ingredient { get; set; }
}

/// <summary>Bảng nối nhiều-nhiều: món nào có những nhóm tùy chọn nào.</summary>
public class ProductModifier
{
    public Guid ProductId { get; set; }
    public Guid ModifierGroupId { get; set; }
    public int SortOrder { get; set; }

    public Product? Product { get; set; }
    public ModifierGroup? Group { get; set; }
}
