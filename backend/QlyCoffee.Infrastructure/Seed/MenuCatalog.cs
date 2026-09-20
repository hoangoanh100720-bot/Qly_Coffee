using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Infrastructure.Seed;

// ==============================================================================
//  THỰC ĐƠN CHUẨN — NGUYÊN LIỆU, DANH MỤC, MÓN VÀ CÔNG THỨC ĐỊNH LƯỢNG
// ==============================================================================
//
//  ĐÂY LÀ NGUỒN SỰ THẬT DUY NHẤT CỦA THỰC ĐƠN.
//  Trước đây toàn bộ dữ liệu này nằm trong DatabaseSeeder và chỉ chạy đúng MỘT
//  lần, lúc database còn trống. Hậu quả: thêm món mới vào mã nguồn thì quán đang
//  chạy không bao giờ thấy — muốn thấy phải xóa sạch database, tức là mất hết
//  đơn hàng và sổ kho. Tách ra đây để MenuSync có thể bơm phần còn thiếu vào một
//  database đã có dữ liệu mà không đụng tới thứ gì đang chạy.
//
//  QUY ƯỚC QUAN TRỌNG
//
//  · Sku và Slug là KHÓA ĐỊNH DANH. MenuSync dựa vào chúng để biết món/nguyên
//    liệu đã có hay chưa. ĐỔI Slug CỦA MỘT MÓN ĐANG BÁN = TẠO RA MỘT MÓN TRÙNG.
//
//  · Định lượng là cho MỘT PHẦN SIZE M. Size L nhân 1,4 (RecipeMultiplier).
//
//  · Giá ở đây là GIÁ KHỞI TẠO, chỉ dùng khi món lần đầu được tạo. Chủ quán sửa
//    giá trong trang quản lý thì MenuSync KHÔNG BAO GIỜ ghi đè lại — giá là
//    quyền của người bán, không phải của lập trình viên.
//
//  · Giá vốn (ComputedCost) KHÔNG nhập tay ở đây. Hệ thống tự tính từ công thức
//    theo Σ(định lượng × đơn giá nguyên liệu × (1 + hao hụt)), rồi từ đó gợi ý
//    giá bán — xem QlyCoffee.Shared/Pricing.
//
//  CÁCH VIẾT MỘT CÔNG THỨC CHO ĐÚNG
//
//    1. Ghi ĐỦ mọi thứ rời khỏi kho, kể cả ly, ống hút, đá. Thiếu một dòng là
//       kho không bao giờ trừ thứ đó và tới cuối tháng kiểm kê mới phát hiện hụt.
//    2. Nguyên liệu khách có quyền từ chối (đá, đường) đặt IsOptional = true.
//       Dòng tùy chọn không bị tính vào phép "còn làm được bao nhiêu ly", nên
//       hết đá không làm cả menu biến mất.
//    3. Thứ nào đong bằng miếng, lát, quả thì ghi Note quy đổi ra gram —
//       nhân viên mới đứng quầy đọc "60g đào" không biết là mấy miếng.
// ==============================================================================

/// <summary>Cách phục vụ một món — quyết định món có size và tùy chọn gì.</summary>
public enum ServeStyle
{
    /// <summary>Đồ uống đá: có size M/L, cho chọn mức đường và mức đá.</summary>
    Iced = 0,

    /// <summary>
    /// Đồ uống nóng: có size M/L, cho chọn mức đường nhưng KHÔNG có mức đá.
    /// Hỏi khách "bao nhiêu phần trăm đá" cho một ly cà phê nóng là lỗi giao diện.
    /// </summary>
    Hot = 1,

    /// <summary>
    /// Đồ ăn: KHÔNG size, KHÔNG mức đường, KHÔNG mức đá, KHÔNG topping.
    /// Một chiếc croissant không có size L và không pha loãng được.
    /// </summary>
    Food = 2,

    /// <summary>
    /// Pha được cả nóng lẫn đá, KHÁCH CHỌN. Có thêm nhóm "Dùng nóng hay đá",
    /// và mức đá chỉ hiện ra sau khi khách chọn dùng đá.
    /// <para>
    /// Chỉ đặt cho món mà quán thật sự phục vụ được cả hai kiểu — cappuccino,
    /// latte, cà phê kem trứng… Món có chữ "đá" hoặc "nóng" ngay trong tên
    /// (cà phê sữa đá, cacao nóng) thì KHÔNG: tên món đã hứa một kiểu duy nhất,
    /// cho chọn kiểu kia là tự mâu thuẫn với thực đơn.
    /// </para>
    /// </summary>
    HotOrIced = 3
}

/// <summary>Một dòng trong công thức định lượng.</summary>
/// <param name="Sku">Mã nguyên liệu, phải có trong <see cref="MenuCatalog.Ingredients"/>.</param>
/// <param name="Quantity">Định lượng cho một phần size M, theo đơn vị gốc của nguyên liệu.</param>
/// <param name="Optional">Khách có quyền từ chối thứ này (đá, đường, topping mặc định).</param>
/// <param name="Note">Quy đổi thực tế hoặc thao tác pha chế. Barista đọc dòng này.</param>
public record RecipeLine(string Sku, double Quantity, bool Optional = false, string? Note = null);

/// <summary>Định nghĩa một nguyên liệu trong kho.</summary>
public record IngredientSpec(
    string Sku,
    string Name,
    IngredientCategory Category,
    BaseUnit Unit,
    /// <summary>Màu THẬT của nguyên liệu ngoài đời — dùng vẽ icon và chấm màu bảng kho.</summary>
    string ColorHex,
    string IconKey,
    /// <summary>Giá vốn một đơn vị gốc, đồng. VD 180 = 180đ mỗi gram cà phê.</summary>
    int UnitCost,
    /// <summary>Hạn dùng mặc định tính bằng ngày. null = không có hạn (đá, ly nhựa).</summary>
    int? ShelfLifeDays,
    int WarnDays,
    /// <summary>Hao hụt chế biến thực tế: gọt vỏ, dính nồi, rơi vãi.</summary>
    double WastageRate,
    double MinStock,
    double ReorderPoint,
    /// <summary>
    /// BÁN THÀNH PHẨM — quán tự nấu/ủ chứ không mua ngoài. Xem <see cref="PrepRecipes"/>.
    /// Nguyên liệu như vậy chỉ vào kho qua màn hình Sơ chế.
    /// </summary>
    bool Prepared = false);

/// <summary>Định nghĩa một danh mục trên thực đơn.</summary>
public record CategorySpec(
    string Key,
    string Name,
    string Slug,
    string Description,
    string ColorHex,
    string IconKey,
    int SortOrder);

/// <summary>Định nghĩa một món kèm công thức đầy đủ.</summary>
public record ProductSpec(
    string CategoryKey,
    string Name,
    string Slug,
    string Description,
    /// <summary>Giá khởi tạo, đồng. Chỉ dùng lúc tạo mới, không ghi đè giá chủ quán đã sửa.</summary>
    int Price,
    string ColorPrimaryHex,
    string ColorAccentHex,
    string Tags,
    ServeStyle Serve,
    RecipeLine[] Recipe,
    bool Featured = false,
    int SortOrder = 0,
    /// <summary>Thời gian pha một phần, giây. 0 = lấy mặc định theo danh mục.</summary>
    int PrepSeconds = 0,
    /// <summary>Cho khách chọn topping hay không. Chỉ có ý nghĩa với đồ uống.</summary>
    bool AllowTopping = false);

/// <summary>Định nghĩa một topping kèm công thức riêng của nó.</summary>
public record ToppingSpec(string Name, int PriceDelta, string ColorHex, string Sku, double Quantity, int SortOrder);

