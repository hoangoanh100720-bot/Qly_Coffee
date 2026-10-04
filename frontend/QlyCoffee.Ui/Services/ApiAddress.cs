namespace QlyCoffee.Client.Services;

// ==============================================================================
//  ĐỊA CHỈ BACKEND — CHỌN HOST THEO CHÍNH TRANG ĐANG MỞ
//
//  VẤN ĐỀ NÓ GIẢI QUYẾT
//
//  appsettings.json của hai frontend ghi cứng địa chỉ API, và script chạy LAN
//  ghi vào đó địa chỉ IP của máy tại thời điểm chạy, ví dụ:
//
//      "ApiBaseUrl": "http://10.50.104.62:5080"
//
//  Đổi wifi một cái là máy có IP khác. Địa chỉ cũ không còn tồn tại trên mạng,
//  nên MỌI lời gọi API từ trình duyệt đều chết — toàn bộ trang trắng dữ liệu:
//  không thực đơn, không lịch workshop, không đơn hàng. Mà thông báo lỗi duy
//  nhất là "Không kết nối được máy chủ", không chỉ ra địa chỉ sai nằm ở đâu.
//
//  Chuyện này đã xảy ra thật, và sẽ xảy ra lại mỗi lần đổi mạng nếu chỉ sửa
//  con số trong file cấu hình.
//
//  CÁCH LÀM
//
//  Nếu host trong cấu hình là MỘT MÁY TRONG NHÀ (localhost hoặc IP mạng nội
//  bộ), thay nó bằng host mà trình duyệt đang mở, GIỮ NGUYÊN cổng:
//
//      Mở ở localhost:5180        → API ở localhost:5080
//      Mở ở 192.168.1.32:5180     → API ở 192.168.1.32:5080   (điện thoại)
//      Mở ở shop.motchut.vn       → giữ nguyên api.motchut.vn (tên miền thật)
//
//  Suy từ trang đang mở là đúng về bản chất: ở môi trường phát triển, backend
//  luôn nằm trên CÙNG MỘT MÁY với frontend. Ai mở được trang thì cũng gọi được
//  API ở đúng máy đó — không cần biết IP là bao nhiêu.
//
//  VÌ SAO KHÔNG BỎ HẲN CẤU HÌNH ĐI
//  Khi lên máy chủ thật, API nằm ở tên miền khác hẳn (api.motchut.vn) chứ không
//  phải cùng host với trang bán hàng. Lúc đó cấu hình là thứ duy nhất nói được
//  điều đó, nên nó phải thắng.
// ==============================================================================

/// <summary>Chọn địa chỉ backend đúng cho hoàn cảnh đang chạy.</summary>
public static class ApiAddress
{
    /// <summary>
    /// Trả về địa chỉ backend nên dùng.
    /// </summary>
    /// <param name="configured">Giá trị <c>ApiBaseUrl</c> trong appsettings.json. Có thể rỗng.</param>
    /// <param name="pageBaseAddress">
    /// Địa chỉ trang đang mở (<c>builder.HostEnvironment.BaseAddress</c>),
    /// ví dụ "http://192.168.1.32:5180/".
    /// </param>
    public static string Resolve(string? configured, string pageBaseAddress)
    {
        // Không cấu hình gì: backend đứng luôn làm host cho file tĩnh.
        if (string.IsNullOrWhiteSpace(configured))
            return pageBaseAddress;

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var api))
            return pageBaseAddress;

        // Cấu hình trỏ ra Internet thật → tôn trọng, không đụng vào.
        if (!IsLocalHost(api.Host))
            return configured;

        if (!Uri.TryCreate(pageBaseAddress, UriKind.Absolute, out var page))
            return configured;

        // Trang đang mở cũng phải là máy trong nhà thì mới thay. Trường hợp
        // ngược đời (trang ở tên miền thật mà cấu hình trỏ localhost) là cấu
        // hình sai — giữ nguyên để lỗi lộ ra, đừng âm thầm đoán hộ.
        if (!IsLocalHost(page.Host))
            return configured;

        if (string.Equals(api.Host, page.Host, StringComparison.OrdinalIgnoreCase))
            return configured;

        // Giữ nguyên cổng và giao thức của API, chỉ đổi host.
        var fixedUp = new UriBuilder(api) { Host = page.Host };
        return fixedUp.Uri.ToString();
    }

    /// <summary>
    /// Như <see cref="Resolve"/> nhưng GIỮ RỖNG khi không có cấu hình.
    /// <para>
    /// Dùng cho liên kết sang ứng dụng kia (trang bán hàng ↔ trang quản lý).
    /// Ở đó "không cấu hình" có nghĩa là ẨN liên kết đi, chứ không phải trỏ về
    /// chính mình — một nút "Xem trang bán hàng" dẫn ngược về trang quản lý thì
    /// tệ hơn là không có nút nào.
    /// </para>
    /// </summary>
    public static string ResolveLink(string? configured, string pageBaseAddress)
        => string.IsNullOrWhiteSpace(configured) ? "" : Resolve(configured, pageBaseAddress);

    /// <summary>
    /// Host này có phải một máy trong mạng nội bộ không.
    /// <para>
    /// Các dải địa chỉ riêng theo RFC 1918 (10.x, 172.16–31.x, 192.168.x),
    /// dải link-local 169.254.x, dải CGNAT 100.64–127.x mà nhiều router phát
    /// wifi dùng, cùng localhost và tên máy nội bộ (.local, không có dấu chấm).
    /// </para>
    /// </summary>
    private static bool IsLocalHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host is "127.0.0.1" or "0.0.0.0" or "::1") return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

        // Tên máy trần, không có dấu chấm ("may-cua-toi") chỉ phân giải được
        // trong mạng nội bộ.
        if (!host.Contains('.')) return true;

        var parts = host.Split('.');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b)) return false;

        return a switch
        {
            10  => true,
            127 => true,
            169 => b == 254,
            172 => b >= 16 && b <= 31,
            192 => b == 168,
            100 => b >= 64 && b <= 127,
            _   => false
        };
    }
}
