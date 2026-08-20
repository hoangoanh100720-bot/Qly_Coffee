using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM KHUYẾN MÃI & KẾ HOẠCH AI
//
//  Luồng hoạt động của tính năng lõi:
//
//   23:00 mỗi ngày
//        │
//        ├─ 1. Tổng hợp tiêu thụ trong ngày  → DailyConsumption, DailySales
//        │
//        ├─ 2. Quét lô cận hạn, dự báo EWMA  → tính QuantityAtRisk, ValueAtRisk
//        │       (CODE tính, không phải AI)
//        │
//        ├─ 3. Sinh phương án khuyến mãi     → tính NetBenefit từng mức giảm
//        │       (CODE tính, không phải AI)
//        │
//        ├─ 4. Gọi Claude viết diễn giải     → Headline, Summary, Reasoning, BannerCopy
//        │       (AI chỉ viết chữ, KHÔNG tính số)
//        │
//        └─ 5. Lưu DailyPlan + PlanSuggestion → gửi trang quản lý + email
//                     │
//                     └─ Quản lý duyệt → PlanDecision → tạo Promotion thật
//
//  RANH GIỚI QUAN TRỌNG NHẤT: mọi trường số trong PlanSuggestion đều do backend
//  tính. AI chỉ điền các trường chữ. Nếu để AI tính tiền, báo cáo sẽ sai mà
//  không ai phát hiện được.
// ==============================================================================

/// <summary>
/// Chương trình khuyến mãi đang hoặc sẽ chạy.
/// </summary>
public class Promotion : StoreScopedEntity
{
    /// <summary>Tên chương trình, hiển thị nội bộ. VD: "Xả đào ngâm cận hạn".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mô tả nội bộ — thường là lý do tạo, sao chép từ đề xuất của AI.</summary>
    public string? Description { get; set; }

    public PromotionType Type { get; set; }

    /// <summary>
    /// Giá trị giảm. Ý nghĩa phụ thuộc <see cref="Type"/>:
    /// PercentOff → số phần trăm (20 = giảm 20%);
    /// AmountOff → số đồng (10000 = giảm 10.000đ).
    /// </summary>
    public int Value { get; set; }

    public PromotionStatus Status { get; set; } = PromotionStatus.Draft;

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Giới hạn số lần sử dụng. <c>null</c> = không giới hạn.</summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>Đã dùng bao nhiêu lần.</summary>
    public int RedemptionCount { get; set; }

    /// <summary>
    /// Áp dụng cho món nào. <c>null</c> = áp dụng toàn menu.
    /// Khuyến mãi do AI sinh luôn gắn với một món cụ thể (món "chở" nguyên liệu cận hạn).
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Dòng chữ hiển thị trên banner trang bán hàng.
    /// Do AI viết, tối đa 80 ký tự, viết cho khách trẻ.
    /// VD: "Trà đào cam sả −20% — chỉ 2 ngày ☀️".
    /// </summary>
    public string? BannerText { get; set; }

    // --- Truy vết nguồn gốc -----------------------------------------------------

    /// <summary>Bản kế hoạch nào đã sinh ra khuyến mãi này. Dùng để đo hiệu quả của AI.</summary>
    public Guid? SourcePlanId { get; set; }

    /// <summary>Đề xuất cụ thể nào đã được duyệt thành khuyến mãi này.</summary>
    public Guid? SourceSuggestionId { get; set; }

    public Product? Product { get; set; }
}

/// <summary>
/// ⭐ BẢN KẾ HOẠCH HẰNG NGÀY — sản phẩm đầu ra của AI Engine.
/// <para>
/// Mỗi ngày kinh doanh có đúng một bản. Job chạy lại cùng ngày sẽ bỏ qua,
/// không tạo bản trùng (ràng buộc duy nhất trên StoreId + BusinessDate).
/// </para>
/// </summary>
public class DailyPlan : StoreScopedEntity
{
    /// <summary>
    /// Ngày kinh doanh, định dạng "yyyy-MM-dd" theo giờ Việt Nam.
    /// <para>
    /// Lưu dạng chuỗi thay vì DateTime để tránh hoàn toàn lỗi lệch múi giờ —
    /// đây là lỗi phổ biến nhất khi làm báo cáo theo ngày cho thị trường VN.
    /// </para>
    /// </summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Thời điểm job sinh ra bản kế hoạch này.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // --- PHẦN SỐ LIỆU: do backend tính, AI không được sửa -----------------------

    /// <summary>
    /// Tổng giá trị nguyên liệu có nguy cơ phải bỏ đi, tính bằng đồng.
    /// Đây là con số quan trọng nhất của bản kế hoạch — hiển thị to nhất trên UI.
    /// </summary>
    public int TotalValueAtRisk { get; set; }

