namespace QlyCoffee.Admin;

/// <summary>
/// Địa chỉ của những thứ NẰM NGOÀI app quản lý.
///
/// Trước đây trang bán hàng và trang quản lý chung một ứng dụng nên liên kết
/// "Xem trang bán hàng" chỉ cần trỏ tới "/". Giờ hai bên là hai ứng dụng phát
/// hành riêng, có thể ở hai tên miền khác nhau, nên địa chỉ đó phải đi ra cấu
/// hình chứ không viết cứng trong mã.
///
/// Giá trị nạp từ wwwroot/appsettings.json lúc khởi động — xem Program.cs.
/// File đó do scripts/gen-client-config.ps1 sinh ra từ .env, cùng đường với
/// ApiBaseUrl, nên đổi tên miền chỉ phải sửa một chỗ duy nhất.
/// </summary>
public static class AppLinks
{
    /// <summary>Trang bán hàng công khai. Để trống thì liên kết tự ẩn đi.</summary>
    public static string ShopUrl { get; set; } = "";

    /// <summary>Có đáng hiện liên kết sang trang bán hàng hay không.</summary>
    public static bool HasShopUrl => !string.IsNullOrWhiteSpace(ShopUrl);

    /// <summary>
    /// Thực đơn trên trang bán hàng — dùng cho nút "Xem trang bán hàng" ở màn
    /// hình Món, để quản lý xem ngay món vừa sửa hiện ra thế nào với khách.
    ///
    /// Ghép ở đây chứ không viết "@AppLinks.ShopUrl/menu" ngay trong .razor:
    /// địa chỉ trong cấu hình có thể có hoặc không có dấu "/" ở cuối, mà ghép
    /// thẳng thì ra "https://…//menu". Một số máy chủ trả 404 cho đường dẫn hai
    /// dấu gạch như vậy.
    /// </summary>
    public static string ShopMenuUrl => HasShopUrl ? ShopUrl.TrimEnd('/') + "/menu" : "";
}
