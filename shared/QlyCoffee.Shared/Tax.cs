namespace QlyCoffee.Shared;

// ==============================================================================
//  THUẾ GIÁ TRỊ GIA TĂNG — MỘT NGUỒN SỰ THẬT DUY NHẤT
// ==============================================================================
//
//  VÌ SAO LỚP NÀY NẰM Ở DỰ ÁN DÙNG CHUNG
//  Con số thuế xuất hiện ở HAI nơi và bắt buộc phải khớp từng đồng:
//    · Giỏ hàng (Blazor) — khách nhìn thấy trước khi bấm đặt
//    · OrderService (backend) — số được chốt vào đơn và in ra hóa đơn
//  Lệch một đồng giữa hai chỗ là khách mất niềm tin ngay tại bước trả tiền.
//  Đặt phép tính ở đây thì cả hai phía gọi CÙNG một hàm, không thể lệch.
//
//  CĂN CỨ PHÁP LÝ (áp dụng cho quán cà phê, đồ uống tại Việt Nam)
//
//  1. GIÁ NIÊM YẾT ĐÃ BAO GỒM THUẾ.
//     Luật Giá 2023, Điều 29: giá niêm yết là giá đã bao gồm các loại thuế,
//     phí và lệ phí (nếu có) của hàng hóa, dịch vụ.
//     → Giá trên thực đơn KHÔNG được cộng thêm thuế ở bước thanh toán.
//       Thuế phải được TÁCH RA TỪ BÊN TRONG tổng tiền, không cộng vào.
//       Đây là lý do TaxMode.Inclusive là mặc định của hệ thống.
//
//  2. THUẾ SUẤT.
//     Dịch vụ ăn uống chịu thuế suất phổ thông 10% theo Luật Thuế GTGT.
//     Đang được giảm còn 8% theo Nghị quyết 204/2025/QH15, áp dụng từ
//     01/7/2025 đến hết 31/12/2026. Ngành ăn uống KHÔNG nằm trong danh mục
//     bị loại trừ khỏi diện giảm thuế.
//     → Mặc định của hệ thống là 8%. HẾT 31/12/2026 PHẢI XEM LẠI: nếu Quốc hội
//       không gia hạn thì đổi về 10% qua biến STORE_VAT_RATE trong .env,
//       không phải sửa mã nguồn.
//
//  3. HỘ KINH DOANH NỘP THUẾ THEO PHƯƠNG PHÁP TRỰC TIẾP.
//     Thông tư 40/2021/TT-BTC: dịch vụ ăn uống nộp theo tỷ lệ phần trăm trên
//     doanh thu (GTGT 3%, TNCN 1,5%). Nhóm này dùng HÓA ĐƠN BÁN HÀNG — trên
//     hóa đơn KHÔNG có dòng thuế GTGT tách riêng và không được thu thuế GTGT
//     của khách.
//     → Quán thuộc diện này phải đặt TaxMode.None, nếu không là ghi sai bản
//       chất nghĩa vụ thuế trên chứng từ giao cho khách.
//
//  4. HÓA ĐƠN ĐIỆN TỬ TỪ MÁY TÍNH TIỀN.
//     Nghị định 70/2025/NĐ-CP: từ 01/6/2025, hộ và cá nhân kinh doanh ngành ăn
//     uống có doanh thu từ 1 tỷ đồng một năm phải dùng hóa đơn điện tử khởi tạo
//     từ máy tính tiền, có kết nối dữ liệu với cơ quan thuế.
//     Hệ thống này CHƯA phát hành hóa đơn điện tử — phần dưới đây chỉ tách và
//     hiển thị đúng số thuế. Muốn xuất hóa đơn hợp lệ vẫn phải nối với một nhà
//     cung cấp hóa đơn điện tử.
//
//  ⚠️ Đây là phần mềm, không phải tư vấn thuế. Chủ quán cần xác nhận với cơ quan
//     thuế quản lý xem quán thuộc phương pháp khấu trừ hay trực tiếp trước khi
//     chọn TaxMode.
// ==============================================================================

