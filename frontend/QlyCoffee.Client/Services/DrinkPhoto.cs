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
//  ĐỘ PHỦ
//  Bộ ảnh cố ý CHIA NHỎ theo nhóm chứ không dùng một tấm chung. Thực đơn có
//  tám danh mục và hơn năm mươi món; một tấm "ly takeaway" dùng cho tất cả thì
//  lưới menu trông như lỗi lặp ảnh, và khách không phân biệt nổi món nào với
//  món nào. Mỗi nhóm dưới đây là một tấm riêng, chụp đúng thứ trong ly.
//
//  QUY TẮC KHI THÊM ẢNH MỚI
//  - Định dạng .webp, cạnh 900px chất lượng ~80, cắt vuông 1:1 (khớp aspect-ratio của
//    .product-visual trong design-system.css §9).
//  - Đặt tên tiếng Việt không dấu: tiền tố drink- cho đồ uống, food- cho đồ ăn,
//    hero-/không tiền tố cho ảnh trang trí.
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
    public const string Espresso   = Dir + "drink-espresso.webp";
    public const string TraSua     = Dir + "drink-tra-sua.webp";
    public const string TraTraiCay = Dir + "drink-tra-trai-cay.webp";
    public const string TraThaoMoc = Dir + "drink-tra-thao-moc.webp";
    public const string Matcha     = Dir + "drink-matcha.webp";
    public const string Chocolate  = Dir + "drink-socola.webp";
    public const string Soda       = Dir + "drink-soda.webp";
    public const string SuaChua    = Dir + "drink-sua-chua.webp";
    public const string DaXay      = Dir + "drink-da-xay.webp";
    public const string SinhTo     = Dir + "drink-sinh-to.webp";
    public const string NuocEp     = Dir + "drink-nuoc-ep.webp";
    public const string MacDinh    = Dir + "drink-mac-dinh.webp";

    // --- Đồ ăn kèm -------------------------------------------------------------
    public const string BanhNgot   = Dir + "food-banh-ngot.webp";
    public const string Croissant  = Dir + "food-croissant.webp";
    public const string Cookie     = Dir + "food-cookie.webp";
    public const string BanhMi     = Dir + "food-banh-mi.webp";

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
    /// Chọn ảnh mẫu theo tên danh mục và tên món.
    ///
    /// Đối sánh theo TỪ KHÓA TIẾNG VIỆT chứ không theo mã danh mục, vì chủ quán
    /// tự đặt tên danh mục trong trang quản lý — hôm nay là "Trà sữa", mai có
    /// thể là "Trà sữa nhà làm". So khớp bằng Contains nên cả hai đều trúng.
    ///
    /// ⚠️ THỨ TỰ CÁC NHÁNH LÀ MỘT PHẦN CỦA LOGIC, đừng sắp xếp lại cho "gọn".
    /// Nhánh HẸP phải đứng trước nhánh RỘNG, vì rất nhiều tên món chứa từ khóa
    /// của nhiều nhóm cùng lúc. Ba cái bẫy đã gặp thật:
    ///
    ///   · "Matcha latte" chứa chữ "latte" → nếu nhánh cà phê đứng trước thì
    ///     một ly matcha xanh lá lại hiện ảnh cà phê sữa đá.
    ///   · "Cookies &amp; cream đá xay" chứa chữ "cookie" → nếu nhánh bánh đứng
    ///     trước thì một ly đá xay lại hiện đĩa bánh quy.
    ///   · "Trà sữa matcha" chứa cả "trà sữa" lẫn "matcha" — trà sữa thắng vì
    ///     ly bưng ra là ly trà sữa, màu matcha chỉ là hương vị.
    /// </summary>
    public static string FallbackFor(string? categoryName, string? productName)
    {
        // Gộp danh mục và tên món thành một chuỗi để chỉ phải quét một lần.
        // ToLowerInvariant vì dữ liệu do người nhập, hoa thường không đoán trước được.
        var s = $"{categoryName} {productName}".ToLowerInvariant();

        // ---- 1. Trà sữa: thắng cả matcha, thắng cả trà trái cây --------------
        if (s.Contains("trà sữa") || s.Contains("tra sua") || s.Contains("trân châu"))
            return TraSua;

        // ---- 2. Matcha: PHẢI đứng trước cà phê vì "matcha latte" ------------
        if (s.Contains("matcha"))
            return Matcha;

        // ---- 3. Sữa chua ------------------------------------------------------
        if (s.Contains("sữa chua") || s.Contains("sua chua")
         || s.Contains("yaourt") || s.Contains("yogurt"))
            return SuaChua;

        // ---- 4. Đá xay & sinh tố: đứng trước bánh vì "cookies & cream đá xay" -
        if (s.Contains("sinh tố") || s.Contains("smoothie"))
            return SinhTo;

        if (s.Contains("đá xay") || s.Contains("da xay")
         || s.Contains("frappe") || s.Contains("frappuccino")
         || s.Contains("milkshake"))
            return DaXay;

        // ---- 5. Đồ ăn kèm -----------------------------------------------------
        //
        // PHẢI đứng trước nhánh socola: "Cookie socola" chứa chữ "socola", nếu để
        // sau thì một chiếc bánh quy lại hiện ảnh ly socola nóng. Ngược lại,
        // "Cookies & cream đá xay" đã bị nhánh đá xay ở trên bắt mất rồi nên
        // không có ly đá xay nào rơi nhầm vào đây.
        //
        // Trong nhóm này cũng theo hẹp trước rộng sau: "bánh mì", "croissant",
        // "cookie" đều là con của "bánh".
        if (s.Contains("bánh mì") || s.Contains("banh mi"))
            return BanhMi;

        if (s.Contains("croissant") || s.Contains("sừng bò"))
            return Croissant;

        if (s.Contains("cookie") || s.Contains("bánh quy"))
            return Cookie;

        if (s.Contains("bánh") || s.Contains("tiramisu")
         || s.Contains("su kem") || s.Contains("phô mai") || s.Contains("cheesecake"))
            return BanhNgot;

        // ---- 6. Socola & cacao ------------------------------------------------
        if (s.Contains("socola") || s.Contains("sô cô la")
         || s.Contains("chocolate") || s.Contains("cacao") || s.Contains("cocoa"))
            return Chocolate;

        // ---- 7. Soda ----------------------------------------------------------
        if (s.Contains("soda"))
            return Soda;

        // ---- 8. Nước ép -------------------------------------------------------
        if (s.Contains("nước ép") || s.Contains("ép trái cây") || s.Contains("juice"))
            return NuocEp;

        // ---- 9. Cà phê: tách espresso, nóng và đá vì ba bức ảnh khác hẳn nhau -
        if (s.Contains("espresso"))
            return Espresso;

        if (s.Contains("cà phê") || s.Contains("ca phe") || s.Contains("cafe")
         || s.Contains("coffee") || s.Contains("latte") || s.Contains("cappuccino")
         || s.Contains("macchiato") || s.Contains("americano")
         || s.Contains("cold brew") || s.Contains("bạc xỉu"))
        {
            // "kem trứng" nằm ở đây vì cà phê trứng LUÔN uống nóng — đó là cả
            // điểm của món: lớp kem trứng đánh bông chỉ giữ được độ bông khi
            // ly còn ấm. Tên món không có chữ "nóng" nên nếu không liệt kê
            // riêng, nó sẽ nhận ảnh cà phê đá.
            return s.Contains("nóng") || s.Contains("hot") || s.Contains("kem trứng")
                ? CaPheNong
                : CaPheDa;
        }

        // ---- 10. Trà thuần & thảo mộc: đứng trước trà trái cây ----------------
        // "Lục trà chanh" chứa chữ "chanh" nên nếu để sau, nó sẽ rơi vào ảnh
        // trà trái cây — mà đó là ly trà thuần, không có trái cây nào trong ly.
        if (s.Contains("hoa cúc") || s.Contains("thảo mộc") || s.Contains("gừng")
         || s.Contains("ô long") || s.Contains("o long")
         || s.Contains("lục trà") || s.Contains("trà xanh"))
            return TraThaoMoc;

        // ---- 11. Nhánh rộng: đặt CUỐI để không nuốt mất các nhánh trên --------
        if (s.Contains("trà") || s.Contains("tea")
         || s.Contains("chanh") || s.Contains("tắc") || s.Contains("đào")
         || s.Contains("vải") || s.Contains("ổi") || s.Contains("dứa")
         || s.Contains("việt quất") || s.Contains("dâu") || s.Contains("xoài"))
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
    ///
    /// Không mở đầu bằng "Ly" nữa: thực đơn giờ có cả bánh, mà "Ly croissant bơ"
    /// là câu vô nghĩa với người dùng trình đọc màn hình.
    /// </summary>
    public static string AltFor(string? imageUrl, string productName)
        => Media.HasPhoto(imageUrl)
            ? $"{productName} tại Một Chút Coffee"
            : $"Ảnh minh họa món {productName}";
}
