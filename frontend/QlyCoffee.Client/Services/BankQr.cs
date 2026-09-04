namespace QlyCoffee.Client.Services;

// ==============================================================================
//  MÃ QR CHUYỂN KHOẢN
// ==============================================================================
//
//  Sinh ảnh QR theo chuẩn VietQR qua dịch vụ ảnh của SePay:
//
//      https://qr.sepay.vn/img?acc=…&bank=…&amount=…&des=…
//
//  Đây là một ĐƯỜNG DẪN ẢNH thuần túy, không phải lệnh gọi API: không cần khóa
//  bí mật, không cần backend, thẻ <img> trỏ thẳng vào là ra QR. Nhờ vậy quán
//  nhận được chuyển khoản đúng số tiền ngay từ hôm nay, trước cả khi đấu nối
//  webhook SePay để tự động đối soát.
//
//  ⚠️ SỐ TÀI KHOẢN KHÔNG VIẾT CỨNG Ở ĐÂY.
//  Nó nằm trong .env (BANK_CODE, BANK_ACCOUNT_NUMBER, BANK_ACCOUNT_NAME),
//  được script scripts/gen-client-config.ps1 ghi ra wwwroot/appsettings.json
//  lúc build, rồi Program.cs nạp vào lớp này. Đổi ngân hàng chỉ sửa .env.
//
//  KHI ĐẤU NỐI SEPAY THẬT
//  SePay đối soát tự động bằng cách dò NỘI DUNG CHUYỂN KHOẢN. Mã tham chiếu do
//  NewReference() sinh ra chính là chỗ để móc vào: lưu nó kèm đơn hàng, rồi khi
//  webhook SePay báo có tiền về với nội dung chứa mã đó thì đánh dấu đơn đã
//  thanh toán. Hiện tại mã mới chỉ hiện ra cho nhân viên đối chiếu bằng mắt.
// ==============================================================================

public static class BankQr
{
    /// <summary>Mã ngân hàng theo danh sách VietQR/NAPAS. VD: "VCB", "TCB", "MB", "ACB".</summary>
    public static string BankCode { get; set; } = "";

    /// <summary>Số tài khoản nhận tiền.</summary>
    public static string AccountNumber { get; set; } = "";

    /// <summary>Tên chủ tài khoản, chỉ để hiện cho khách đối chiếu trước khi bấm chuyển.</summary>
    public static string AccountName { get; set; } = "";

    /// <summary>
    /// Đã khai báo đủ để dựng được QR chưa.
    ///
    /// Giao diện phải hỏi cờ này trước khi vẽ: chưa cấu hình mà vẫn nhét thẻ
    /// &lt;img&gt; vào thì khách nhìn thấy một ô ảnh vỡ ngay tại bước trả tiền —
    /// mất lòng tin hơn hẳn so với việc nói thẳng "chưa cài số tài khoản".
    /// </summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BankCode) && !string.IsNullOrWhiteSpace(AccountNumber);

    /// <summary>
    /// Đường dẫn ảnh QR cho đúng số tiền và đúng nội dung chuyển khoản.
    /// </summary>
    /// <param name="amount">Số tiền, đơn vị đồng.</param>
    /// <param name="content">Nội dung chuyển khoản — xem NewReference().</param>
    public static string ImageUrl(int amount, string content)
    {
        // template=qronly: CHỈ mã QR, không khung VietQR, không logo napas và
        // ngân hàng. Ô hiển thị ở quầy chỉ rộng hơn trăm điểm ảnh; bản "compact"
        // dành gần một phần ba chiều cao cho logo, phần mã thật co lại nhỏ đến
        // mức khách phải rê điện thoại vài lần mới bắt được.
        //
        // Danh tính người nhận không mất đi: mã ngân hàng, số tài khoản và tên
        // chủ tài khoản in bằng chữ ngay cạnh mã, khách đọc được trước khi quét
        // và app ngân hàng còn hiện lại lần nữa trước khi bấm xác nhận.
        return "https://qr.sepay.vn/img"
             + $"?acc={Uri.EscapeDataString(AccountNumber)}"
             + $"&bank={Uri.EscapeDataString(BankCode)}"
             + $"&amount={amount}"
             + $"&des={Uri.EscapeDataString(content)}"
             + "&template=qronly";
    }

    /// <summary>
    /// Sinh mã tham chiếu ngắn cho một lần thanh toán, VD "MCC7K2M9".
    ///
    /// Vì sao cần: hai khách liên tiếp cùng gọi ly 45.000đ thì hai giao dịch về
    /// tài khoản giống hệt nhau, nhân viên không biết cái nào của ai. Có mã
    /// riêng thì nhìn thông báo ngân hàng là đối chiếu được ngay.
    ///
    /// Bảng chữ cái cố tình BỎ các ký tự dễ đọc nhầm khi nhân viên đọc mã qua
    /// màn hình: 0/O, 1/I/L, 5/S, 8/B. Nội dung chuyển khoản chỉ nhận chữ và số
    /// không dấu nên bảng này cũng an toàn với chuẩn VietQR.
    /// </summary>
    public static string NewReference()
    {
        const string alphabet = "ACDEFGHJKMNPQRTUVWXY2346789";

        return "MCC" + new string(Enumerable
            .Range(0, 5)
            .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
            .ToArray());
    }
}
