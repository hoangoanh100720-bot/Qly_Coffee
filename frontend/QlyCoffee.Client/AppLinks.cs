namespace QlyCoffee.Client;

/// <summary>
/// Địa chỉ của những thứ NẰM NGOÀI trang bán hàng.
///
/// Đối xứng với QlyCoffee.Admin/AppLinks.cs. Lý do tồn tại giống hệt: trang bán
/// hàng và app quản lý giờ là hai ứng dụng phát hành riêng, có thể ở hai tên
/// miền khác nhau, nên liên kết từ bên này sang bên kia không thể viết cứng
/// thành "/admin" được nữa — trong app này "/admin" chỉ dẫn tới trang không tìm
/// thấy.
///
/// Giá trị nạp từ wwwroot/appsettings.json lúc khởi động — xem Program.cs.
/// File đó do scripts/gen-client-config.ps1 sinh ra từ .env.
/// </summary>
public static class AppLinks
{
    /// <summary>
    /// App quản lý. Để trống thì liên kết "Trang quản lý" ở chân trang tự ẩn đi.
    ///
    /// Để trống là lựa chọn hợp lý nếu quán không muốn lộ địa chỉ trang quản lý
    /// ra trang công khai: nhân viên vẫn vào được bằng dấu trang trên máy của
    /// quán, mà khách thì không nhìn thấy đường vào.
    /// </summary>
    public static string AdminUrl { get; set; } = "";

    /// <summary>Có đáng hiện liên kết sang app quản lý hay không.</summary>
    public static bool HasAdminUrl => !string.IsNullOrWhiteSpace(AdminUrl);
}
