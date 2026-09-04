namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM SƠ CHẾ — CẦU NỐI GIỮA NGUYÊN LIỆU THÔ VÀ NGUYÊN LIỆU PHA CHẾ
//
//  VẤN ĐỀ NÓ GIẢI QUYẾT
//
//  Trước đây công thức một ly trà sữa ghi "8g hồng trà". Điều đó ngầm nói rằng
//  nhân viên cân 8g lá trà cho TỪNG LY. Ngoài đời không ai làm vậy: người ta ủ
//  một bình 2 lít cốt trà rồi rót dần cho ba mươi ly, và bình đó hỏng sau vài
//  tiếng dù còn nguyên.
//
//  Hệ quả của việc mô hình sai:
//    · Kho báo còn 500g hồng trà = còn 62 ly, trong khi bình cốt đã cạn từ lâu
//      và không ai pha được ly nào.
//    · Lượng cốt đổ đi cuối ca không nằm ở đâu trong sổ sách.
//    · Không trả lời được câu "hôm nay ủ mấy bình trà" — câu mà chủ quán hỏi
//      mỗi ngày.
//
//  CÁCH LÀM
//
//  Tách làm hai bước, đúng như thao tác thật ở quầy:
//
//    Bước 1 — SƠ CHẾ (PrepRecipe)   nhân viên bấm "Ủ hồng trà"
//             80g lá hồng trà  ──▶  2000ml Cốt hồng trà, hạn 6 tiếng
//             Kho trừ LÁ TRÀ ngay tại đây (bút toán ProductionOut).
//
//    Bước 2 — BÁN HÀNG (RecipeItem)  khách gọi, nhân viên bấm "Hoàn tất"
//             200ml Cốt hồng trà  ──▶  một ly trà sữa
//             Kho trừ CỐT TRÀ (bút toán SaleOut, FEFO như mọi nguyên liệu khác).
//
//  Bán thành phẩm (cốt trà, cà phê phin, nước đường) là một Ingredient bình
//  thường, chỉ khác ở cờ IsPrepared. Nhờ vậy TOÀN BỘ máy móc sẵn có — FEFO,
//  sổ cái, cảnh báo hạn dùng, tính giá vốn, "còn làm được bao nhiêu ly" — áp
//  dụng được ngay mà không phải viết thêm đường đi riêng nào.
// ==============================================================================

/// <summary>
/// Công thức của MỘT MẺ sơ chế: nấu/ủ nguyên liệu thô thành bán thành phẩm.
/// <para>
/// Khác <see cref="RecipeItem"/> ở chỗ: công thức món tính cho MỘT LY, còn công
/// thức sơ chế tính cho MỘT MẺ. Nhân viên làm nửa mẻ hay hai mẻ thì nhân hệ số,
/// không sửa công thức.
/// </para>
/// </summary>
public class PrepRecipe : StoreScopedEntity
{
    /// <summary>
    /// Mã công thức, duy nhất trong chi nhánh. VD: "PREP-TEA-BLK".
    /// Là khóa định danh mà <c>MenuSync</c> dùng để biết công thức đã có hay chưa.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên thao tác, viết theo cách nhân viên gọi. VD: "Ủ hồng trà".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Bán thành phẩm sinh ra. Nguyên liệu này phải có <c>IsPrepared = true</c>.</summary>
    public Guid OutputIngredientId { get; set; }

    /// <summary>
    /// Sản lượng của MỘT MẺ CHUẨN, theo đơn vị cơ sở của bán thành phẩm.
    /// <para>
    /// Đây là sản lượng SAU HAO HỤT — tức lượng thật sự rót vào bình, không phải
    /// lượng nước đổ vào nồi. Lá trà ngậm nước, cà phê giữ lại trong phin: chênh
    /// lệch đó phải nằm sẵn trong con số này, nếu không kho sẽ luôn dư ảo.
    /// </para>
    /// </summary>
    public double OutputQuantity { get; set; }

    /// <summary>
    /// Hạn dùng của mẻ, tính bằng GIỜ kể từ lúc làm xong.
    /// <para>
    /// Phải tính bằng giờ chứ không phải ngày — cốt trà hỏng sau 6 tiếng, mà
    /// <see cref="Ingredient.DefaultShelfLifeDays"/> nhỏ nhất chỉ được 1 ngày.
    /// Ghi 1 ngày cho cốt trà nghĩa là cho phép bán trà ủ từ sáng vào lúc tối.
    /// </para>
    /// </summary>
    public int ShelfLifeHours { get; set; } = 24;

    /// <summary>Thời gian làm xong một mẻ, tính bằng phút. Dùng để nhắc ca sau.</summary>
    public int PrepMinutes { get; set; } = 15;

    /// <summary>
    /// Hướng dẫn thao tác, hiện nguyên văn trên màn hình sơ chế.
    /// Nhiệt độ nước và thời gian ủ nằm ở đây — sai một trong hai thì mẻ trà chát.
    /// </summary>
    public string? Instructions { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Store? Store { get; set; }
    public Ingredient? OutputIngredient { get; set; }
    public ICollection<PrepRecipeLine> Lines { get; set; } = new List<PrepRecipeLine>();
}

/// <summary>Một dòng nguyên liệu thô trong công thức sơ chế.</summary>
public class PrepRecipeLine : BaseEntity
{
    public Guid PrepRecipeId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>
    /// Lượng cần cho MỘT MẺ CHUẨN, theo đơn vị cơ sở của nguyên liệu thô.
    /// Làm hai mẻ thì hệ thống nhân đôi, không sửa số này.
    /// </summary>
    public double Quantity { get; set; }

    /// <summary>Thao tác của riêng dòng này. VD: "Tráng qua nước sôi rồi mới hãm".</summary>
    public string? Note { get; set; }

    public int SortOrder { get; set; }

    public PrepRecipe? PrepRecipe { get; set; }
    public Ingredient? Ingredient { get; set; }
}
