namespace QlyCoffee.Client.Services;

// ==============================================================================
//  GIỜ MỞ CỬA — MỘT NGUỒN SỰ THẬT DUY NHẤT
// ==============================================================================
//
//  BỐI CẢNH
//  Khung giờ "07:00 – 22:00" trước đây nằm rải rác ở bốn nơi: chip trên trang
//  chủ, khối "Ghé quán", chân trang, và thẻ JSON-LD trong index.html. Sửa một
//  chỗ quên ba chỗ là chuyện sớm muộn — mà lệch giờ giữa website và hồ sơ
//  Google Business Profile thì Google hạ điểm tin cậy của quán.
//
//  Lớp này gom khung giờ về một chỗ VÀ trả lời được câu hỏi quan trọng hơn:
//  "ngay lúc này quán còn mở không". Chip trạng thái trên trang chủ dựa vào đó
//  để đổi từ chấm xanh sang chấm đỏ sau giờ đóng cửa, thay vì suốt ngày khoe
//  "đang mở" kể cả lúc 2 giờ sáng.
//
//  MÚI GIỜ
//  Đây là Blazor WebAssembly nên DateTime.Now là giờ MÁY KHÁCH, không phải giờ
//  máy chủ. Với quán ở TP.HCM phục vụ khách quanh đó thì hai giá trị này trùng
//  nhau. Khách đang ở múi giờ khác sẽ thấy trạng thái theo đồng hồ của họ —
//  chấp nhận được, vì họ cũng không ghé quán được. Nếu sau này cần chuẩn tuyệt
//  đối thì đổi Now() sang quy đổi về Asia/Ho_Chi_Minh (biến STORE_TIMEZONE
//  trong .env), phần còn lại của lớp giữ nguyên.
//
//  ⚠️ Sửa giờ ở đây thì nhớ sửa cả:
//     1. Khối JSON-LD "openingHoursSpecification" trong wwwroot/index.html
//     2. Biến STORE_OPEN_TIME / STORE_CLOSE_TIME trong .env
// ==============================================================================

public static class StoreHours
{
    /// <summary>Giờ mở cửa, tính theo đồng hồ 24 giờ.</summary>
    public static readonly TimeOnly Open = new(7, 0);

    /// <summary>Giờ đóng cửa. Quán không bán qua đêm nên Close luôn lớn hơn Open.</summary>
    public static readonly TimeOnly Close = new(22, 0);

    /// <summary>Chuỗi "07:00" để in ra giao diện — định dạng cố định, không theo máy khách.</summary>
    public static string OpenLabel => Open.ToString("HH\\:mm");

    /// <summary>Chuỗi "22:00" để in ra giao diện.</summary>
    public static string CloseLabel => Close.ToString("HH\\:mm");

    /// <summary>Giá trị cho thuộc tính datetime của thẻ &lt;time&gt; theo cú pháp schema.org.</summary>
    public static string SchemaValue => $"Mo-Su {OpenLabel}-{CloseLabel}";

    /// <summary>
    /// Quán có đang mở tại thời điểm <paramref name="at"/> không.
    ///
    /// Biên được xử lý theo cách khách hiểu chứ không theo cách lập trình viên
    /// thấy tiện: đúng 07:00 là ĐÃ mở, đúng 22:00 là ĐÃ đóng. Nhân viên cần vài
    /// phút dọn dẹp, không ai muốn khách bấm đặt đúng phút cuối rồi tới nơi thấy
    /// cửa cuốn đã kéo xuống.
    /// </summary>
    public static bool IsOpenAt(DateTime at)
    {
        var now = TimeOnly.FromDateTime(at);
        return now >= Open && now < Close;
    }

    /// <summary>Quán có đang mở ngay bây giờ không (theo đồng hồ máy khách).</summary>
    public static bool IsOpenNow() => IsOpenAt(DateTime.Now);

    /// <summary>
    /// Câu hiển thị trên chip trạng thái ở trang chủ.
    ///
    /// Lúc đóng cửa KHÔNG chỉ nói "đã đóng" rồi bỏ mặc khách: câu trả lời hữu
    /// ích là "mở lại lúc mấy giờ". Khách biết được điều đó thì quay lại,
    /// còn không thì đóng tab.
    /// </summary>
    public static string StatusLabel(bool isOpen)
        => isOpen
            ? $"Đang mở cửa · {OpenLabel} – {CloseLabel} mỗi ngày"
            : $"Đã đóng cửa · Mở lại lúc {OpenLabel}";

    /// <summary>
    /// Bản tiện dụng cho nơi chỉ có mốc thời gian.
    ///
    /// Giao diện nên gọi bản nhận sẵn bool ở trên và truyền vào đúng biến trạng
    /// thái đang điều khiển màu chấm. Nếu chữ tự đọc giờ một lần còn chấm đọc
    /// giờ một lần khác, hai lần đọc đó rơi vào hai bên mốc 22:00 là chip hiện
    /// ra chấm xanh kèm chữ "đã đóng cửa".
    /// </summary>
    public static string StatusLabel(DateTime at) => StatusLabel(IsOpenAt(at));
}