public static class MenuCatalog
{
    // ==========================================================================
    //  §1  NGUYÊN LIỆU
    //
    //  Đơn giá là giá NHẬP SỈ tại TP.HCM, quy về đơn vị nhỏ nhất. Nhập hàng thật
    //  sẽ cập nhật lại AverageUnitCost theo giá của từng lô, nên số ở đây chỉ là
    //  điểm khởi đầu để hệ thống tính được giá vốn ngay từ ngày đầu.
    //
    //  Hao hụt (WastageRate) là con số hay bị bỏ qua nhất và cũng là con số làm
    //  lệch giá vốn nhiều nhất:
    //    · Trái cây phải gọt vỏ bỏ hạt   → 10–35% (dứa và bơ nặng nhất)
    //    · Topping phải nấu, dễ dính nồi → 6–8%
    //    · Bột, siro, đường              → 1–3%
    //    · Đá viên tan trong lúc thao tác → 10%
    // ==========================================================================
    public static readonly IngredientSpec[] Ingredients =
    {
        // --- Cà phê ---------------------------------------------------------
        new("COF-ROB-01", "Cà phê Robusta rang xay", IngredientCategory.Coffee,
            BaseUnit.Gram, "#3B2416", "coffee-beans", 180, 180, 30, 0.02, 500, 1500),
        new("COF-ARA-01", "Cà phê Arabica rang xay", IngredientCategory.Coffee,
            BaseUnit.Gram, "#4E3524", "coffee-beans", 320, 180, 30, 0.02, 300, 1000),

        // --- Sữa & kem ------------------------------------------------------
        // Sữa tươi hạn rất ngắn (7 ngày) — nguyên liệu hay vào cảnh báo cận hạn nhất.
        new("DAI-MILK-01", "Sữa tươi không đường", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#F5EDE0", "milk", 32, 7, 3, 0.03, 2000, 5000),
        new("DAI-CON-01", "Sữa đặc có đường", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#E8C89A", "condensed-milk", 55, 90, 14, 0.02, 800, 2000),
        new("DAI-WHIP-01", "Kem sữa béo (whipping)", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#FBF6EC", "cream", 120, 14, 5, 0.05, 500, 1200),
        new("DAI-CHE-01", "Kem cheese", IngredientCategory.Dairy,
            BaseUnit.Gram, "#FAF0DC", "cheese-foam", 180, 10, 4, 0.05, 400, 1000),
        new("DAI-YOG-01", "Sữa chua không đường", IngredientCategory.Dairy,
            BaseUnit.Gram, "#FDFBF5", "milk", 45, 14, 4, 0.02, 800, 2000),

        // --- Trà -------------------------------------------------------------
        new("TEA-BLK-01", "Hồng trà", IngredientCategory.Tea,
            BaseUnit.Gram, "#8B3A1A", "tea-leaf", 250, 365, 30, 0.02, 200, 500),
        new("TEA-OOL-01", "Trà ô long", IngredientCategory.Tea,
            BaseUnit.Gram, "#6B7A3F", "tea-leaf", 380, 365, 30, 0.02, 150, 400),
        new("TEA-JAS-01", "Trà lài", IngredientCategory.Tea,
            BaseUnit.Gram, "#9FA85E", "tea-leaf", 300, 365, 30, 0.02, 150, 400),
        new("TEA-GRN-01", "Lục trà (trà xanh)", IngredientCategory.Tea,
            BaseUnit.Gram, "#7D9448", "tea-leaf", 280, 365, 30, 0.02, 150, 400),
        new("TEA-CHA-01", "Hoa cúc khô", IngredientCategory.Tea,
            BaseUnit.Gram, "#E4C558", "tea-leaf", 520, 365, 30, 0.02, 80, 200),

        // --- Bột --------------------------------------------------------------
        new("POW-MAT-01", "Bột matcha Nhật", IngredientCategory.Powder,
            BaseUnit.Gram, "#7CA24A", "matcha", 1400, 180, 20, 0.03, 100, 300),
        new("POW-CAC-01", "Bột cacao", IngredientCategory.Powder,
            BaseUnit.Gram, "#5C3A21", "cacao", 450, 365, 30, 0.03, 200, 500),
        // Bột kem béo là thứ làm nên vị "trà sữa truyền thống" — sữa tươi không thay được.
        new("POW-CRE-01", "Bột kem béo (creamer)", IngredientCategory.Powder,
            BaseUnit.Gram, "#F3EADC", "milk", 95, 365, 30, 0.03, 500, 1500),

        // Bột hương vị cho nhóm trà sữa: một muỗng bột cho ra đúng màu và vị
        // của món, không phụ thuộc trái cây theo mùa. Mua theo bịch 1kg.
        new("POW-TAR-01", "Bột khoai môn", IngredientCategory.Powder,
            BaseUnit.Gram, "#B4A0C4", "powder", 180, 365, 30, 0.03, 300, 800),
        new("POW-STR-01", "Bột dâu", IngredientCategory.Powder,
            BaseUnit.Gram, "#E8A0B0", "powder", 200, 365, 30, 0.03, 300, 800),
        new("POW-THG-01", "Bột trà thái xanh", IngredientCategory.Powder,
            BaseUnit.Gram, "#8DBE6A", "matcha", 320, 365, 30, 0.03, 250, 600),

        // --- Topping ----------------------------------------------------------
        //
        //  Trân châu và pudding là BÁN THÀNH PHẨM: quán nấu tại chỗ từ nguyên liệu
        //  khô, không mua sẵn. Đó cũng là lý do chúng chỉ để được vài giờ và là
        //  nhóm hết hàng giữa ca thường xuyên nhất — nấu thêm một mẻ ngay tại quầy
        //  là thao tác hằng ngày, xem PrepRecipes §5.
        //
        //  Thạch dừa và nha đam thì mua theo hũ, để được lâu, nên vẫn là hàng nhập.
        new("TOP-BOB-RAW", "Trân châu đen khô", IngredientCategory.Topping,
            BaseUnit.Gram, "#3A2C22", "boba", 70, 365, 30, 0.02, 2000, 5000),
        new("TOP-BOW-RAW", "Trân châu trắng khô", IngredientCategory.Topping,
            BaseUnit.Gram, "#DCD3C4", "boba", 95, 365, 30, 0.02, 1200, 3000),
        new("TOP-PUD-RAW", "Bột pudding trứng", IngredientCategory.Topping,
            BaseUnit.Gram, "#E8D9A8", "pudding", 180, 365, 30, 0.02, 400, 1000),

        new("TOP-BOB-01", "Trân châu đen", IngredientCategory.Topping,
            BaseUnit.Gram, "#2A1F1A", "boba", 45, 1, 1, 0.08, 500, 1500, Prepared: true),
        new("TOP-BOW-01", "Trân châu trắng", IngredientCategory.Topping,
            BaseUnit.Gram, "#EDE4D6", "boba", 60, 1, 1, 0.08, 300, 900, Prepared: true),
        new("TOP-JEL-01", "Thạch dừa", IngredientCategory.Topping,
            BaseUnit.Gram, "#F2EDE3", "jelly", 40, 14, 5, 0.05, 400, 1000),
        new("TOP-PUD-01", "Pudding trứng", IngredientCategory.Topping,
            BaseUnit.Gram, "#F3D68A", "pudding", 70, 1, 1, 0.06, 300, 800, Prepared: true),
        new("TOP-ALO-01", "Thạch nha đam", IngredientCategory.Topping,
            BaseUnit.Gram, "#EAF0DC", "jelly", 55, 10, 4, 0.06, 300, 800),
        new("TOP-OREO-01", "Bánh quy socola nghiền", IngredientCategory.Topping,
            BaseUnit.Gram, "#3A3335", "cacao", 220, 180, 20, 0.03, 200, 500),

        //  Hạt nổ và củ năng mua đóng hũ/lon, sương sáo mua theo khối — không nấu.
        //  Hạt nổ hao 5% vì hạt vỡ khi múc; múc bằng muôi thủng, đừng dùng thìa ép.
        new("TOP-POP-01", "Hạt nổ", IngredientCategory.Topping,
            BaseUnit.Gram, "#F4A259", "popping", 85, 30, 7, 0.05, 400, 1000),
        new("TOP-WCH-01", "Củ năng cắt hạt lựu", IngredientCategory.Topping,
            BaseUnit.Gram, "#F4F1E6", "chestnut", 90, 180, 30, 0.04, 300, 800),
        new("TOP-GRJ-01", "Sương sáo", IngredientCategory.Topping,
            BaseUnit.Gram, "#2B2A24", "jelly", 30, 7, 2, 0.05, 400, 1000),
        //  Thạch trái cây mua theo hũ ngâm siro — topping thứ hai của nhóm trà sữa.
        new("TOP-FJL-01", "Thạch trái cây", IngredientCategory.Topping,
            BaseUnit.Gram, "#F2A65A", "jelly", 45, 30, 7, 0.05, 400, 1000),

        //  Thạch cà phê là BÁN THÀNH PHẨM: nấu từ cà phê phin cốt và bột rau câu.
        new("TOP-AGA-RAW", "Bột rau câu dẻo", IngredientCategory.Topping,
            BaseUnit.Gram, "#EFE9DA", "sugar", 600, 365, 30, 0.02, 60, 150),
        new("TOP-COF-01", "Thạch cà phê", IngredientCategory.Topping,
            BaseUnit.Gram, "#4A2E1C", "jelly", 30, 1, 1, 0.05, 300, 800, Prepared: true),

        // --- Trái cây ---------------------------------------------------------
        new("FRU-PEA-01", "Đào ngâm", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F0A868", "peach", 95, 5, 2, 0.05, 800, 2000),
        new("FRU-LYC-01", "Vải ngâm", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F6E7E2", "peach", 90, 5, 2, 0.05, 600, 1500),
        new("FRU-MAN-01", "Xoài tươi", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F2B33D", "mango", 55, 4, 2, 0.12, 1000, 2500),
        new("FRU-LEM-01", "Chanh tươi", IngredientCategory.Fruit,
            BaseUnit.Piece, "#D8E04A", "lemon", 3000, 10, 3, 0.10, 20, 50),
        new("FRU-KUM-01", "Tắc (quất)", IngredientCategory.Fruit,
            BaseUnit.Piece, "#E8A427", "lemon", 700, 10, 3, 0.10, 60, 150),
        new("FRU-ORA-01", "Cam vàng", IngredientCategory.Fruit,
            BaseUnit.Piece, "#E8871E", "lemon", 12000, 14, 4, 0.15, 15, 40),
        new("FRU-LGR-01", "Sả tươi", IngredientCategory.Fruit,
            BaseUnit.Gram, "#C3CE8A", "lemongrass", 35, 7, 3, 0.15, 200, 500),
        new("FRU-MIN-01", "Lá bạc hà", IngredientCategory.Fruit,
            BaseUnit.Gram, "#6FA86B", "leaf", 200, 4, 2, 0.20, 60, 150),
        new("FRU-STR-01", "Dâu tây", IngredientCategory.Fruit,
            BaseUnit.Gram, "#D63B3B", "peach", 180, 3, 1, 0.10, 400, 1000),
        new("FRU-BLU-01", "Việt quất đông lạnh", IngredientCategory.Fruit,
            BaseUnit.Gram, "#4A4A8C", "peach", 320, 180, 20, 0.03, 300, 800),
        new("FRU-GUA-01", "Ổi hồng", IngredientCategory.Fruit,
            BaseUnit.Gram, "#E68A8A", "peach", 35, 5, 2, 0.18, 600, 1500),
        // Dứa hao hụt tới 30% vì phải gọt vỏ và khoét mắt — cao nhất trong nhóm trái cây.
        new("FRU-PIN-01", "Dứa (thơm)", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F2C744", "mango", 25, 5, 2, 0.30, 800, 2000),
        // Bơ bỏ vỏ và hạt mất khoảng một phần tư quả.
        new("FRU-AVO-01", "Bơ sáp", IngredientCategory.Fruit,
            BaseUnit.Gram, "#7A8B4A", "leaf", 60, 4, 2, 0.28, 600, 1500),
        new("FRU-GIN-01", "Gừng tươi", IngredientCategory.Fruit,
            BaseUnit.Gram, "#E0C892", "lemongrass", 45, 21, 5, 0.15, 150, 400),

        // --- Siro & sốt --------------------------------------------------------
        // Hai dòng đầu là BÁN THÀNH PHẨM: quán tự đánh kem muối và tự nấu nước
        // đường, không ai đi mua chai nước đường pha sẵn. Đánh dấu Prepared để
        // chúng vào kho qua màn hình Sơ chế thay vì màn hình Nhập kho.
        // Các dòng còn lại (siro đào, sốt socola…) thì mua chai — giữ nguyên.
        new("SYR-SLT-01", "Kem muối", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#F7F2E8", "cream", 140, 5, 2, 0.04, 500, 1200, Prepared: true),
        new("SYR-SUG-01", "Nước đường", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#D9B368", "syrup", 18, 30, 7, 0.01, 1500, 3000, Prepared: true),
        new("SYR-BRW-01", "Đường đen (brown sugar)", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#6B4423", "syrup", 65, 30, 7, 0.02, 500, 1200),
        new("SYR-PEA-01", "Siro đào", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#F0A868", "syrup", 85, 365, 30, 0.01, 500, 1200),
        new("SYR-LYC-01", "Siro vải", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#F6DDD8", "syrup", 85, 365, 30, 0.01, 400, 1000),
        new("SYR-STR-01", "Siro dâu", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#D63B5A", "syrup", 80, 365, 30, 0.01, 400, 1000),
        new("SYR-CHO-01", "Sốt socola", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#4A2E1E", "syrup", 95, 365, 30, 0.02, 400, 1000),
        new("SYR-CAR-01", "Sốt caramel", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#B5772E", "syrup", 100, 365, 30, 0.02, 400, 1000),
        // Siro trái cây mua chai cho nhóm soda và trà trái cây: giữ vị ổn định
        // giữa các ly khi trái cây tươi lúc ngọt lúc chua.
        new("SYR-MIN-01", "Siro bạc hà", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#7FCB9A", "syrup", 85, 365, 30, 0.01, 400, 1000),
        new("SYR-BLU-01", "Siro việt quất", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#5A4E96", "syrup", 95, 365, 30, 0.01, 400, 1000),
        new("SYR-PIN-01", "Siro dứa", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#F2C744", "syrup", 80, 365, 30, 0.01, 400, 1000),
        new("SYR-GUA-01", "Siro ổi hồng", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#E68A8A", "syrup", 85, 365, 30, 0.01, 400, 1000),
        new("SYR-HON-01", "Mật ong", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#D89A2B", "syrup", 180, 730, 60, 0.01, 300, 800),
        new("SYR-COC-01", "Nước cốt dừa", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#FAF7F0", "cream", 45, 5, 2, 0.03, 500, 1200),

        // --- Chất tạo ngọt -----------------------------------------------------
        new("SWE-SUG-01", "Đường cát", IngredientCategory.Sweetener,
            BaseUnit.Gram, "#F7F3EA", "sugar", 22, 730, 60, 0.01, 2000, 5000),
        // Muối chỉ dùng để đánh kem muối, lượng rất nhỏ nhưng vẫn phải có trong
        // kho: thiếu một dòng là công thức kem muối không tự trừ được thứ gì.
        new("SWE-SAL-01", "Muối tinh", IngredientCategory.Sweetener,
            BaseUnit.Gram, "#FBFBF8", "sugar", 8, 730, 60, 0.01, 200, 500),

        // --- BÁN THÀNH PHẨM (quán tự ủ / tự nấu) --------------------------------
        //
        //  Nhóm này KHÔNG mua từ nhà cung cấp. Nó vào kho qua màn hình Sơ chế:
        //  nhân viên bấm "Ủ hồng trà", hệ thống trừ lá trà và tạo một bình cốt
        //  có hạn dùng tính bằng giờ. Công thức các mẻ nằm ở §5 PrepRecipes.
        //
        //  BA CON SỐ CẦN HIỂU ĐÚNG:
        //
        //  · Đơn giá ở đây chỉ là ƯỚC TÍNH ban đầu để hệ thống chạy được ngay từ
        //    ngày đầu. Ngay sau mẻ đầu tiên nó được thay bằng giá vốn THẬT, tính
        //    từ đúng những lô lá trà đã bị trừ.
        //
        //  · Hạn dùng mặc định (ngày) gần như không dùng tới, vì mỗi mẻ nhận hạn
        //    riêng theo ShelfLifeHours của công thức sơ chế. Nó chỉ là lưới an
        //    toàn cho trường hợp lô được tạo bằng đường khác.
        //
        //  · Ngưỡng tối thiểu ở đây mang nghĩa "sắp phải ủ mẻ mới", chứ không
        //    phải "sắp phải gọi nhà cung cấp". Vì vậy đặt bằng vài ly, không
        //    phải vài ngày bán như nguyên liệu mua ngoài.
        //
        //  Hao hụt 2% là phần đọng lại trong bình và rớt ra ngoài lúc rót. Phần
        //  hao hụt của khâu ủ (lá trà ngậm nước) KHÔNG nằm ở đây — nó đã nằm sẵn
        //  trong sản lượng mỗi mẻ. Ghi cả hai chỗ là tính hao hụt hai lần.
        //  Đơn giá cốt trà = giá lá × 0,06 (6g lá cho 100ml cốt, xem §5).
        new("PRE-TEA-BLK", "Cốt hồng trà", IngredientCategory.Tea,
            BaseUnit.Milliliter, "#A64B1E", "syrup", 15, 1, 1, 0.02, 400, 800, Prepared: true),
        new("PRE-TEA-OOL", "Cốt trà ô long", IngredientCategory.Tea,
            BaseUnit.Milliliter, "#96944A", "syrup", 23, 1, 1, 0.02, 400, 800, Prepared: true),
        new("PRE-TEA-JAS", "Cốt trà lài", IngredientCategory.Tea,
            BaseUnit.Milliliter, "#C2BE72", "syrup", 18, 1, 1, 0.02, 300, 600, Prepared: true),
        new("PRE-TEA-GRN", "Cốt lục trà", IngredientCategory.Tea,
            BaseUnit.Milliliter, "#9BB35C", "syrup", 17, 1, 1, 0.02, 350, 700, Prepared: true),
        new("PRE-TEA-CHA", "Cốt hoa cúc", IngredientCategory.Tea,
            BaseUnit.Milliliter, "#EBD681", "syrup", 9, 1, 1, 0.02, 220, 440, Prepared: true),
        new("PRE-COF-PHI", "Cà phê phin cốt", IngredientCategory.Coffee,
            BaseUnit.Milliliter, "#2E1B0F", "syrup", 90, 1, 1, 0.02, 80, 160, Prepared: true),
        new("PRE-COF-CLD", "Cốt cold brew", IngredientCategory.Coffee,
            BaseUnit.Milliliter, "#5A3A22", "syrup", 46, 7, 2, 0.02, 200, 400, Prepared: true),

        // --- Bánh & đồ ăn nhẹ (nhập về nguyên cái) ------------------------------
        // Nhóm này KHÔNG pha chế: nhập bao nhiêu bán bấy nhiêu, hao hụt gần bằng 0
        // nhưng hạn dùng ngắn nên hết ngày là phải hủy. Đó là lý do đặt hạn 2–3 ngày:
        // hệ thống sẽ nhắc trước khi bánh kịp hỏng.
        new("BAK-CRO-01", "Bánh sừng bò (croissant)", IngredientCategory.Bakery,
            BaseUnit.Piece, "#D8A860", "cake", 12000, 2, 1, 0.02, 10, 30),
        new("BAK-TIR-01", "Bánh tiramisu (miếng)", IngredientCategory.Bakery,
            BaseUnit.Piece, "#7A5230", "cake", 22000, 3, 1, 0.02, 8, 20),
        new("BAK-CHE-01", "Bánh phô mai nướng (miếng)", IngredientCategory.Bakery,
            BaseUnit.Piece, "#EAC77A", "cake", 20000, 3, 1, 0.02, 8, 20),
        new("BAK-SUK-01", "Bánh su kem", IngredientCategory.Bakery,
            BaseUnit.Piece, "#E8D3AE", "cake", 6000, 2, 1, 0.03, 15, 40),
        new("BAK-COO-01", "Cookie socola", IngredientCategory.Bakery,
            BaseUnit.Piece, "#8A5A32", "cake", 7000, 14, 4, 0.02, 15, 40),
        new("BAK-BMQ-01", "Bánh mì que pate", IngredientCategory.Bakery,
            BaseUnit.Piece, "#C8965A", "cake", 6500, 1, 1, 0.03, 15, 40),

        // --- Khác ---------------------------------------------------------------
        new("OTH-ICE-01", "Đá viên", IngredientCategory.Other,
            BaseUnit.Gram, "#DCEAF2", "ice", 2, null, 0, 0.10, 10000, 20000),
        new("OTH-EGG-01", "Trứng gà", IngredientCategory.Other,
            BaseUnit.Piece, "#F0D9A8", "egg", 3500, 21, 5, 0.05, 20, 50),
        new("OTH-SOD-01", "Soda (nước có ga)", IngredientCategory.Other,
            BaseUnit.Milliliter, "#EAF4F7", "ice", 12, 180, 20, 0.02, 2000, 5000),

        // --- Bao bì ---------------------------------------------------------------
        new("PKG-CUP-M", "Ly nhựa 500ml + nắp", IngredientCategory.Packaging,
            BaseUnit.Piece, "#E8EEF0", "cup", 1800, null, 0, 0.02, 200, 500),
        new("PKG-CUP-L", "Ly nhựa 700ml + nắp", IngredientCategory.Packaging,
            BaseUnit.Piece, "#E8EEF0", "cup", 2200, null, 0, 0.02, 150, 400),
        new("PKG-CUP-H", "Ly giấy nóng 350ml + nắp", IngredientCategory.Packaging,
            BaseUnit.Piece, "#E4D9C8", "cup", 2400, null, 0, 0.02, 100, 300),
        new("PKG-STR-01", "Ống hút", IngredientCategory.Packaging,
            BaseUnit.Piece, "#C46A52", "straw", 350, null, 0, 0.03, 300, 800),
        new("PKG-BOX-01", "Hộp giấy đựng bánh", IngredientCategory.Packaging,
            BaseUnit.Piece, "#D9CDBA", "cup", 2500, null, 0, 0.02, 50, 150),
        new("PKG-NAP-01", "Khăn giấy + nĩa gỗ", IngredientCategory.Packaging,
            BaseUnit.Piece, "#F2EFE8", "straw", 600, null, 0, 0.03, 100, 300)
    };

    // ==========================================================================
    //  §2  DANH MỤC
    //
    //  ColorHex lấy từ màu đặc trưng của nhóm — dùng tô chip lọc ở trang thực đơn.
    // ==========================================================================
    public static readonly CategorySpec[] Categories =
    {
        new("coffee",  "Cà phê",            "ca-phe",
            "Pha phin truyền thống và espresso",        "#3B2416", "cup-hot",    1),
        new("milktea", "Trà sữa",           "tra-sua",
            "Ủ trà tươi mỗi 4 tiếng",                   "#C9A57B", "bubble-tea", 2),
        new("fruit",   "Trà trái cây",      "tra-trai-cay",
            "Trái cây tươi, không dùng siro pha sẵn",   "#E8944A", "leaf",       3),
        new("tea",     "Trà & Thảo mộc",    "tra-thao-moc",
            "Trà thuần và trà thảo mộc, không sữa",     "#6E8B3D", "tea-leaf",   4),
        new("blended", "Đá xay & Sinh tố",  "da-xay",
            "Xay tại chỗ khi có đơn",                   "#8FC3D8", "blender",    5),
        new("matcha",  "Matcha & Cacao",    "matcha-cacao",
            "Matcha Uji và cacao nguyên chất",          "#7CA24A", "leaf",       6),
        new("yogurt",  "Sữa chua",          "sua-chua",
            "Sữa chua nhà làm, đánh đá tại quầy",       "#E9D9E8", "milk",       7),
        new("bakery",  "Bánh & Đồ ăn nhẹ",  "banh-do-an",
            "Bánh nhập trong ngày, hết là hết",         "#C98A4B", "cake",       8)
    };

    /// <summary>
    /// Thời gian pha mặc định theo nhóm món, tính bằng giây.
    /// <para>
    /// Đây là số ban đầu để hệ thống chạy được ngay. Quán PHẢI bấm đồng hồ đo lại
    /// cho đúng tay nghề nhân viên mình, vì thời gian hứa với khách phụ thuộc
    /// hoàn toàn vào nó.
    /// </para>
    /// </summary>
    public static readonly Dictionary<string, int> PrepSecondsByCategory = new()
    {
        ["coffee"]  = 100,   // pha máy hoặc phin đã ủ sẵn
        ["milktea"] = 120,   // ủ trà, lắc, múc trân châu
        ["fruit"]   = 130,   // dằm trái cây tươi tại chỗ
        ["tea"]     = 90,    // chỉ ủ và rót
        ["blended"] = 150,   // xay rồi còn phải tráng cối
        ["matcha"]  = 100,   // đánh matcha bằng chasen
        ["yogurt"]  = 90,    // đánh sữa chua với đá
        ["bakery"]  = 30     // lấy ra hộp, không pha chế
    };

    // ==========================================================================
    //  §3  TOPPING
    //
    //  Topping CÓ tiêu tốn nguyên liệu nên mỗi món đều có công thức riêng —
    //  khách thêm trân châu thì kho phải trừ trân châu, không thì cuối tháng hụt.
    // ==========================================================================
    public static readonly ToppingSpec[] Toppings =
    {
        new("Trân châu đen",   8000,  "#2A1F1A", "TOP-BOB-01",  40, 1),
        new("Trân châu trắng", 10000, "#EDE4D6", "TOP-BOW-01",  35, 2),
        new("Thạch dừa",       7000,  "#F2EDE3", "TOP-JEL-01",  40, 3),
        new("Thạch nha đam",   8000,  "#EAF0DC", "TOP-ALO-01",  45, 4),
        new("Pudding trứng",   10000, "#F3D68A", "TOP-PUD-01",  45, 5),
        new("Kem cheese",      12000, "#FAF0DC", "DAI-CHE-01",  35, 6),
        new("Bánh quy nghiền", 9000,  "#3A3335", "TOP-OREO-01", 20, 7),
        new("Hạt nổ",          9000,  "#F4A259", "TOP-POP-01",  40, 8),
        new("Củ năng",         8000,  "#F4F1E6", "TOP-WCH-01",  40, 9),
        new("Sương sáo",       7000,  "#2B2A24", "TOP-GRJ-01",  50, 10),
        new("Thạch cà phê",    8000,  "#4A2E1C", "TOP-COF-01",  45, 11),
        new("Thạch trái cây",  8000,  "#F2A65A", "TOP-FJL-01",  40, 12)
    };

    /// <summary>
    /// Mức đường và mức đá KHÔNG có công thức riêng.
    /// <para>
    /// Tác động của chúng lên kho quá nhỏ so với độ phức tạp phải thêm vào logic
    /// trừ kho. Nếu sau này cần chính xác hơn thì thêm cột QuantityDelta cho phép
    /// giá trị âm, chứ đừng nhân hệ số vào cả công thức.
    /// </para>
    /// </summary>
    public static readonly string[] SugarLevels =
        { "100% đường", "70% đường", "50% đường", "30% đường", "Không đường" };

    public static readonly string[] IceLevels =
        { "100% đá", "70% đá", "50% đá", "Ít đá", "Không đá" };

    /// <summary>
    /// Hai lựa chọn của nhóm "Dùng nóng hay đá", chỉ gắn cho món
    /// <see cref="ServeStyle.HotOrIced"/>.
    /// <para>
    /// Nhóm này KHÔNG có lựa chọn mặc định — trang đặt món cố ý để trống và bắt
    /// khách bấm. Không có mặc định nào đúng cho cả menu: bạc xỉu thì hầu như ai
    /// cũng gọi đá, còn cà phê kem trứng phải nóng mới đúng vị.
    /// </para>
    /// <para>
    /// Chữ "nóng" trong tên là thứ giao diện dựa vào để biết có cần ẩn mức đá
    /// hay không — xem <c>QlyCoffee.Shared.ModifierGroupKinds.IsHotChoice</c>.
    /// </para>
    /// </summary>
    public static readonly string[] TemperatureChoices = { "Dùng nóng", "Dùng đá" };

    // ==========================================================================
    //  §3b  GỢI Ý THƯỞNG THỨC
    //
    //  Một câu cho mỗi món: uống thế nào cho đúng vị, và ăn kèm gì thì ngon hơn.
    //
    //  VÌ SAO ĐỂ RIÊNG MỘT BẢNG chứ không nhét vào ProductSpec: đây là câu chữ
    //  bán hàng, chủ quán sẽ sửa nhiều lần theo mùa và theo món bánh đang có.
    //  Gom một chỗ thì đọc và sửa được cả loạt mà không phải lần trong công thức.
    //
    //  BA QUY TẮC VIẾT
    //    1. Phải NÓI ĐƯỢC MỘT ĐIỀU KHÁCH CHƯA BIẾT — cách thưởng thức đúng kiểu,
    //       hoặc món ăn kèm hợp vị. Câu khen suông ("ngon lắm") thì bỏ hẳn đi.
    //    2. Món ăn kèm phải CÓ THẬT trong thực đơn, nếu không là dắt khách đi
    //       tìm một thứ quán không bán.
    //    3. Một câu, dưới 120 ký tự. Đây là dòng chú thích, không phải bài viết.
    // ==========================================================================
    public static readonly Dictionary<string, string> PairingBySlug = new(StringComparer.Ordinal)
    {
        // --- Cà phê -----------------------------------------------------------
        ["ca-phe-kem-trung"] =
            "Uống nóng ngay khi kem còn bông; chấm bánh mì que hoặc quẩy vào lớp kem trứng đúng kiểu Hà Nội.",
        ["ca-phe-muoi"] =
            "Đừng khuấy vội — hớp ngụm đầu qua lớp kem muối, ăn kèm croissant bơ là chuẩn bài.",
        ["bac-xiu"] =
            "Ngọt dịu nên hợp bánh ít ngọt: croissant bơ hoặc cookie socola.",
        ["ca-phe-sua-da"] =
            "Đậm và ngọt, dùng kèm bánh mì que pate cho bữa sáng no bụng.",
        ["ca-phe-den-da"] =
            "Vị đắng gắt nên hợp món ngọt: bánh tiramisu hoặc cookie socola.",
        ["cold-brew"] =
            "Chua thanh, ít đắng — uống khi bụng đói vẫn êm, hợp bánh phô mai nướng.",
        ["americano"] =
            "Loãng và tỉnh táo, hợp ngồi làm việc lâu. Dùng kèm croissant bơ.",
        ["espresso"] =
            "Uống hết trong hai ngụm khi còn nóng, kèm một chiếc cookie socola.",
        ["latte"] =
            "Ngậy và dịu, hợp buổi sáng. Dùng kèm bánh su kem hoặc croissant bơ.",
        ["cappuccino"] =
            "Lớp foam dày nhất khi vừa pha xong — uống ngay, kèm bánh tiramisu.",
        ["ca-phe-cot-dua"] =
            "Béo đậm nên uống chậm, ăn kèm bánh phô mai nướng cho cân vị.",
        ["caramel-macchiato"] =
            "Hút một hơi xuyên qua các lớp để nếm đủ caramel, sữa và cà phê. Hợp bánh su kem.",

        // --- Trà sữa ----------------------------------------------------------
        ["tra-sua-tran-chau-duong-den"] =
            "Trân châu ngon nhất trong 2 tiếng đầu — uống sớm, đừng để tủ lạnh qua đêm.",
        ["tra-sua-truyen-thong"] =
            "Vị nền dễ chịu, thêm trân châu đen hoặc thạch dừa là thành món quen.",
        ["tra-sua-o-long"] =
            "Hậu vị trà rõ nên hợp bánh ngọt đậm như tiramisu.",
        ["tra-sua-matcha"] =
            "Chát nhẹ, cân với bánh su kem hoặc cookie socola.",
        ["hong-tra-sua-kem-cheese"] =
            "Nghiêng ly 45° uống trực tiếp, đừng khuấy — để lớp kem cheese mặn chạm lưỡi trước.",
        ["tra-sua-khoai-mon"] =
            "Béo bùi, thêm trân châu trắng cho đủ độ dai.",
        ["tra-sua-dau"] =
            "Ngọt trái cây, hợp khách nhỏ tuổi. Dùng kèm bánh su kem.",
        ["tra-sua-socola"] =
            "Đậm socola nên chọn 50% đường là vừa, kèm cookie socola nếu thích ngọt đậm.",
        ["tra-sua-thai-xanh"] =
            "Thơm nồng đặc trưng — uống lạnh thật sâu, để 100% đá.",

        // --- Trà trái cây -----------------------------------------------------
        ["tra-dao-cam-sa"] =
            "Nhai luôn miếng đào ngâm ở đáy ly, ăn kèm bánh phô mai nướng là combo bán chạy nhất.",
        ["tra-vai"] =
            "Ngọt thanh, hợp buổi chiều nóng. Dùng kèm bánh su kem lạnh.",
        ["tra-chanh-gia-tay"] =
            "Chua gắt, giải ngấy rất tốt sau bánh mì que pate.",
        ["tra-tac-mat-ong"] =
            "Ấm họng khi đang mệt — gọi ít đá để vị mật ong không bị loãng.",
        ["tra-oi-hong"] =
            "Chua ngọt cân bằng, hợp bánh tiramisu.",
        ["tra-dua-nhiet-doi"] =
            "Vị dứa gắt, uống thật lạnh mới đã. Kèm bánh phô mai nướng.",
        ["soda-dau-tay"] =
            "Uống ngay khi ga còn mạnh, để lâu là nhạt.",
        ["soda-viet-quat"] =
            "Chua nhẹ và nhiều ga, hợp sau bữa ăn nhiều dầu mỡ.",
        ["soda-chanh-bac-ha"] =
            "Mát lạnh, giải nhiệt tốt nhất trong ngày nắng. Kèm cookie socola.",

        // --- Trà & thảo mộc ---------------------------------------------------
        ["tra-o-long-nuong"] =
            "Uống nóng để mùi trà nướng bung hết, kèm bánh su kem hoặc cookie socola.",
        ["luc-tra-chanh"] =
            "Thanh và nhẹ, hợp uống kèm bữa trưa nhiều đạm.",
        ["tra-hoa-cuc-mat-ong"] =
            "Uống nóng trước khi ngủ cho dễ ngủ, kèm cookie socola nếu thèm ngọt.",
        ["tra-gung-mat-ong"] =
            "Uống thật nóng khi đau họng hoặc trời lạnh, nhấp từng ngụm nhỏ.",

        // --- Đá xay & sinh tố -------------------------------------------------
        ["sinh-to-xoai"] =
            "Đặc nên uống chậm, coi như một bữa phụ. Không cần thêm bánh.",
        ["sinh-to-dau"] =
            "Uống trong 15 phút đầu, để lâu là tách nước.",
        ["sinh-to-bo"] =
            "Rất no bụng — hợp thay bữa sáng hơn là uống kèm bánh.",
        ["sinh-to-viet-quat"] =
            "Chua nhẹ, hợp sau buổi tập hoặc bữa trưa nhẹ.",
        ["cacao-da-xay"] =
            "Ngọt đậm, chọn 70% đường là vừa miệng phần lớn khách.",
        ["matcha-da-xay"] =
            "Chát mát, ăn kèm bánh su kem cho cân vị.",
        ["cookies-cream-da-xay"] =
            "Ăn phần bánh quy nghiền trên mặt bằng thìa trước, rồi mới hút phần đá xay.",
        ["caramel-da-xay"] =
            "Ngọt gắt nhất menu — hợp khách thích đồ ngọt, uống kèm nước lọc.",

        // --- Matcha & cacao ---------------------------------------------------
        ["matcha-latte"] =
            "Uống nóng thì rõ mùi trà hơn, uống đá thì dịu và dễ vào. Kèm bánh su kem.",
        ["matcha-kem-cheese"] =
            "Nghiêng ly uống thẳng để lớp kem cheese mặn gặp vị chát của matcha.",
        ["cacao-nong"] =
            "Uống khi còn bốc khói, kèm cookie socola hoặc bánh tiramisu.",
        ["cacao-da"] =
            "Mát và đậm, hợp buổi chiều. Kèm croissant bơ.",
        ["socola-nong"] =
            "Ngọt ấm, hợp trời lạnh và hợp trẻ nhỏ. Kèm bánh su kem.",

        // --- Sữa chua ---------------------------------------------------------
        ["sua-chua-danh-da"] =
            "Chua mát, giải ngấy rất tốt sau món dầu mỡ hoặc bánh béo.",
        ["sua-chua-viet-quat"] =
            "Uống sau bữa ăn cho dễ tiêu, đừng để tan đá rồi mới uống.",
        ["sua-chua-nha-dam"] =
            "Nhai kỹ phần nha đam giòn ở đáy ly — đó là phần ngon nhất.",

        // --- Bánh & đồ ăn nhẹ (gợi ý ngược: bánh này hợp món uống nào) ---------
        ["croissant-bo"] =
            "Ăn nóng, chấm được cả cà phê sữa lẫn latte.",
        ["banh-tiramisu"] =
            "Ngọt đậm mùi cà phê — hợp nhất với americano hoặc trà ô long không đường.",
        ["banh-pho-mai-nuong"] =
            "Béo mặn nhẹ, ăn kèm trà đào cam sả hoặc cold brew cho đỡ ngán.",
        ["banh-su-kem"] =
            "Ăn lạnh, kèm trà nóng hoặc matcha latte.",
        ["cookie-socola"] =
            "Giòn và ngọt đậm, chấm sữa hoặc uống kèm espresso.",
        ["banh-mi-que-pate"] =
            "Ăn nóng kèm cà phê sữa đá — bữa sáng quen thuộc nhất của khách Việt."
    };

    /// <summary>Gợi ý thưởng thức của một món. Không có thì trả về null và giao diện ẩn khối này.</summary>
    public static string? PairingFor(string slug)
        => PairingBySlug.TryGetValue(slug, out var text) ? text : null;

    // ==========================================================================
    //  §4  MÓN & CÔNG THỨC ĐỊNH LƯỢNG
    //
    //  ColorPrimary = màu thân nước, ColorAccent = màu lớp kem hoặc lớp foam.
    //
    //  CHUẨN ĐỊNH LƯỢNG CỦA QUÁN (size M — size L tự nhân 1,4)
    //    · Một ly ≈ 500ml tính cả đá.
    //    · Cốt trà 150ml (ủ 6g lá / 100ml, xem §5). Trà uống thuần rót 180–200ml.
    //    · Sữa tươi 120–150ml. Sữa đặc 15–25ml.
    //    · Siro trái cây 20–30ml (mức 20–40ml tùy độ ngọt, quán chọn giữa).
    //    · Đá viên 150–180g cho đồ uống đá, 180–200g cho đồ đá xay.
    //    · Nước đường luôn là dòng TÙY CHỌN: kho trừ theo mức đường khách chọn
    //      (70% đường → trừ 70%, không đường → không trừ). Đá cũng vậy: "Ít đá",
    //      "Không đá", "Dùng nóng" đều được trừ đúng — xem RecipeService.
    //    · Trang trí (lát chanh, lá bạc hà, bột rắc mặt, hoa cúc khô) GHI VÀO
    //      CÔNG THỨC như mọi nguyên liệu khác, đánh dấu Optional để hết thì vẫn
    //      bán được. Không ghi thì cuối tháng kho hụt mà không ai biết vì sao.
    //    · Một nguyên liệu chỉ được xuất hiện MỘT dòng trong mỗi công thức
    //      (database có chỉ mục duy nhất). Vừa pha vừa rắc mặt thì cộng dồn
    //      vào cùng một dòng và ghi rõ trong Note.
    // ==========================================================================
    public static readonly ProductSpec[] Products =
    {
        // ======================================================================
        //  CÀ PHÊ
        //
        //  Espresso nền (Arabica, 18g = 2 shot) cho nhóm máy: americano, latte,
        //  cappuccino, caramel macchiato. Nhóm Việt (cà phê sữa đá, đen đá, bạc
        //  xỉu, muối, cốt dừa, kem trứng) giữ cốt PHIN Robusta — đó là thứ làm
        //  nên chữ "cà phê pha phin" trên bảng hiệu, espresso không thay được.
        // ======================================================================
        new("coffee", "Cà phê muối", "ca-phe-muoi",
            "Cà phê phin đậm, phủ lớp kem muối béo mặn đặc trưng xứ Huế.",
            35000, "#3D2415", "#F7F2E8", "best-seller,signature", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 40, Note: "Rót từ bình cốt phin đã ủ — tương đương một phin 20g"),
                new("DAI-CON-01",  20),
                new("SYR-SLT-01",  45, Note: "Đánh bông rồi rót lên mặt, KHÔNG khuấy"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            Featured: true, SortOrder: 1, PrepSeconds: 140),   // đánh lớp kem muối riêng

        new("coffee", "Cà phê kem trứng", "ca-phe-kem-trung",
            "Cà phê nóng phủ kem trứng đánh bông, thơm béo kiểu Hà Nội.",
            42000, "#2E1A0F", "#F3D68A", "signature", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 44),
                new("OTH-EGG-01",   1, Note: "Chỉ lấy lòng đỏ, bỏ lòng trắng"),
                new("DAI-CON-01",  25),
                new("DAI-WHIP-01", 20, Note: "Đánh cùng lòng đỏ tới khi bông cứng"),
                new("POW-CAC-01",   1, Optional: true, Note: "Trang trí: rắc một lớp mỏng lên mặt kem"),
                new("OTH-ICE-01", 150, Optional: true, Note: "CHỈ khi khách chọn dùng đá"),
                new("PKG-CUP-H",    1)
            },
            SortOrder: 2, PrepSeconds: 210),   // đánh kem trứng tại chỗ, lâu nhất menu

        new("coffee", "Bạc xỉu", "bac-xiu",
            "Nhiều sữa, ít cà phê — vị ngọt dịu quen thuộc.",
            32000, "#B08B60", "#F5EDE0", "best-seller", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 24, Note: "Ít cà phê hơn hẳn cà phê sữa đá"),
                new("DAI-CON-01",   30),
                new("DAI-MILK-01", 130, Note: "Đánh foam nếu khách dùng nóng"),
                new("OTH-ICE-01",  170, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 3),

        new("coffee", "Cà phê sữa đá", "ca-phe-sua-da",
            "Phin truyền thống, đậm và ngọt vừa.",
            30000, "#4A2C17", "#C89968", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 40, Note: "Cốt cà phê nền"),
                new("DAI-CON-01",  25),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            Featured: true, SortOrder: 4),

        new("coffee", "Cà phê đen đá", "ca-phe-den-da",
            "Phin đậm, chỉ thêm chút đường — vị cà phê nguyên bản.",
            25000, "#241610", "#4A3020", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 44),
                new("SYR-SUG-01",  15, Optional: true, Note: "Trừ theo mức đường khách chọn"),
                new("OTH-ICE-01", 200, Optional: true),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 5),

        new("coffee", "Cold brew", "cold-brew",
            "Ủ lạnh 18 tiếng, vị trong và chua thanh, ít đắng.",
            45000, "#5A3A22", "#8B6242", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-COF-CLD", 196, Note: "Rót từ mẻ cold brew đã ủ, không pha loãng thêm"),
                new("SYR-SUG-01",   10, Optional: true),
                new("OTH-ICE-01",  200, Optional: true),
                new("PKG-CUP-L",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 6, PrepSeconds: 45),    // đã ủ sẵn, tại quầy chỉ rót

        new("coffee", "Americano", "americano",
            "Espresso pha loãng, không sữa không đường.",
            38000, "#4A3020", "#7A5A42", "", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("COF-ARA-01",  18, Note: "2 shot espresso, thêm 200ml nước nóng (nước lọc nếu dùng đá)"),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 7),

        new("coffee", "Espresso", "espresso",
            "Một shot đôi, uống ngay tại quầy khi còn nóng.",
            30000, "#31200F", "#A6784E", "", ServeStyle.Hot,
            new RecipeLine[]
            {
                new("COF-ARA-01", 18, Note: "2 shot, chiết 36ml trong 25–30 giây"),
                new("PKG-CUP-H",   1)
            },
            SortOrder: 8, PrepSeconds: 60),

        new("coffee", "Latte", "latte",
            "Espresso và sữa tươi đánh bông mịn.",
            45000, "#B99A78", "#F5EDE0", "", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("COF-ARA-01",   18, Note: "2 shot espresso nền"),
                new("DAI-MILK-01", 150, Note: "Đánh foam mịn tới 60°C nếu dùng nóng"),
                new("SYR-SUG-01",   15, Optional: true),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 9),

        new("coffee", "Cappuccino", "cappuccino",
            "Một phần espresso, một phần sữa nóng, một phần bọt sữa.",
            45000, "#A8825C", "#F7EFE2", "", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("COF-ARA-01",   18),
                new("DAI-MILK-01", 150, Note: "Đánh bọt tới 60–65°C, bọt dày 1cm"),
                new("SYR-SUG-01",   10, Optional: true),
                new("POW-CAC-01",    1, Optional: true, Note: "Trang trí: rắc bột cacao lên foam"),
                new("OTH-ICE-01",  120, Optional: true, Note: "CHỈ khi khách chọn dùng đá"),
                new("PKG-CUP-H",     1)
            },
            SortOrder: 10, PrepSeconds: 120),

        new("coffee", "Cà phê cốt dừa", "ca-phe-cot-dua",
            "Cà phê phin đánh cùng nước cốt dừa, béo và mát.",
            42000, "#6B4A32", "#FAF7F0", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-COF-PHI", 40),
                new("SYR-COC-01",  60, Note: "Đánh cùng đá cho sánh trước khi rót cà phê"),
                new("DAI-CON-01",  15),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 11, PrepSeconds: 150),

        new("coffee", "Caramel macchiato", "caramel-macchiato",
            "Sữa tươi, espresso rót nổi và một đường caramel vắt ngang.",
            52000, "#9E7648", "#E8C89A", "new", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("COF-ARA-01",   18, Note: "Rót nổi lên trên lớp sữa, không khuấy"),
                new("DAI-MILK-01", 150),
                new("SYR-CAR-01",   25, Note: "15ml trộn dưới đáy, 10ml vẽ caramel drizzle trên mặt"),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-L",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 12, PrepSeconds: 130),

        // ======================================================================
        //  TRÀ SỮA
        //
        //  Khung chung: 150ml cốt trà + 130ml sữa tươi + 20ml sữa đặc + phần
        //  hương vị + 150g đá ≈ 500ml. Topping (trân châu đen, thạch trái cây…)
        //  khách chọn thêm ở nhóm Topping, có công thức riêng.
        // ======================================================================
        new("milktea", "Trà sữa trân châu đường đen", "tra-sua-tran-chau-duong-den",
            "Trân châu nấu đường đen, ủ nóng liên tục để luôn dẻo.",
            45000, "#8E6647", "#EFE3D2", "best-seller,signature", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150, Note: "Rót từ bình cốt hồng trà — kiểm giờ ủ ghi trên bình"),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   20),
                new("POW-CRE-01",   10, Note: "Kem béo: hòa với 20ml nước nóng trước khi pha"),
                new("SYR-BRW-01",   20, Note: "Vẽ vằn đường nâu quanh thành ly trước khi cho đá"),
                new("TOP-BOB-01",   60, Note: "Trân châu đen đường nâu, khoảng 2 muỗng canh đầy"),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            Featured: true, SortOrder: 1, AllowTopping: true),

        new("milktea", "Trà sữa truyền thống", "tra-sua-truyen-thong",
            "Hồng trà ủ đậm, sữa tươi và sữa đặc — vị trà sữa quen thuộc.",
            35000, "#B59570", "#F0E4D2", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   25),
                new("SYR-SUG-01",   20, Optional: true),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 2, AllowTopping: true),

        new("milktea", "Trà sữa ô long", "tra-sua-o-long",
            "Ô long rang, hậu vị ngọt nhẹ, ít gắt.",
            42000, "#A98B62", "#F0E6D6", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-OOL", 150),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   20),
                new("SYR-SUG-01",   15, Optional: true),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 3, AllowTopping: true),

        new("milktea", "Trà sữa matcha", "tra-sua-matcha",
            "Matcha Uji nguyên chất trên nền ô long nhẹ.",
            50000, "#8FAE6B", "#EFF3E4", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-OOL",  80, Note: "Nền trà nhẹ — nhiều hơn sẽ lấn vị matcha"),
                new("POW-MAT-01",    4, Note: "Rây bột rồi đánh chasen với 40ml nước 80°C"),
                new("DAI-MILK-01", 140),
                new("DAI-CON-01",   20),
                new("SYR-SUG-01",   10, Optional: true),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 4, AllowTopping: true),

        new("milktea", "Hồng trà sữa kem cheese", "hong-tra-sua-kem-cheese",
            "Lớp kem cheese mặn mịn phủ trên hồng trà.",
            52000, "#B58358", "#FAF0DC", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("DAI-MILK-01", 120),
                new("DAI-CON-01",   15),
                new("DAI-CHE-01",   40, Note: "Đánh bông, rót lên mặt, không khuấy"),
                new("SYR-SUG-01",   15, Optional: true),
                new("OTH-ICE-01",  140, Optional: true),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 5, AllowTopping: true),

        new("milktea", "Trà sữa khoai môn", "tra-sua-khoai-mon",
            "Vị khoai môn bùi, màu tím nhạt tự nhiên.",
            46000, "#B4A0C4", "#EDE6F2", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("POW-TAR-01",   20, Note: "≈ 2 muỗng cà phê đầy, hòa tan trong cốt trà ấm"),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   20),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 6, AllowTopping: true),

        new("milktea", "Trà sữa dâu", "tra-sua-dau",
            "Nền ô long nhẹ cho dậy mùi dâu, màu hồng phấn.",
            46000, "#E0A8B4", "#F8DDE2", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-OOL", 150),
                new("POW-STR-01",   20, Note: "≈ 2 muỗng cà phê đầy, hòa tan trong cốt trà ấm"),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   20),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 7, AllowTopping: true),

        new("milktea", "Trà sữa socola", "tra-sua-socola",
            "Hồng trà nhẹ nền, bột cacao đậm và sốt socola phủ thành ly.",
            45000, "#5B3B2A", "#C79C74", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("POW-CAC-01",   12, Note: "Hòa bột cacao với cốt trà nóng cho tan hết"),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   20),
                new("SYR-CHO-01",   10, Note: "Vẽ quanh thành ly trước khi rót"),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 8, AllowTopping: true),

        new("milktea", "Trà sữa thái xanh", "tra-sua-thai-xanh",
            "Bột trà thái xanh trên nền ô long, vị Thái quen thuộc.",
            42000, "#9FBE7A", "#EDF3E2", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-OOL", 150),
                new("POW-THG-01",   15, Note: "Hòa tan trong cốt trà nóng rồi lọc qua rây"),
                new("DAI-MILK-01", 130),
                new("DAI-CON-01",   25),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 9, AllowTopping: true),

        // ======================================================================
        //  TRÀ TRÁI CÂY & SODA
        //
        //  Khung chung: 150ml cốt trà (hoặc 200ml soda) + 20–30ml siro trái cây
        //  + nước cốt chanh/cam + 180g đá. Trang trí trái cây tươi và lá bạc hà.
        // ======================================================================
        new("fruit", "Trà đào cam sả", "tra-dao-cam-sa",
            "Đào ngâm, cam vàng và sả tươi đập dập, ủ cùng trà lài.",
            45000, "#E8944A", "#F7D9B4", "best-seller,signature", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-JAS", 150, Note: "Rót từ bình cốt trà lài đã ủ"),
                new("SYR-PEA-01",  25, Note: "Siro trong hũ đào ngâm"),
                new("FRU-PEA-01",  60, Note: "Trang trí: 2 miếng đào, mỗi miếng ≈ 30g, thái lát"),
                new("FRU-ORA-01", 0.2, Note: "2 lát cam vàng: 1 lát vắt nước, 1 lát trang trí"),
                new("FRU-LGR-01",  12, Note: "1 cây sả cắt khúc, đập dập cho ra tinh dầu"),
                new("FRU-LEM-01", 0.2, Note: "Vắt lấy nước cốt, bỏ hạt"),
                new("SYR-SUG-01",  10, Optional: true, Note: "Trừ theo mức đường khách chọn"),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            Featured: true, SortOrder: 1, PrepSeconds: 150, AllowTopping: true),

        new("fruit", "Trà vải", "tra-vai",
            "Vải ngâm nguyên trái, trà lài ướp hương.",
            43000, "#F0D3D8", "#FAEAEC", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-JAS", 150),
                new("SYR-LYC-01",  25, Note: "Nước ngâm trong hũ vải"),
                new("FRU-LYC-01",  56, Note: "Trang trí: 4 trái vải ngâm, mỗi trái ≈ 14g"),
                new("FRU-LEM-01", 0.2),
                new("SYR-SUG-01",  10, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 2, AllowTopping: true),

        new("fruit", "Trà chanh giã tay", "tra-chanh-gia-tay",
            "Chanh tươi giã cùng đá, chua mát, giải nhiệt.",
            35000, "#D8E04A", "#EEF2AE", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("FRU-LEM-01", 1.5, Note: "1 quả vắt lấy nước, nửa quả thái lát giã cùng đá"),
                new("SYR-SUG-01",  28, Optional: true),
                new("OTH-ICE-01", 200, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 3, AllowTopping: true),

        new("fruit", "Trà tắc mật ong", "tra-tac-mat-ong",
            "Tắc vắt tươi, mật ong rừng, hồng trà ủ nhạt.",
            32000, "#E8A427", "#F7DFA8", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("FRU-KUM-01",   3, Note: "3 quả tắc: 2 vắt nước cốt, 1 bổ đôi trang trí"),
                new("SYR-HON-01",  25),
                new("SYR-SUG-01",  10, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 4, AllowTopping: true),

        new("fruit", "Trà ổi hồng", "tra-oi-hong",
            "Ổi ruột hồng ép lấy nước, dằm cùng trà lài.",
            45000, "#E68A8A", "#F7D2D2", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-JAS", 150),
                new("SYR-GUA-01",  20),
                new("FRU-GUA-01",  60, Note: "50g ép lấy nước, 10g cắt miếng trang trí"),
                new("FRU-LEM-01", 0.2),
                new("SYR-SUG-01",  10, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 5, AllowTopping: true),

        new("fruit", "Trà dứa nhiệt đới", "tra-dua-nhiet-doi",
            "Dứa tươi cắt hạt lựu, hồng trà và một chút chanh.",
            45000, "#F2C744", "#FBEBB8", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-BLK", 150),
                new("SYR-PIN-01",  20),
                new("FRU-PIN-01",  70, Note: "≈ 1/10 quả dứa đã gọt: 60g hạt lựu, 10g miếng trang trí"),
                new("FRU-LEM-01", 0.2),
                new("SYR-SUG-01",  10, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 6, AllowTopping: true),

        new("fruit", "Soda dâu tây", "soda-dau-tay",
            "Dâu tây tươi dằm, soda mát lạnh.",
            48000, "#D63B3B", "#F5B8B8", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("SYR-STR-01",  30),
                new("FRU-STR-01",  30, Note: "2 quả dằm dưới đáy ly + 1 quả bổ đôi trang trí"),
                new("FRU-LEM-01", 0.2, Note: "Vắt lấy nước cốt"),
                new("OTH-SOD-01", 200, Note: "Rót nghiêng ly để giữ ga"),
                new("SYR-SUG-01",  10, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 150, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 7, AllowTopping: true),

        new("fruit", "Soda việt quất", "soda-viet-quat",
            "Việt quất dằm tím sẫm, soda và vỏ chanh bào.",
            50000, "#4A4A8C", "#B9B9DC", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("SYR-BLU-01",  30),
                new("FRU-BLU-01",  20, Note: "Rã đông trước 10 phút; dằm 15g, 5g thả mặt trang trí"),
                new("FRU-LEM-01", 0.2, Note: "Vắt nước cốt, bào ít vỏ lên mặt"),
                new("OTH-SOD-01", 200),
                new("SYR-SUG-01",   5, Optional: true),
                new("FRU-MIN-01", 0.5, Optional: true, Note: "Trang trí: 1 ngọn bạc hà"),
                new("OTH-ICE-01", 150, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 8, AllowTopping: true),

        new("fruit", "Soda chanh bạc hà", "soda-chanh-bac-ha",
            "Chanh và lá bạc hà giã nhẹ, soda đầy ga.",
            42000, "#8FC98F", "#DCEFD9", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("SYR-MIN-01",  25),
                new("FRU-LEM-01", 0.6, Note: "Nửa quả vắt nước cốt, 1 lát trang trí"),
                new("FRU-MIN-01",   5, Note: "≈ 10 lá, vỗ nhẹ cho dậy mùi, KHÔNG giã nát"),
                new("OTH-SOD-01", 200),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 9, AllowTopping: true),

        // ======================================================================
        //  TRÀ & THẢO MỘC
        //
        //  Khung chung: cốt trà xanh / ô long + nước cốt chanh hoặc gừng + mật
        //  ong + đá. Trang trí lát chanh, lát gừng, hoa cúc khô.
        // ======================================================================
        new("tea", "Trà ô long nướng", "tra-o-long-nuong",
            "Ô long rang lửa nhỏ, uống không đường vẫn ngọt hậu.",
            35000, "#9A7B4F", "#D9C4A0", "", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("PRE-TEA-OOL", 180, Note: "Trà ô long hãm — món uống thuần nên rót nhiều hơn trà sữa"),
                new("SYR-SUG-01",  20, Optional: true),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 1, AllowTopping: true),

        new("tea", "Lục trà chanh", "luc-tra-chanh",
            "Lục trà ủ lạnh, vắt chanh tươi, mật ong, không sữa.",
            32000, "#B7CE6E", "#E6EFC6", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("PRE-TEA-GRN", 180),
                new("FRU-LEM-01", 0.6, Note: "Nửa quả vắt nước cốt, 1 lát trang trí"),
                new("SYR-HON-01",  20),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 180, Optional: true),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 2, AllowTopping: true),

        new("tea", "Trà hoa cúc mật ong", "tra-hoa-cuc-mat-ong",
            "Hoa cúc khô hãm cùng lục trà, thêm mật ong — uống buổi tối không mất ngủ.",
            38000, "#E4C558", "#F7EAC0", "new", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("PRE-TEA-CHA", 120, Note: "Rót từ bình cốt hoa cúc, hâm lại tới 70°C"),
                new("PRE-TEA-GRN",  80, Note: "Nền trà xanh"),
                new("SYR-HON-01",  25, Note: "Chờ trà nguội dưới 60°C mới cho mật ong"),
                new("TEA-CHA-01",   1, Optional: true, Note: "Trang trí: 3–4 bông hoa cúc khô thả mặt"),
                new("OTH-ICE-01", 150, Optional: true, Note: "CHỈ khi khách chọn dùng đá"),
                new("PKG-CUP-H",    1)
            },
            SortOrder: 3, PrepSeconds: 120, AllowTopping: true),

        new("tea", "Trà gừng mật ong", "tra-gung-mat-ong",
            "Gừng tươi đập dập hãm cùng lục trà, mật ong và chanh — ấm bụng ngày mưa.",
            38000, "#E0C892", "#F5E7C8", "", ServeStyle.Hot,
            new RecipeLine[]
            {
                new("PRE-TEA-GRN", 150, Note: "Nền trà xanh, hâm nóng"),
                new("FRU-GIN-01",  14, Note: "3 lát dày đập dập hãm 5 phút + 1 lát mỏng trang trí"),
                new("SYR-HON-01",  25),
                new("FRU-LEM-01", 0.2, Note: "Vắt nước cốt"),
                new("PKG-CUP-H",    1)
            },
            SortOrder: 4, PrepSeconds: 130, AllowTopping: true),

        // ======================================================================
        //  ĐÁ XAY & SINH TỐ
        //
        //  Khung chung: 120–150ml sữa tươi + 180–200g đá xay + siro hoặc trái
        //  cây tươi + kem béo. Trang trí kem tươi và trái cây tươi.
        // ======================================================================
        new("blended", "Sinh tố xoài", "sinh-to-xoai",
            "Xoài cát tươi xay cùng sữa, không dùng siro.",
            50000, "#F2B33D", "#FCEBC4", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("FRU-MAN-01", 160, Note: "≈ 1 quả xoài cát nhỏ: 140g xay, 20g hạt lựu trang trí"),
                new("DAI-MILK-01", 120),
                new("DAI-WHIP-01", 20, Note: "Kem béo xay cùng cho sánh"),
                new("SYR-SUG-01",  15, Optional: true),
                new("OTH-ICE-01", 180),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            Featured: true, SortOrder: 1, AllowTopping: true),

        new("blended", "Sinh tố dâu", "sinh-to-dau",
            "Dâu tây tươi xay mịn cùng sữa tươi.",
            55000, "#E05C6E", "#F8CDD4", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                // 90g chứ không phải 150g: dâu tây 180đ/g là nguyên liệu đắt nhất
                // nhóm trái cây. 90g dâu tươi cộng 15ml siro dâu cho màu và độ
                // ngọt tương đương mà giá vốn không vượt mức.
                new("FRU-STR-01",  90, Note: "≈ 7 quả: 6 quả xay, 1 quả bổ đôi trang trí"),
                new("SYR-STR-01",  15),
                new("DAI-MILK-01", 120),
                new("DAI-WHIP-01", 10, Note: "Kem béo ít thôi — trái cây đã đắt, 10ml đủ sánh"),
                new("SYR-SUG-01",  15, Optional: true),
                new("OTH-ICE-01", 180),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 2, AllowTopping: true),

        new("blended", "Sinh tố bơ", "sinh-to-bo",
            "Bơ sáp Đắk Lắk xay đặc, sữa đặc ngọt vừa.",
            55000, "#7A8B4A", "#DDE4C0", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("FRU-AVO-01", 150, Note: "≈ 1 quả bơ sáp vừa, bỏ vỏ và hạt"),
                new("DAI-CON-01",  25),
                new("DAI-MILK-01", 120),
                new("DAI-WHIP-01", 15),
                new("OTH-ICE-01", 180),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 3, AllowTopping: true),

        new("blended", "Sinh tố việt quất", "sinh-to-viet-quat",
            "Việt quất và sữa chua xay lạnh, chua ngọt cân bằng.",
            60000, "#4A4A8C", "#C4C4E0", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                // 50g: việt quất đông lạnh 320đ/g là nguyên liệu đắt thứ nhì cả
                // kho, chỉ sau bột matcha. Siro việt quất bù phần màu và vị.
                new("FRU-BLU-01",  45, Note: "40g xay, 5g thả mặt trang trí"),
                new("SYR-BLU-01",  15),
                new("DAI-YOG-01",  50),
                new("DAI-MILK-01", 100),
                new("DAI-WHIP-01", 10, Note: "Kem béo ít thôi — trái cây đã đắt, 10ml đủ sánh"),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 180),
                new("PKG-CUP-L",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 4, AllowTopping: true),

        new("blended", "Cacao đá xay", "cacao-da-xay",
            "Cacao nguyên chất xay đá, phủ kem tươi.",
            52000, "#4E3225", "#FBF6EC", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("POW-CAC-01",  19, Note: "18g xay cùng, 1g rắc lên kem"),
                new("DAI-MILK-01", 150),
                new("SYR-SUG-01",   25, Optional: true),
                new("DAI-WHIP-01",  30, Note: "Kem béo đánh bông, bóp lên mặt sau khi xay"),
                new("OTH-ICE-01",  200),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 5, AllowTopping: true),

        new("blended", "Matcha đá xay", "matcha-da-xay",
            "Matcha xay đá, phủ kem cheese mặn nhẹ.",
            58000, "#8FAE6B", "#FAF0DC", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("POW-MAT-01",    6, Note: "5,5g xay cùng, 0,5g rắc lên lớp kem"),
                new("DAI-MILK-01", 150),
                new("SYR-SUG-01",   25, Optional: true),
                new("DAI-CHE-01",   35),
                new("OTH-ICE-01",  200),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 6, AllowTopping: true),

        new("blended", "Cookies & cream đá xay", "cookies-cream-da-xay",
            "Bánh quy socola nghiền xay cùng sữa, phủ kem tươi.",
            58000, "#4A4245", "#F2ECE2", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("TOP-OREO-01",  35, Note: "25g xay cùng, 10g rắc lên mặt"),
                new("DAI-MILK-01", 150),
                new("DAI-WHIP-01",  30, Note: "Kem tươi phủ mặt"),
                new("SYR-SUG-01",   20, Optional: true),
                new("OTH-ICE-01",  200),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 7, AllowTopping: true),

        new("blended", "Caramel đá xay", "caramel-da-xay",
            "Espresso, sữa và sốt caramel xay lạnh.",
            55000, "#B5772E", "#F2DCB8", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("COF-ARA-01",   15),
                new("DAI-MILK-01", 150),
                new("SYR-CAR-01",   30, Note: "25ml xay cùng, 5ml vẽ drizzle trên kem"),
                new("DAI-WHIP-01",  25, Note: "Kem tươi phủ mặt"),
                new("SYR-SUG-01",   10, Optional: true),
                new("OTH-ICE-01",  200),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 8, AllowTopping: true),

        // ======================================================================
        //  MATCHA & CACAO
        //
        //  Khung chung: bột matcha / cacao + 150ml sữa tươi + nước đường + kem
        //  béo hoặc kem cheese. Trang trí kem tươi, rắc bột matcha / cacao.
        // ======================================================================
        new("matcha", "Matcha latte", "matcha-latte",
            "Matcha Uji đánh tay, sữa tươi nguyên kem.",
            48000, "#7CA24A", "#EFF3E4", "best-seller", ServeStyle.HotOrIced,
            new RecipeLine[]
            {
                new("POW-MAT-01",  5.5, Note: "5g rây rồi đánh chasen với 40ml nước 80°C, 0,5g rắc mặt"),
                new("DAI-MILK-01", 150),
                new("SYR-SUG-01",   20, Optional: true),
                new("OTH-ICE-01",  150, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            Featured: true, SortOrder: 1),

        new("matcha", "Matcha kem cheese", "matcha-kem-cheese",
            "Matcha đậm phủ lớp kem cheese mặn, uống không khuấy.",
            55000, "#7CA24A", "#FAF0DC", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("POW-MAT-01",  5.5, Note: "5g đánh chasen, 0,5g rắc lên lớp kem cheese"),
                new("DAI-MILK-01", 150),
                new("DAI-CHE-01",   40),
                new("SYR-SUG-01",   20, Optional: true),
                new("OTH-ICE-01",  140, Optional: true),
                new("PKG-CUP-L",     1)
            },
            SortOrder: 2),

        new("matcha", "Cacao nóng", "cacao-nong",
            "Cacao nguyên chất đánh cùng sữa nóng, phủ kem tươi.",
            42000, "#5C3A21", "#C9A57B", "", ServeStyle.Hot,
            new RecipeLine[]
            {
                new("POW-CAC-01",   17, Note: "16g đánh tan với 30ml nước sôi, 1g rắc lên kem"),
                new("DAI-MILK-01", 150, Note: "Hâm tới 65°C, không đun sôi kẻo sữa tách"),
                new("SYR-SUG-01",   20, Optional: true),
                new("DAI-WHIP-01",  15, Note: "Kem tươi phủ mặt"),
                new("PKG-CUP-H",     1)
            },
            SortOrder: 3),

        new("matcha", "Cacao đá", "cacao-da",
            "Cacao pha đậm, sữa tươi, kem béo và đá — bản lạnh của cacao nóng.",
            42000, "#4E3225", "#B8916A", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("POW-CAC-01",   16, Note: "Đánh tan với 30ml nước sôi trước khi cho đá"),
                new("DAI-MILK-01", 150),
                new("SYR-SUG-01",   25, Optional: true),
                new("DAI-WHIP-01",  15, Note: "Kem béo khuấy cùng cho sánh"),
                new("OTH-ICE-01",  160, Optional: true),
                new("PKG-CUP-M",     1),
                new("PKG-STR-01",    1)
            },
            SortOrder: 4),

        new("matcha", "Socola nóng", "socola-nong",
            "Sốt socola đen đánh cùng sữa nóng, phủ kem tươi, rắc bột cacao.",
            45000, "#3E2418", "#B08050", "new", ServeStyle.Hot,
            new RecipeLine[]
            {
                new("SYR-CHO-01",   30),
                new("POW-CAC-01",   10, Note: "8g đánh cùng sữa, 2g rắc mặt trước khi đậy nắp"),
                new("DAI-MILK-01", 150),
                new("SYR-SUG-01",   10, Optional: true),
                new("DAI-WHIP-01",  20, Note: "Kem tươi phủ mặt"),
                new("PKG-CUP-H",     1)
            },
            SortOrder: 5),

        // ======================================================================
        //  SỮA CHUA
        //
        //  Khung chung: 130g sữa chua + siro trái cây / đường + đá xay.
        //  Trang trí nha đam, trái cây tươi.
        // ======================================================================
        new("yogurt", "Sữa chua đánh đá", "sua-chua-danh-da",
            "Sữa chua nhà làm đánh cùng đá bào, chua mát.",
            32000, "#FDFBF5", "#E8E0D0", "best-seller", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("DAI-YOG-01", 130, Note: "≈ 1 hũ rưỡi sữa chua"),
                new("DAI-CON-01",  20),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 180, Note: "Đá xay"),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 1, AllowTopping: true),

        new("yogurt", "Sữa chua việt quất", "sua-chua-viet-quat",
            "Sữa chua đánh đá, việt quất dằm phủ mặt.",
            48000, "#8C7AAE", "#DCD2EA", "new", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("DAI-YOG-01", 130),
                new("SYR-BLU-01",  25),
                new("FRU-BLU-01",  20, Note: "Trang trí: dằm sơ, phủ mặt"),
                new("SYR-SUG-01",  10, Optional: true),
                new("OTH-ICE-01", 160, Note: "Đá xay"),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 2, AllowTopping: true),

        new("yogurt", "Sữa chua nha đam", "sua-chua-nha-dam",
            "Sữa chua với thạch nha đam giòn, ít ngọt.",
            40000, "#EAF0DC", "#D6E2BE", "", ServeStyle.Iced,
            new RecipeLine[]
            {
                new("DAI-YOG-01", 130),
                new("TOP-ALO-01",  50, Note: "40g trộn cùng, 10g trang trí mặt — rửa nước lạnh cho hết nhớt"),
                new("SYR-SUG-01",  20, Optional: true),
                new("OTH-ICE-01", 160, Note: "Đá xay"),
                new("PKG-CUP-M",    1),
                new("PKG-STR-01",   1)
            },
            SortOrder: 3, AllowTopping: true),

        // ======================================================================
        //  BÁNH & ĐỒ ĂN NHẸ
        //
        //  Nhóm này KHÔNG có size, KHÔNG mức đường, KHÔNG mức đá và KHÔNG topping
        //  (ServeStyle.Food). Công thức chỉ gồm chính cái bánh và bao bì — nhưng
        //  vẫn PHẢI có công thức, vì đó là thứ khiến kho tự trừ một cái bánh mỗi
        //  khi bán, thay vì tới cuối ngày mới đếm tay.
        // ======================================================================
        new("bakery", "Croissant bơ", "croissant-bo",
            "Bánh sừng bò nướng lại giòn vỏ, ruột xốp thơm bơ.",
            30000, "#D8A860", "#F2DFC0", "best-seller", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-CRO-01", 1, Note: "Nướng lại 4 phút ở 160°C trước khi phục vụ"),
                new("PKG-BOX-01", 1),
                new("PKG-NAP-01", 1)
            },
            SortOrder: 1),

        new("bakery", "Bánh tiramisu", "banh-tiramisu",
            "Cốt cà phê ẩm, lớp mascarpone mềm, rắc cacao.",
            45000, "#7A5230", "#E8D8C0", "signature", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-TIR-01", 1, Note: "Để tủ mát, lấy ra trước 5 phút cho bớt cứng"),
                new("PKG-BOX-01", 1),
                new("PKG-NAP-01", 1)
            },
            Featured: true, SortOrder: 2),

        new("bakery", "Bánh phô mai nướng", "banh-pho-mai-nuong",
            "Phô mai nướng mặt cháy nhẹ, béo vừa, không ngán.",
            42000, "#EAC77A", "#F7E8C4", "", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-CHE-01", 1),
                new("PKG-BOX-01", 1),
                new("PKG-NAP-01", 1)
            },
            SortOrder: 3),

        new("bakery", "Bánh su kem", "banh-su-kem",
            "Vỏ su giòn nhẹ, nhân kem trứng lạnh.",
            15000, "#E8D3AE", "#F7ECD8", "", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-SUK-01", 1),
                new("PKG-BOX-01", 1)
            },
            SortOrder: 4),

        new("bakery", "Cookie socola", "cookie-socola",
            "Cookie nướng mềm giữa, socola chip tan trong miệng.",
            18000, "#8A5A32", "#D8B88E", "", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-COO-01", 1),
                new("PKG-BOX-01", 1)
            },
            SortOrder: 5),

        new("bakery", "Bánh mì que pate", "banh-mi-que-pate",
            "Bánh mì que Hải Phòng, pate gan béo và chút tương ớt.",
            15000, "#C8965A", "#E8CFA8", "", ServeStyle.Food,
            new RecipeLine[]
            {
                new("BAK-BMQ-01", 1, Note: "Nướng lại 2 phút cho vỏ giòn"),
                new("PKG-NAP-01", 1)
            },
            SortOrder: 6)
    };

    // ==========================================================================
    //  §5  CÔNG THỨC SƠ CHẾ — MỘT MẺ, KHÔNG PHẢI MỘT LY
    //
    //  ĐÂY LÀ BƯỚC ĐỨNG TRƯỚC BÁN HÀNG.
    //
    //  Nhân viên bấm "Ủ hồng trà" → kho trừ 80g LÁ hồng trà, đồng thời sinh một
    //  bình 2000ml CỐT hồng trà có hạn 6 tiếng. Bán một ly trà sữa thì trừ vào
    //  bình cốt đó, không trừ vào lá trà nữa.
    //
    //  VÌ SAO PHẢI TÁCH HAI BƯỚC
    //  Ngoài đời không ai cân 8g lá trà cho từng ly. Ghi công thức ly theo gram
    //  lá trà thì hệ thống nói "còn 500g trà = còn 62 ly" trong khi bình cốt đã
    //  cạn và thực tế không pha được ly nào. Tách ra thì kho phản ánh đúng thứ
    //  đang có thật trên quầy, và lượng cốt đổ đi cuối ca cũng có chỗ để ghi.
    //
    //  QUY TẮC ĐẶT SỐ — SAI LÀ LỆCH GIÁ VỐN TOÀN BỘ THỰC ĐƠN
    //
    //  1. TỶ LỆ QUY ĐỔI PHẢI KHỚP GIỮA MẺ VÀ LY.
    //     Hồng trà: 120g cho 2000ml, tức 6g ↔ 100ml. Một ly trà sữa rót 150ml
    //     cốt là đã dùng 9g lá. Đổi tỷ lệ ủ thì phải đổi luôn đơn giá ước tính
    //     của cốt trà ở §1 (giá lá × 0,06), không thì giá vốn gợi ý bị lệch.
    //
    //  2. SẢN LƯỢNG LÀ LƯỢNG SAU CÙNG RÓT VÀO BÌNH, không phải lượng nước đổ vào
    //     nồi. Lá trà ngậm nước, bã giữ lại vài trăm ml. Ghi lượng nước đổ vào
    //     thì kho lúc nào cũng dư ảo.
    //
    //  3. HẠN DÙNG TÍNH BẰNG GIỜ. Cốt trà 6 tiếng, cà phê phin 12 tiếng, cold
    //     brew 7 ngày. Đây là con số quyết định lúc nào món hiện "chưa ủ trà".
    // ==========================================================================

    /// <summary>Một dòng nguyên liệu thô trong công thức sơ chế, tính cho MỘT MẺ.</summary>
    public record PrepLine(string Sku, double Quantity, string? Note = null);

    /// <summary>
    /// Định nghĩa một mẻ sơ chế.
    /// <list type="bullet">
    /// <item><c>Code</c> — khóa định danh, MenuSync dựa vào nó để biết đã có hay chưa.</item>
    /// <item><c>OutputSku</c> — bán thành phẩm sinh ra, phải có <c>Prepared: true</c>.</item>
    /// <item><c>OutputQuantity</c> — sản lượng một mẻ, SAU hao hụt.</item>
    /// <item><c>ShelfLifeHours</c> — hạn dùng của mẻ, tính bằng giờ.</item>
    /// </list>
    /// </summary>
    public record PrepSpec(
        string Code,
        string Name,
        string OutputSku,
        double OutputQuantity,
        int ShelfLifeHours,
        int PrepMinutes,
        string Instructions,
        PrepLine[] Inputs,
        int SortOrder = 0);

    public static readonly PrepSpec[] PrepRecipes =
    {
        // ---- TRÀ ------------------------------------------------------------
        // Tỷ lệ chuẩn của quán: 6g lá trà cho 100ml cốt (khoảng giữa của mức
        // 5–7g / 100ml nước sôi). Ủ đậm vì cốt còn được pha thêm sữa, siro và
        // đá — pha loãng từ đầu thì tới ly đã nhạt thếch.
        //
        // Lá trà ngậm nước khoảng 2,5 lần khối lượng của nó, nên lượng nước đổ
        // vào nồi phải nhiều hơn sản lượng mẻ đúng chừng đó.
        new("PREP-TEA-BLK", "Ủ hồng trà", "PRE-TEA-BLK", 2000, 6, 12,
            "Tráng ấm bằng nước sôi. Ủ 120g hồng trà trong 2,3 lít nước 95°C đúng 6 phút "
          + "rồi LỌC BỎ BÃ NGAY — để bã trong bình thêm 5 phút là cả mẻ chát, không cứu được. "
          + "Bã ngậm khoảng 300ml nên rót ra còn 2 lít. Ghi giờ ủ lên bình.",
            new PrepLine[] { new("TEA-BLK-01", 120, "6g / 100ml — cân bằng cân, đừng ước lượng bằng muỗng") },
            SortOrder: 1),

        new("PREP-TEA-OOL", "Ủ trà ô long", "PRE-TEA-OOL", 1500, 6, 12,
            "Ủ 90g ô long trong 1,75 lít nước 95°C, 5 phút. Ô long chịu nhiệt cao hơn hồng trà "
          + "nhưng ủ quá 6 phút thì mất hậu ngọt. Lọc bã ngay.",
            new PrepLine[] { new("TEA-OOL-01", 90, "6g / 100ml") },
            SortOrder: 2),

        new("PREP-TEA-JAS", "Ủ trà lài", "PRE-TEA-JAS", 1500, 6, 12,
            "Ủ 90g trà lài trong 1,75 lít nước 90°C, 5 phút. Nước sôi 100°C làm bay hết hương lài — "
          + "chờ nước nguội bớt rồi mới rót.",
            new PrepLine[] { new("TEA-JAS-01", 90, "6g / 100ml") },
            SortOrder: 3),

        new("PREP-TEA-GRN", "Ủ lục trà", "PRE-TEA-GRN", 1500, 5, 12,
            "Ủ 90g lục trà trong 1,75 lít nước 80°C, 4 phút. Lục trà là loại dễ chát nhất: "
          + "nóng quá hoặc lâu quá đều hỏng. Hạn ngắn hơn các loại khác nên ủ vừa đủ dùng.",
            new PrepLine[] { new("TEA-GRN-01", 90, "6g / 100ml") },
            SortOrder: 4),

        new("PREP-TEA-CHA", "Hãm hoa cúc", "PRE-TEA-CHA", 1100, 8, 10,
            "Tráng 20g hoa cúc qua nước sôi rồi đổ nước đó đi — bước này rửa bụi và làm hoa nở. "
          + "Hãm tiếp trong 1,3 lít nước 90°C, 7 phút.",
            new PrepLine[] { new("TEA-CHA-01", 20, "≈ 75 bông") },
            SortOrder: 5),

        // ---- CÀ PHÊ ----------------------------------------------------------
        new("PREP-COF-PHI", "Ủ cà phê phin", "PRE-COF-PHI", 500, 12, 45,
            "Chia 250g cà phê vào các phin lớn, ủ ra 500ml cốt đậm (tỷ lệ 1:2). "
          + "Chờ nguội rồi đậy kín, để ngăn mát. Cốt còn nóng mà đậy nắp sẽ bị hấp hơi và chua.",
            new PrepLine[] { new("COF-ROB-01", 250) },
            SortOrder: 6),

        new("PREP-COF-CLD", "Ủ cold brew", "PRE-COF-CLD", 1400, 168, 1080,
            "Ngâm 200g Arabica xay thô trong 2 lít nước lạnh, để ngăn mát ĐÚNG 18 tiếng rồi lọc. "
          + "Bã ngậm khoảng 600ml nên rót ra còn 1,4 lít. Ủ lâu hơn 20 tiếng là đắng gắt. "
          + "Mẻ này để được 7 ngày — làm một lần cho cả tuần.",
            new PrepLine[] { new("COF-ARA-01", 200, "Xay thô như đường cát, xay mịn sẽ lọc không hết") },
            SortOrder: 7),

        // ---- TOPPING NẤU TẠI QUÁN ---------------------------------------------
        //
        //  Ba mẻ này là thứ hết giữa ca nhiều nhất trong một quán trà sữa, và cũng
        //  là thứ nấu lại nhanh nhất. Vì vậy chúng phải bấm được ngay từ màn hình
        //  quầy: hết trân châu lúc 4 giờ chiều mà bắt nhân viên bỏ khách chạy sang
        //  trang Sơ chế thì mất cả mạch bán hàng.
        //
        //  Hạn dùng tính bằng GIỜ chứ không phải ngày: trân châu để qua đêm bị
        //  cứng lại, sáng hôm sau nhai như hạt sạn. 8 tiếng là đúng một ca.
        new("PREP-TOP-BOB", "Nấu trân châu đen", "TOP-BOB-01", 1000, 8, 25,
            "Đun 3 lít nước SÔI MẠNH rồi mới thả 400g trân châu khô — thả vào nước chưa sôi thì "
          + "hạt tan ra thành hồ. Luộc 20 phút, tắt bếp ủ thêm 15 phút cho chín tới lõi. "
          + "Xả nước lạnh cho hạt săn lại, rồi ngâm đường đen. "
          + "Ủ ẤM liên tục trong bình giữ nhiệt, đừng để tủ mát — lạnh là cứng.",
            new PrepLine[]
            {
                new("TOP-BOB-RAW", 400, "Cân trước khi luộc, hạt nở gấp 2,5 lần"),
                new("SYR-BRW-01",  100, "Ngâm sau khi xả nước lạnh, đảo đều cho ngấm")
            },
            SortOrder: 10),

        new("PREP-TOP-BOW", "Nấu trân châu trắng", "TOP-BOW-01", 800, 8, 20,
            "Thả 320g trân châu trắng vào 2,5 lít nước sôi, luộc 15 phút rồi ủ 10 phút. "
          + "Trân châu trắng chín nhanh hơn loại đen và dễ nát — canh giờ, đừng luộc theo cảm tính. "
          + "Xả lạnh rồi ngâm nước đường.",
            new PrepLine[]
            {
                new("TOP-BOW-RAW", 320),
                new("SYR-SUG-01",   80)
            },
            SortOrder: 11),

        new("PREP-TOP-PUD", "Làm pudding trứng", "TOP-PUD-01", 1000, 24, 30,
            "Đánh tan 120g bột pudding với 800ml sữa tươi và 4 quả trứng, LỌC QUA RÂY để bỏ lợn cợn — "
          + "bỏ bước rây là pudding rỗ mặt. Hấp lửa nhỏ 20 phút, để nguội rồi mới cho vào ngăn mát. "
          + "Cắt miếng ngay trước khi bán, cắt sẵn thì chảy nước.",
            new PrepLine[]
            {
                new("TOP-PUD-RAW", 120),
                new("DAI-MILK-01", 800),
                new("OTH-EGG-01",    4)
            },
            SortOrder: 12),

        new("PREP-TOP-COF", "Nấu thạch cà phê", "TOP-COF-01", 1000, 24, 20,
            "Hòa 15g bột rau câu dẻo với 700ml nước nguội, khuấy đều rồi mới bắc lên bếp — "
          + "đổ bột vào nước nóng là vón cục. Đun sôi, hạ lửa, thêm 150ml nước đường và 150ml "
          + "cà phê phin cốt, khuấy 1 phút rồi tắt bếp. Đổ khay, để nguội hẳn rồi cho ngăn mát "
          + "ít nhất 2 tiếng mới cắt hạt lựu. Nấu từ đầu ca, đừng đợi hết mới nấu.",
            new PrepLine[]
            {
                new("TOP-AGA-RAW",  15, "Cân chính xác — dư 3g là thạch cứng như cao su"),
                new("PRE-COF-PHI", 150),
                new("SYR-SUG-01",  150)
            },
            SortOrder: 13),

        // ---- SIRO & KEM NHÀ LÀM -----------------------------------------------
        new("PREP-SYR-SUG", "Nấu nước đường", "SYR-SUG-01", 1500, 720, 25,
            "Đun 1kg đường cát với 800ml nước tới khi tan hoàn toàn và hơi sánh. "
          + "Để nguội hẳn mới rót vào chai — rót lúc còn nóng thì hơi nước đọng nắp và mẻ lên men. "
          + "Để được 30 ngày trong ngăn mát.",
            new PrepLine[] { new("SWE-SUG-01", 1000) },
            SortOrder: 8),

        new("PREP-SYR-SLT", "Đánh kem muối", "SYR-SLT-01", 480, 24, 15,
            "Đánh 300ml kem béo với 150ml sữa tươi, 60g đường và 6g muối tới khi bông MỀM — "
          + "bông cứng thì không rót lên mặt ly được, nó đóng cục. "
          + "Chỉ để được một ngày, đánh vừa đủ ca.",
            new PrepLine[]
            {
                new("DAI-WHIP-01", 300),
                new("DAI-MILK-01", 150),
                new("SWE-SUG-01",   60),
                new("SWE-SAL-01",    6, "Cân bằng cân tiểu ly — thừa 2g là mặn không uống được")
            },
            SortOrder: 9)
    };

    /// <summary>
    /// Tra nhanh nguyên liệu theo Sku. Dùng khi tính giá vốn ngoài database.
    /// </summary>
    public static readonly Dictionary<string, IngredientSpec> IngredientBySku =
        Ingredients.ToDictionary(i => i.Sku, StringComparer.Ordinal);

    /// <summary>
    /// Giá vốn nguyên liệu của một món theo ĐÚNG công thức mà hệ thống dùng:
    /// Σ(định lượng × đơn giá × (1 + hao hụt)), làm tròn về đồng.
    /// <para>
    /// Dòng tùy chọn (đá, đường) VẪN được tính, vì phần lớn khách không bỏ chúng —
    /// tính vào cho giá vốn phản ánh trường hợp phổ biến chứ không phải trường hợp tốt nhất.
    /// </para>
    /// <para>
    /// Đây là bản tính ngoài database, dùng cho báo cáo và kiểm tra. Con số chính
    /// thức của từng món do <c>RecipeService.ComputeProductCostAsync</c> tính lại
    /// từ giá vốn bình quân THỰC TẾ của các lô đã nhập.
    /// </para>
    /// </summary>
    public static int EstimateCost(ProductSpec p)
    {
        var total = 0.0;

        foreach (var line in p.Recipe)
        {
            if (!IngredientBySku.TryGetValue(line.Sku, out var ing)) continue;
            total += line.Quantity * ing.UnitCost * (1 + ing.WastageRate);
        }

        return (int)Math.Round(total);
    }

    /// <summary>
    /// Tỷ lệ giá vốn mục tiêu của một món: đồ ăn khác đồ uống.
    /// Trả về hằng số trong <c>QlyCoffee.Shared.Pricing</c>.
    /// </summary>
    public static int TargetCostRatio(ProductSpec p) =>
        p.Serve == ServeStyle.Food
            ? Shared.Pricing.FoodCostRatioPercent
            : Shared.Pricing.DrinkCostRatioPercent;
}