/// <summary>
/// Cách quán ứng xử với thuế GTGT trên chứng từ giao cho khách.
/// </summary>
public enum TaxMode
{
    /// <summary>
    /// Không tách thuế GTGT trên chứng từ.
    /// <para>
    /// Dành cho hộ kinh doanh nộp thuế theo phương pháp trực tiếp hoặc khoán —
    /// nhóm này dùng hóa đơn bán hàng, không có dòng thuế GTGT.
    /// Giao diện sẽ hiện ghi chú thay cho dòng thuế.
    /// </para>
    /// </summary>
    None = 0,

    /// <summary>
    /// MẶC ĐỊNH. Giá niêm yết ĐÃ bao gồm thuế GTGT — đúng Luật Giá 2023 Điều 29.
    /// <para>
    /// Tổng tiền khách trả KHÔNG đổi. Hệ thống chỉ tách ngược ra hai dòng: tiền
    /// hàng chưa thuế và tiền thuế, để khách và kế toán cùng đọc được.
    /// </para>
    /// </summary>
    Inclusive = 1,

    /// <summary>
    /// Giá niêm yết CHƯA gồm thuế, thuế được cộng thêm vào tổng.
    /// <para>
    /// Chỉ dùng khi bán cho doanh nghiệp theo báo giá chưa thuế đã thỏa thuận
    /// trước. KHÔNG dùng cho giá niêm yết bán lẻ tại quán — làm vậy là thu thêm
    /// ngoài giá niêm yết, trái Luật Giá.
    /// </para>
    /// </summary>
    Exclusive = 2
}

/// <summary>
/// Kết quả tách thuế của một hóa đơn. Mọi số đều là ĐỒNG, đã làm tròn.
/// </summary>
/// <param name="Mode">Cách xử lý thuế đã áp dụng, giá trị của <see cref="TaxMode"/>.</param>
/// <param name="RatePercent">Thuế suất phần trăm đã dùng. 0 khi Mode = None.</param>
/// <param name="Net">Tiền hàng CHƯA có thuế.</param>
/// <param name="Tax">Tiền thuế GTGT.</param>
/// <param name="Gross">Tổng tiền khách phải trả. Luôn bằng Net + Tax.</param>
public readonly record struct VatBreakdown(int Mode, int RatePercent, int Net, int Tax, int Gross)
{
    /// <summary>Có dòng thuế để hiển thị hay không.</summary>
    public bool HasTax => Mode != (int)TaxMode.None && RatePercent > 0;
}

/// <summary>
/// Phép tách thuế GTGT. Không giữ trạng thái, gọi được từ cả hai phía.
/// </summary>
public static class VatPolicy
{
    /// <summary>
    /// Thuế suất mặc định, phần trăm.
    /// <para>
    /// 8% theo Nghị quyết 204/2025/QH15, hiệu lực tới hết 31/12/2026.
    /// Sau mốc đó thuế suất dịch vụ ăn uống trở lại 10% nếu không được gia hạn.
    /// </para>
    /// </summary>
    public const int DefaultRatePercent = 8;

    /// <summary>Chế độ mặc định: giá niêm yết đã gồm thuế (Luật Giá 2023 Điều 29).</summary>
    public const int DefaultMode = (int)TaxMode.Inclusive;

    /// <summary>Thuế suất phổ thông của dịch vụ ăn uống khi hết thời gian được giảm.</summary>
    public const int StandardRatePercent = 10;

    /// <summary>Ngày cuối cùng thuế suất 8% còn hiệu lực theo nghị quyết hiện hành.</summary>
    public static readonly DateOnly ReducedRateEndsOn = new(2026, 12, 31);

