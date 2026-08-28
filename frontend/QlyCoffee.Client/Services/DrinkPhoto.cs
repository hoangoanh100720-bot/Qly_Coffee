namespace QlyCoffee.Client.Services;

// ==============================================================================
//  ẢNH DỰ PHÒNG CHO MÓN — TOÀN BỘ LÀ ẢNH CHỤP THẬT
// ==============================================================================
//
//  BỐI CẢNH
//  Trước đây khi một món chưa có ảnh trong database, giao diện dựng tạm một
//  hình ly nước bằng SVG từ hai mã màu color_hex. Cách đó có ba vấn đề:
//
//    1. Trông ra hình vẽ, không ra đồ uống. Khách nhìn hình vẽ thì không thèm.
//    2. Google Image Search không đọc được SVG dựng động — mất một nguồn
//       truy cập tự nhiên đáng kể với ngành đồ uống.
//    3. Thẻ og:image khi chia sẻ lên Facebook/Zalo phải là ảnh bitmap.
//
//  GIẢI PHÁP
//  Một bộ ảnh chụp thật đặt sẵn trong wwwroot/img/, chọn theo tên danh mục
//  và tên món. Ảnh này CHỈ là lớp đỡ: khi quán tải ảnh thật của chính mình
//  lên (cột image_url), ảnh đó luôn được ưu tiên — xem PhotoFor().
//
//  QUY TẮC KHI THÊM ẢNH MỚI
//  - Định dạng .webp, cạnh 600px, cắt vuông 1:1 (khớp aspect-ratio của
//    .product-visual trong design-system.css §9).
//  - Đặt tên tiếng Việt không dấu, tiền tố drink- để phân biệt với ảnh trang trí.
//  - Ghi nguồn vào wwwroot/img/NGUON-ANH.md.
// ==============================================================================

public static class DrinkPhoto
{
    /// <summary>Thư mục chứa ảnh tĩnh, tính từ gốc site (base href = "/").</summary>
    private const string Dir = "img/";

    // --- Bộ ảnh theo nhóm món -------------------------------------------------
    // Mỗi hằng số là MỘT vai trò, không phải một bức ảnh cụ thể. Muốn đổi ảnh
    // thì sửa ở đây, không phải đi tìm trong các trang .razor.
    public const string CaPheDa    = Dir + "drink-ca-phe-sua-da.webp";
    public const string CaPheNong  = Dir + "drink-ca-phe-nong.webp";
    public const string TraSua     = Dir + "drink-tra-sua.webp";
    public const string TraTraiCay = Dir + "drink-tra-trai-cay.webp";
    public const string DaXay      = Dir + "drink-da-xay.webp";
    public const string SinhTo     = Dir + "drink-sinh-to.webp";
    public const string NuocEp     = Dir + "drink-nuoc-ep.webp";
    public const string MacDinh    = Dir + "drink-mac-dinh.webp";

    // --- Ảnh trang trí dùng ngoài thẻ món -------------------------------------
    // Hero CHỈ có một ảnh. Bản trước từng có thêm HeroNuocEp và HeroTraSua cho
    // bố cục ba ảnh, nhưng hai món đó đã xuất hiện ở dải danh mục và lưới món
    // ngay bên dưới — bày lại trong hero là lặp lại chính mình và kéo dài trang.
    // Hai file ảnh đó đã được xóa khỏi wwwroot/img/.
    public const string HeroCaPhe   = Dir + "hero-ca-phe-da.webp";
    public const string KhongGian   = Dir + "khong-gian-quan.webp";
    public const string PhaChe      = Dir + "pha-che-thu-cong.webp";
    public const string HatCaPhe    = Dir + "hat-ca-phe-rang.webp";
    public const string BanBe       = Dir + "ban-be-ca-phe.webp";

    /// <summary>Ảnh dùng cho thẻ og:image khi chia sẻ trang chủ lên mạng xã hội.</summary>
    public const string OgCover = Dir + "og-cover.jpg";