    /// <summary>Số lô ở mức khẩn cấp (hết hạn trong ≤ 2 ngày hoặc giá trị rất lớn).</summary>
    public int CriticalCount { get; set; }

    /// <summary>Số lô ở mức cảnh báo (hết hạn trong 3–7 ngày).</summary>
    public int WarningCount { get; set; }

    /// <summary>Số nguyên liệu đang dưới ngưỡng tồn tối thiểu.</summary>
    public int LowStockCount { get; set; }

    /// <summary>Doanh thu ngày kinh doanh này (đồng) — để AI có ngữ cảnh khi viết.</summary>
    public int Revenue { get; set; }

    /// <summary>Số đơn hoàn thành trong ngày.</summary>
    public int OrderCount { get; set; }

    /// <summary>Giá trị hao hụt đã ghi nhận trong ngày (đồng).</summary>
    public int WasteValue { get; set; }

    /// <summary>
    /// Toàn bộ kết quả phân tích rủi ro, lưu dạng JSON.
    /// Cấu trúc khớp với <c>List&lt;LotRiskDto&gt;</c>.
    /// Dùng để vẽ biểu đồ chi tiết mà không phải tính lại.
    /// </summary>
    public string RiskAnalysisJson { get; set; } = "[]";

    // --- PHẦN DIỄN GIẢI: do Claude sinh -----------------------------------------

    /// <summary>
    /// Câu tiêu đề nêu điều quan trọng nhất cần xử lý.
    /// VD: "3 nguyên liệu cần xả trong 48 giờ".
    /// </summary>
    public string? Headline { get; set; }

    /// <summary>Đoạn tóm tắt tình hình 3–5 câu, giọng như quản lý ca báo cáo.</summary>
    public string? Summary { get; set; }

    /// <summary>Model đã dùng. VD: "claude-opus-5". Lưu để đối chiếu khi chất lượng thay đổi.</summary>
    public string? AiModel { get; set; }

    /// <summary>Số token đã dùng — để theo dõi chi phí.</summary>
    public int AiTokensUsed { get; set; }

    /// <summary>
    /// Lỗi khi gọi AI, nếu có. Khi có lỗi, hệ thống dùng bản diễn giải mẫu cố định
    /// nhưng VẪN tạo bản kế hoạch đầy đủ số liệu — lỗi AI không được làm mất kế hoạch.
    /// </summary>
    public string? AiError { get; set; }

    public PlanStatus Status { get; set; } = PlanStatus.New;

    public ICollection<PlanSuggestion> Suggestions { get; set; } = new List<PlanSuggestion>();
    public ICollection<PlanDecision> Decisions { get; set; } = new List<PlanDecision>();
}

/// <summary>
/// Một đề xuất hành động trong bản kế hoạch.
/// </summary>
public class PlanSuggestion : BaseEntity
{
    public Guid DailyPlanId { get; set; }

    public SuggestionType Type { get; set; }
    public SuggestionPriority Priority { get; set; }

    // --- Đối tượng liên quan ----------------------------------------------------

    /// <summary>Nguyên liệu đang gặp vấn đề.</summary>
    public Guid? IngredientId { get; set; }

    /// <summary>Món được chọn để "chở" nguyên liệu đó đi (món đem giảm giá).</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Lô hàng cụ thể đang cận hạn.</summary>
    public Guid? LotId { get; set; }

    // --- Nội dung do AI viết ----------------------------------------------------

    /// <summary>Tiêu đề đề xuất. VD: "Giảm 20% Trà đào cam sả trong 2 ngày".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Lý do nên làm, do AI viết dựa trên các con số bên dưới.
    /// AI phải trích dẫn số, không được bịa số mới.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>Nội dung banner cho trang bán hàng, tối đa 80 ký tự. Chỉ có với loại Discount.</summary>
    public string? BannerCopy { get; set; }

    // --- Con số do BACKEND tính -------------------------------------------------
    //     AI không được phép sinh hay sửa các trường dưới đây.

    /// <summary>Mức giảm đề xuất, phần trăm. VD: 20.</summary>
    public int? DiscountPercent { get; set; }

    /// <summary>Cần bán thêm bao nhiêu ly để tiêu hết phần nguyên liệu có nguy cơ.</summary>
    public int? TargetUnits { get; set; }

    /// <summary>
    /// Giá trị nguyên liệu cứu được nếu làm theo đề xuất (đồng).
    /// = min(lượng có nguy cơ, lượng bán thêm dự kiến × định mức) × giá vốn.
    /// </summary>
    public int? ExpectedWasteAvoided { get; set; }