    /// <summary>
    /// Tách thuế cho một số tiền đã trừ khuyến mãi.
    /// </summary>
    /// <param name="payable">
    /// Số tiền theo giá niêm yết sau khuyến mãi, tức Subtotal − DiscountTotal.
    /// Với Inclusive đây chính là số khách trả; với Exclusive đây là phần chưa thuế.
    /// </param>
    /// <param name="mode">Giá trị <see cref="TaxMode"/> của cửa hàng.</param>
    /// <param name="ratePercent">Thuế suất phần trăm.</param>
    public static VatBreakdown Compute(int payable, int mode, int ratePercent)
    {
        // Số âm không có ý nghĩa với hóa đơn. Kẹp về 0 thay vì ném lỗi giữa lúc
        // khách đang bấm — một dòng thuế bằng 0 thì vô hại, một trang trắng thì không.
        if (payable < 0) payable = 0;

        if (mode == (int)TaxMode.None || ratePercent <= 0)
            return new VatBreakdown((int)TaxMode.None, 0, payable, 0, payable);

        if (mode == (int)TaxMode.Exclusive)
        {
            // Giá chưa thuế nên thuế được cộng thêm vào tổng.
            var addedTax = (int)Math.Round(
                payable * ratePercent / 100.0, MidpointRounding.AwayFromZero);
            return new VatBreakdown(mode, ratePercent, payable, addedTax, payable + addedTax);
        }

        // Inclusive — tách ngược từ tổng đã gồm thuế:
        //     net  = gross × 100 / (100 + thuế suất)
        //     thuế = gross − net
        //
        // Tính THUẾ bằng phép trừ chứ không tính riêng rồi cộng lại: làm tròn hai
        // số độc lập có thể ra tổng lệch 1đ so với số khách đã nhìn thấy, mà trên
        // hóa đơn thì "tiền hàng + thuế ≠ tổng" là lỗi không giải thích nổi.
        var net = (int)Math.Round(
            payable * 100.0 / (100 + ratePercent), MidpointRounding.AwayFromZero);

        return new VatBreakdown(mode, ratePercent, net, payable - net, payable);
    }

    /// <summary>Nhãn dòng thuế trên hóa đơn. VD: "Thuế GTGT (8%)".</summary>
    public static string RateLabel(int ratePercent) => $"Thuế GTGT ({ratePercent}%)";

    /// <summary>
    /// Câu giải thích ngắn đặt dưới bảng tiền, cho khách hiểu vì sao tổng không đổi.
    /// </summary>
    public static string Explanation(int mode) => mode switch
    {
        (int)TaxMode.Inclusive =>
            "Giá trên thực đơn đã bao gồm thuế GTGT theo Luật Giá 2023. "
          + "Dòng thuế bên trên được tách ra từ tổng tiền, không cộng thêm.",

        (int)TaxMode.Exclusive =>
            "Giá niêm yết chưa bao gồm thuế GTGT. Thuế được cộng vào tổng thanh toán.",

        _ =>
            "Quán nộp thuế theo phương pháp trực tiếp trên doanh thu nên chứng từ "
          + "không tách riêng dòng thuế GTGT."
    };
}

// ==============================================================================
//  GIÁ BÁN ĐỀ NGHỊ
// ==============================================================================