    /// <summary>
    /// Trả về đường dẫn ảnh SẼ HIỂN THỊ cho một món.
    ///
    /// Thứ tự ưu tiên:
    ///   1. Ảnh quán tự chụp (imageUrl từ database) — luôn thắng.
    ///   2. Ảnh mẫu chọn theo danh mục và tên món.
    ///
    /// Hàm này KHÔNG BAO GIỜ trả về null: mọi thẻ món đều phải có ảnh, một ô
    /// trống trong lưới menu trông như lỗi tải trang.
    /// </summary>
    /// <param name="imageUrl">Giá trị cột image_url, có thể rỗng.</param>
    /// <param name="categoryName">Tên danh mục, dùng để đoán nhóm món.</param>
    /// <param name="productName">Tên món, dùng khi danh mục quá chung chung.</param>
    public static string PhotoFor(string? imageUrl, string? categoryName, string? productName)
    {
        // Ảnh thật của quán được ưu tiên tuyệt đối
        var own = Media.Resolve(imageUrl);
        if (own is not null) return own;

        return FallbackFor(categoryName, productName);
    }

    /// <summary>
    /// Chọn ảnh mẫu theo tên danh mục/tên món.
    ///
    /// Đối sánh theo TỪ KHÓA TIẾNG VIỆT chứ không theo mã danh mục, vì chủ quán
    /// tự đặt tên danh mục trong trang quản lý — hôm nay là "Trà sữa", mai có
    /// thể là "Trà sữa nhà làm". So khớp bằng Contains nên cả hai đều trúng.
    ///
    /// Thứ tự các nhánh QUAN TRỌNG: nhánh hẹp đứng trước nhánh rộng.
    /// "Trà sữa trân châu" phải khớp TraSua trước khi kịp rơi vào TraTraiCay
    /// (cả hai đều chứa chữ "trà").
    /// </summary>
    public static string FallbackFor(string? categoryName, string? productName)
    {
        // Gộp danh mục và tên món thành một chuỗi để chỉ phải quét một lần.
        // ToLowerInvariant vì dữ liệu do người nhập, hoa thường không đoán trước được.
        var s = $"{categoryName} {productName}".ToLowerInvariant();

        // --- Nhánh hẹp: những từ khóa chỉ thuộc về đúng một nhóm --------------
        if (s.Contains("trân châu") || s.Contains("tra sua") || s.Contains("trà sữa"))
            return TraSua;

        if (s.Contains("đá xay") || s.Contains("frappe") || s.Contains("frappuccino"))
            return DaXay;

        if (s.Contains("sinh tố") || s.Contains("smoothie"))
            return SinhTo;

        if (s.Contains("nước ép") || s.Contains("ép trái cây") || s.Contains("juice"))
            return NuocEp;

        // --- Cà phê: tách nóng và đá vì hai bức ảnh khác hẳn nhau -------------
        if (s.Contains("cà phê") || s.Contains("cafe") || s.Contains("coffee")
         || s.Contains("espresso") || s.Contains("latte") || s.Contains("cappuccino"))
        {
            return s.Contains("nóng") || s.Contains("hot") ? CaPheNong : CaPheDa;
        }

        // --- Nhánh rộng: đặt CUỐI để không nuốt mất các nhánh trên ------------
        if (s.Contains("trà") || s.Contains("tea") || s.Contains("soda")
         || s.Contains("chanh") || s.Contains("đào") || s.Contains("vải"))
            return TraTraiCay;

        // Không đoán được thì dùng ảnh ly takeaway trung tính — hợp với mọi món
        return MacDinh;
    }

    /// <summary>
    /// Câu mô tả cho thuộc tính alt của ảnh món.
    ///
    /// Ba lý do phải có hàm riêng thay vì đặt alt="@Product.Name":
    ///   - Trình đọc màn hình đọc "Cà phê sữa đá" mà không rõ đó là ảnh gì.
    ///   - Google dùng alt để xếp hạng tìm kiếm hình ảnh, cần cụm từ đầy đủ.
    ///   - Ảnh mẫu và ảnh thật cần mô tả khác nhau: nói rõ đây là ảnh minh họa
    ///     thì khách không thắc mắc vì sao ly trên ảnh khác ly nhận được.
    /// </summary>
    public static string AltFor(string? imageUrl, string productName)
        => Media.HasPhoto(imageUrl)
            ? $"Ly {productName} tại Qly Coffee"
            : $"Ảnh minh họa món {productName}";
}
