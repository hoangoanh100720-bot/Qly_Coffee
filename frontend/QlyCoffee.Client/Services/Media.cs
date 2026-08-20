namespace QlyCoffee.Client.Services;

// ==============================================================================
//  PHÂN GIẢI ĐƯỜNG DẪN ẢNH
//
//  Backend trả về đường dẫn TƯƠNG ĐỐI ("/uploads/products/abc.jpg") chứ không
//  phải đường dẫn đầy đủ. Đó là lựa chọn có chủ đích: đổi tên miền, chuyển từ
//  localhost sang production, hay đưa ảnh lên CDN đều không phải sửa dữ liệu
//  trong database.
//
//  Cái giá phải trả là frontend chạy ở cổng khác backend (5180 vs 5080) nên
//  <img src="/uploads/..."> sẽ tìm ảnh ở 5180 và nhận 404. Hàm dưới ghép thêm
//  địa chỉ backend vào cho đúng.
//
//  Đường dẫn đầy đủ (http://, https://) thì giữ nguyên — dùng khi ảnh nằm sẵn
//  trên CDN hoặc kho ảnh bên ngoài.
// ==============================================================================

public static class Media
{
    /// <summary>Địa chỉ backend, gán một lần lúc khởi động trong Program.cs.</summary>
    public static string ApiBaseUrl { get; set; } = "";

    /// <summary>
    /// Đổi đường dẫn ảnh từ database thành đường dẫn trình duyệt tải được.
    /// Trả về null khi món chưa có ảnh — nơi gọi sẽ tự rơi về hình vẽ SVG.
    /// </summary>
    public static string? Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // Đã là đường dẫn đầy đủ thì dùng luôn
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        // Đường dẫn nội bộ → ghép với địa chỉ backend
        return $"{ApiBaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    /// <summary>Món này đã có ảnh chụp thật chưa.</summary>
    public static bool HasPhoto(string? url) => !string.IsNullOrWhiteSpace(url);
}