/// <summary>
/// Gợi ý giá bán từ giá vốn nguyên liệu.
/// <para>
/// ĐÂY CHỈ LÀ GỢI Ý. Người dùng luôn được sửa giá tùy ý — xem
/// <c>ProductsAdminController.UpdatePrice</c>. Con số này để chủ quán biết mình
/// đang bán dưới hay trên mặt bằng, chứ không phải để ép giá.
/// </para>
/// </summary>
public static class Pricing
{
    /// <summary>
    /// Tỷ lệ giá vốn trên giá bán mà nhóm đồ uống lấy làm chuẩn: 35%.
    /// <para>
    /// 65% còn lại KHÔNG phải lãi — đó là mặt bằng, điện nước, lương pha chế,
    /// hao hụt và khấu hao máy. Bán dưới mức này thì càng đông khách càng lỗ.
    /// </para>
    /// <para>
    /// VÌ SAO 35% CHỨ KHÔNG PHẢI 30% NHƯ SỐ HAY ĐƯỢC TRÍCH DẪN.
    /// Con số 30% của ngành là "food cost", tính riêng nguyên liệu và KHÔNG gồm
    /// bao bì. Ở hệ thống này, ly nhựa và ống hút nằm ngay trong công thức nên
    /// chúng được tính vào giá vốn — mà bao bì chiếm 5–8% giá bán một ly mang đi.
    /// Giữ nguyên 30% thì gần như MỌI món đều bị tô cảnh báo "biên mỏng", và một
    /// cảnh báo bật sáng ở mọi dòng thì không còn là cảnh báo nữa.
    /// </para>
    /// </summary>
    public const int DrinkCostRatioPercent = 35;

    /// <summary>
    /// Tỷ lệ giá vốn cho đồ ăn kèm (bánh, đồ ăn nhẹ): 50%.
    /// <para>
    /// Cao hơn đồ uống vì bánh thường NHẬP VỀ chứ không tự làm — quán không tạo
    /// thêm giá trị bằng tay nghề pha chế nên không đẩy giá được như đồ uống.
    /// Đổi lại, bánh gần như không tốn công phục vụ và không chiếm chỗ hàng pha.
    /// </para>
    /// </summary>
    public const int FoodCostRatioPercent = 50;

    /// <summary>
    /// Giá bán đề nghị, làm tròn LÊN bội số 1.000đ.
    /// <para>
    /// Làm tròn lên chứ không làm tròn về số gần nhất: làm tròn xuống là ăn vào
    /// biên lợi nhuận đã tính sát. Bội số 1.000đ vì đó là tờ tiền nhỏ nhất còn
    /// lưu hành thực tế — giá lẻ 500đ chỉ làm nhân viên thối tiền khó.
    /// </para>
    /// </summary>
    /// <param name="cost">Giá vốn nguyên liệu một phần, đồng.</param>
    /// <param name="costRatioPercent">Tỷ lệ giá vốn mục tiêu, phần trăm.</param>
    public static int Suggest(int cost, int costRatioPercent = DrinkCostRatioPercent)
    {
        if (cost <= 0 || costRatioPercent <= 0) return 0;

        var raw = cost * 100.0 / costRatioPercent;
        return (int)(Math.Ceiling(raw / 1000.0) * 1000);
    }

    /// <summary>Biên lợi nhuận gộp phần trăm của một mức giá.</summary>
    public static int MarginPercent(int price, int cost) =>
        price > 0 ? (int)Math.Round((price - cost) * 100.0 / price) : 0;

    /// <summary>Tỷ lệ giá vốn trên giá bán, phần trăm — con số ngành hay soi nhất.</summary>
    public static int CostRatioPercent(int price, int cost) =>
        price > 0 ? (int)Math.Round(cost * 100.0 / price) : 0;

    /// <summary>
    /// Đánh giá một mức giá so với giá vốn, dùng tô màu cảnh báo ở trang quản lý.
    /// </summary>
    /// <returns>
    /// -1 đang bán lỗ · 0 biên mỏng · 1 lành mạnh · 2 cao hơn mặt bằng cần thiết.
    /// </returns>
    public static int Verdict(int price, int cost, int targetRatio = DrinkCostRatioPercent)
    {
        if (price <= 0 || cost <= 0) return 0;

        var ratio = CostRatioPercent(price, cost);

        if (ratio >= 100) return -1;              // bán dưới giá vốn
        if (ratio > targetRatio + 10) return 0;   // biên mỏng hơn chuẩn ngành
        if (ratio < targetRatio - 12) return 2;   // đắt hơn mặt bằng, coi chừng mất khách
        return 1;
    }
}