    /// <summary>
    /// Lợi ích RÒNG (đồng) — chỉ số quyết định xếp hạng đề xuất.
    /// <para>
    /// = ExpectedWasteAvoided + lãi thêm từ ly bán thêm − lãi mất trên số ly
    ///   vốn dĩ đã bán được với giá gốc.
    /// </para>
    /// <para>
    /// Vế cuối cùng là chỗ hay bị quên: giảm giá món đang bán chạy có thể LỖ RÒNG
    /// dù vẫn "cứu" được nguyên liệu.
    /// </para>
    /// </summary>
    public int? ExpectedNetBenefit { get; set; }

    /// <summary>Số ngày còn lại trước khi lô hết hạn.</summary>
    public int? DaysUntilExpiry { get; set; }

    /// <summary>Lượng nguyên liệu có nguy cơ phải bỏ, theo đơn vị cơ sở.</summary>
    public double? QuantityAtRisk { get; set; }

    /// <summary>Thời gian đề xuất bắt đầu áp dụng.</summary>
    public DateTime? SuggestedFrom { get; set; }

    /// <summary>Thời gian đề xuất kết thúc — thường là ngày trước hạn sử dụng.</summary>
    public DateTime? SuggestedTo { get; set; }

    public int SortOrder { get; set; }

    public DailyPlan? Plan { get; set; }
    public Ingredient? Ingredient { get; set; }
    public Product? Product { get; set; }
    public ICollection<PlanDecision> Decisions { get; set; } = new List<PlanDecision>();
}

/// <summary>
/// Quyết định của người quản lý đối với một đề xuất.
/// <para>
/// Bảng này vừa là nhật ký kiểm toán, vừa là dữ liệu để đánh giá chất lượng AI:
/// tỷ lệ đề xuất được duyệt là chỉ số tin cậy quan trọng nhất.
/// </para>
/// </summary>
public class PlanDecision : BaseEntity
{
    public Guid DailyPlanId { get; set; }
    public Guid SuggestionId { get; set; }

    public DecisionAction Action { get; set; }

    /// <summary>Người ra quyết định.</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>Ghi chú của người duyệt. VD: "Giảm 15% thôi, 20% lỗ quá".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Nếu Action = Modified: các tham số người dùng đã sửa, dạng JSON.
    /// VD: <c>{"discountPercent":15,"endsAt":"2026-08-12T00:00:00Z"}</c>
    /// </summary>
    public string? ModifiedPayloadJson { get; set; }

    /// <summary>Khuyến mãi đã được tạo ra từ quyết định này, nếu duyệt.</summary>
    public Guid? CreatedPromotionId { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    public DailyPlan? Plan { get; set; }
    public PlanSuggestion? Suggestion { get; set; }
    public User? Actor { get; set; }
}

// ------------------------------------------------------------------------------
//  BẢNG TỔNG HỢP — nguồn dữ liệu cho dự báo
// ------------------------------------------------------------------------------

/// <summary>
/// Lượng tiêu thụ nguyên liệu theo từng ngày, đã tổng hợp sẵn.
/// <para>
/// Vì sao cần bảng này: mô hình dự báo EWMA cần đọc 28 ngày lịch sử ở mỗi lần
/// tính. Nếu quét bảng sổ cái (hàng trăm nghìn dòng) mỗi lần thì rất chậm.
/// Bảng này được job cuối ngày ghi một lần, sau đó chỉ đọc.
/// </para>
/// </summary>
public class DailyConsumption : BaseEntity
{
    public Guid StoreId { get; set; }
    public Guid IngredientId { get; set; }

    /// <summary>Ngày kinh doanh "yyyy-MM-dd" theo giờ Việt Nam.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Tổng lượng đã dùng trong ngày, theo đơn vị cơ sở.</summary>
    public double QuantityUsed { get; set; }

    /// <summary>Tổng giá vốn đã dùng trong ngày (đồng).</summary>
    public int CostUsed { get; set; }

    public Ingredient? Ingredient { get; set; }
}

/// <summary>
/// Doanh số theo món theo ngày, đã tổng hợp sẵn.
/// Dùng để tính số ly bán trung bình mỗi ngày khi sinh phương án khuyến mãi.
/// </summary>
public class DailySales : BaseEntity
{
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Ngày kinh doanh "yyyy-MM-dd" theo giờ Việt Nam.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Số ly bán được.</summary>
    public int UnitsSold { get; set; }

    /// <summary>Doanh thu (đồng).</summary>
    public int Revenue { get; set; }

    /// <summary>Giá vốn (đồng).</summary>
    public int Cost { get; set; }

    public Product? Product { get; set; }
}
