using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  WEBHOOK ĐỐI SOÁT CHUYỂN KHOẢN (SePay)
// ==============================================================================
//
//  LUỒNG ĐẦY ĐỦ
//    1. Máy quầy chọn "Chuyển khoản" → sinh mã tham chiếu MCCxxxxx, in vào mã QR
//       cùng số tiền. Mã đó được lưu vào Order.PaymentRef lúc chốt đơn.
//    2. Khách quét, chuyển tiền. Nội dung chuyển khoản chính là mã tham chiếu.
//    3. Tiền về tài khoản → SePay POST vào đúng endpoint dưới đây.
//    4. Dò mã trong nội dung → tìm đơn → so tiền → đánh dấu đã thanh toán.
//
//  BA NGUYÊN TẮC CỦA MỘT ENDPOINT NHẬN TIỀN, đọc kỹ trước khi sửa:
//
//  ① GHI TRƯỚC, XỬ LÝ SAU.
//    Mọi lần SePay gọi tới đều được ghi vào payment_transactions, kể cả giao
//    dịch không khớp đơn nào. Tiền đã vào tài khoản mà hệ thống im lặng bỏ qua
//    là trường hợp tệ nhất lúc đối soát cuối ngày.
//
//  ② MÃ HTTP LÀ MỘT PHẦN CỦA GIAO THỨC, KHÔNG PHẢI CHUYỆN THẨM MỸ.
//    SePay tự gửi lại tối đa 7 lần khi nhận mã ngoài dải 200-299. Nên:
//      · Đã ghi nhận xong (dù khớp hay không khớp đơn) → 200. Việc của mình
//        xong rồi, gửi lại nữa chỉ tốn công hai bên.
//      · Sai khóa API → 401, và KHÔNG ghi gì cả.
//      · Lỗi thật của server (mất kết nối DB…) → để nó bung ra thành 500, đúng
//        lúc cần SePay gửi lại.
//    Trả 200 cho lỗi server là mất luôn giao dịch — SePay sẽ không thử lại.
//
//  ③ CHỐNG GHI NHẬN TRÙNG BẰNG CHỈ MỤC DUY NHẤT, KHÔNG BẰNG TRÍ NHỚ.
//    payment_transactions.gateway_id là unique. Kiểm tra trước cho nhanh, nhưng
//    chốt chặn thật nằm ở tầng cơ sở dữ liệu: hai lần gửi lại đến cùng lúc thì
//    một trong hai sẽ đâm vào chỉ mục và bị bắt ở khối catch bên dưới.
// ==============================================================================

[ApiController]
[Route("hooks")]
[AllowAnonymous]
public class SePayWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(AppDbContext db, ILogger<SePayWebhookController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Mã tham chiếu trong nội dung chuyển khoản.
    ///
    /// Dò bằng Match chứ không so cả chuỗi: ngân hàng gần như luôn thêm chữ vào
    /// nội dung ("CT DEN:xxx MCC7K2M9 GD 123456"), và mỗi ngân hàng thêm một kiểu.
    ///
    /// IgnoreCase vì có ngân hàng chuyển hết nội dung sang chữ thường. Bảng ký tự
    /// phải khớp với RefAlphabet trong OrderService và BankQr bên frontend.
    /// </summary>
    private static readonly Regex RefPattern = new(
        "MCC[ACDEFGHJKMNPQRTUVWXY2346789]{5}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// SePay gọi vào đây mỗi khi tài khoản ngân hàng có biến động số dư.
    /// Khai báo URL này ở bước 1 của trình tạo webhook trên trang SePay.
    /// </summary>
    [HttpPost("sepay-payment")]
    public async Task<IActionResult> SePayPayment(
        [FromBody] SePayWebhookPayload payload, CancellationToken ct)
    {
        // ---- ① Xác thực -----------------------------------------------------
        if (!IsAuthorized())
        {
            // Log ở mức cảnh báo: endpoint này công khai trên Internet, người lạ
            // gõ vào là chuyện bình thường, nhưng gõ nhiều thì cần biết.
            _logger.LogWarning("Webhook SePay bị từ chối: sai hoặc thiếu khóa API. IP {Ip}",
                HttpContext.Connection.RemoteIpAddress);

            return Unauthorized(new { success = false, message = "Sai khóa API." });
        }

        // ---- ② Chống ghi nhận trùng (kiểm tra nhanh) ------------------------
        // Đường phổ biến nhất: lần gửi đầu đã xử lý xong nhưng mạng đứt trước khi
        // SePay nhận được mã 200, nên nó gửi lại. Trả 200 để nó dừng.
        if (await _db.PaymentTransactions.AnyAsync(t => t.GatewayId == payload.Id, ct))
        {
            _logger.LogInformation("Giao dịch SePay {Id} đã ghi nhận trước đó, bỏ qua.", payload.Id);
            return Ok(new { success = true, message = "Đã ghi nhận trước đó." });
        }

        // ---- ③ Ghi nhật ký giao dịch ----------------------------------------
        var tx = new PaymentTransaction
        {
            GatewayId       = payload.Id,
            Gateway         = payload.Gateway ?? "",
            AccountNumber   = payload.AccountNumber ?? "",
            SubAccount      = payload.SubAccount,
            TransferType    = payload.TransferType ?? "in",
            Amount          = (int)Math.Round(payload.TransferAmount),
            Content         = payload.Content ?? "",
            ReferenceCode   = payload.ReferenceCode,
            TransactionDate = ParseTransactionDate(payload.TransactionDate),

            // Giữ nguyên văn: đây là dữ liệu tiền bạc do bên thứ ba gửi tới, khi
            // đối soát lệch thì thứ duy nhất phân xử được là bản gốc.
            RawPayload      = JsonSerializer.Serialize(payload)
        };

        // ---- ④ Đối soát ------------------------------------------------------
        await ReconcileAsync(tx, ct);

        _db.PaymentTransactions.Add(tx);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateGatewayId(ex))
        {
            // Hai lần gửi lại chạy song song, cả hai cùng qua được bước ② rồi mới
            // đâm nhau ở chỉ mục duy nhất. Đây chính là lúc chỉ mục làm việc của
            // nó — coi như đã ghi nhận, trả 200 để SePay dừng.
            _logger.LogInformation(
                "Giao dịch SePay {Id} bị chặn bởi chỉ mục duy nhất (hai lần gửi song song).",
                payload.Id);

            return Ok(new { success = true, message = "Đã ghi nhận trước đó." });
        }

        _logger.LogInformation(
            "Webhook SePay {Id}: {Amount}đ, nội dung \"{Content}\" → {Status}. {Note}",
            payload.Id, tx.Amount, tx.Content, tx.MatchStatus, tx.Note);

        // Luôn 200 khi đã ghi nhận xong, kể cả giao dịch không khớp đơn nào:
        // việc của mình đã hoàn tất, phần còn lại là chủ quán đối soát tay.
        return Ok(new { success = true, matchStatus = tx.MatchStatus.ToString(), note = tx.Note });
    }

    // ==========================================================================
    //  ĐỐI SOÁT
    // ==========================================================================

    /// <summary>
    /// Gán giao dịch vào đơn hàng nếu khớp, và ghi lý do khi không khớp.
    ///
    /// Hàm này CHỈ đặt giá trị lên đối tượng, không tự lưu — người gọi lưu một
    /// lần cùng bản ghi giao dịch, để nhật ký và trạng thái đơn luôn đi cùng nhau
    /// trong một transaction.
    /// </summary>
    private async Task ReconcileAsync(PaymentTransaction tx, CancellationToken ct)
    {
        // Tiền ra khỏi tài khoản thì không liên quan gì tới việc khách trả đơn.
        // Vẫn ghi lại để sổ sách đầy đủ, chỉ không đem đi khớp.
        if (!string.Equals(tx.TransferType, "in", StringComparison.OrdinalIgnoreCase))
        {
            tx.MatchStatus = PaymentMatchStatus.NoReference;
            tx.Note = "Giao dịch tiền ra, không xét thanh toán đơn.";
            return;
        }

        var match = RefPattern.Match(tx.Content);
        if (!match.Success)
        {
            tx.MatchStatus = PaymentMatchStatus.NoReference;
            tx.Note = "Nội dung chuyển khoản không chứa mã tham chiếu của quán.";
            return;
        }

        // ToUpperInvariant vì regex bắt cả chữ thường, còn mã lưu trong đơn luôn in hoa.
        tx.DetectedRef = match.Value.ToUpperInvariant();

        var order = await _db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PaymentRef == tx.DetectedRef, ct);

        if (order is null)
        {
            tx.MatchStatus = PaymentMatchStatus.OrderNotFound;
            tx.Note = $"Không có đơn nào mang mã {tx.DetectedRef}.";
            return;
        }

        tx.OrderId = order.Id;

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            // Không ghi đè: đơn đã thanh toán rồi mà tiền lại về lần nữa là
            // chuyện cần người xem, không phải chuyện để hệ thống tự quyết.
            tx.MatchStatus = PaymentMatchStatus.AlreadyPaid;
            tx.Note = $"Đơn {order.Code} đã được đánh dấu thanh toán từ trước. Cần đối soát tay.";
            return;
        }

        // Chuyển THIẾU thì không đánh dấu đã trả — nhưng vẫn gắn vào đơn để nhân
        // viên thấy "khách đã chuyển 40.000/45.000" thay vì tưởng chưa trả gì.
        if (tx.Amount < order.GrandTotal)
        {
            tx.MatchStatus = PaymentMatchStatus.AmountMismatch;
            tx.Note = $"Đơn {order.Code} cần {order.GrandTotal:N0}đ, "
                    + $"khách chuyển {tx.Amount:N0}đ — còn thiếu {order.GrandTotal - tx.Amount:N0}đ.";
            return;
        }

        // Chuyển DƯ vẫn tính là đã trả đủ. Giữ nguyên số tiền thật vào PaidAmount
        // để sổ ngân hàng và sổ quán khớp nhau đến từng đồng.
        order.PaymentStatus    = PaymentStatus.Paid;
        order.PaidAt           = DateTime.UtcNow;
        order.PaidAmount       = tx.Amount;
        order.PaymentGatewayId = tx.GatewayId;
        order.UpdatedAt        = DateTime.UtcNow;

        tx.MatchStatus = PaymentMatchStatus.Matched;
        tx.Note = tx.Amount > order.GrandTotal
            ? $"Đơn {order.Code} đã thanh toán. Khách chuyển dư {tx.Amount - order.GrandTotal:N0}đ."
            : $"Đơn {order.Code} đã thanh toán đủ {tx.Amount:N0}đ.";
    }

    // ==========================================================================
    //  TIỆN ÍCH
    // ==========================================================================

    /// <summary>
    /// SePay gửi khóa ở dạng <c>Authorization: Apikey &lt;khóa&gt;</c>.
    ///
    /// Khóa lấy từ biến môi trường SEPAY_API_KEY. CHƯA KHAI BÁO KHÓA THÌ TỪ CHỐI
    /// TẤT CẢ — endpoint này nằm công khai trên Internet và có quyền đánh dấu đơn
    /// đã thanh toán, để nó mở toang lúc quên cấu hình là ai cũng báo được
    /// "tôi trả rồi".
    /// </summary>
    private bool IsAuthorized()
    {
        var expected = Environment.GetEnvironmentVariable("SEPAY_API_KEY");
        if (string.IsNullOrWhiteSpace(expected)) return false;

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;

        // Chấp nhận cả "Apikey xxx" lẫn "Bearer xxx": trang cấu hình SePay từng
        // đổi cách gọi, và khóa mới là thứ quyết định chứ không phải nhãn.
        var value = header.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase)
                 || header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : header.Trim();

        // So sánh theo thời gian cố định: so bằng == sẽ dừng ngay ở ký tự lệch
        // đầu tiên, đủ để dò ra khóa từng ký tự một qua thời gian phản hồi.
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(value),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// SePay gửi "2023-03-25 14:02:37" theo GIỜ VIỆT NAM, không kèm múi giờ.
    /// Cột lưu là timestamptz nên phải quy về UTC, nếu không mọi giao dịch sẽ
    /// lệch 7 tiếng và báo cáo theo ngày sẽ sai ở hai đầu ngày.
    /// </summary>
    private static DateTime ParseTransactionDate(string? raw)
    {
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var vn))
            return VietnamTime.ToUtc(vn);

        // Không đọc được thì lấy giờ nhận webhook — lệch vài giây còn hơn để trống.
        return DateTime.UtcNow;
    }

    /// <summary>Lỗi khi lưu có phải do đâm vào chỉ mục duy nhất trên gateway_id không.</summary>
    private static bool IsDuplicateGatewayId(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}

/// <summary>
/// Payload webhook của SePay.
///
/// Chỉ khai báo những trường thực sự dùng tới; SePay còn gửi thêm "code",
/// "accumulated", "description"… nhưng chúng đã nằm nguyên trong RawPayload nên
/// không cần ánh xạ. Thêm trường mới ở phía SePay cũng không làm hỏng endpoint.
/// </summary>
public class SePayWebhookPayload
{
    /// <summary>Id giao dịch do SePay cấp. Khóa chống ghi nhận trùng.</summary>
    public long Id { get; set; }

    /// <summary>Tên ngân hàng, VD "Vietcombank".</summary>
    public string? Gateway { get; set; }

    /// <summary>"yyyy-MM-dd HH:mm:ss" theo giờ Việt Nam.</summary>
    public string? TransactionDate { get; set; }

    public string? AccountNumber { get; set; }
    public string? SubAccount { get; set; }

    /// <summary>"in" = tiền vào, "out" = tiền ra.</summary>
    public string? TransferType { get; set; }

    /// <summary>
    /// Số tiền. Khai báo decimal chứ không phải int vì SePay gửi kiểu số của
    /// JSON — có bản ghi về dạng 45000.00, ép thẳng sang int sẽ lỗi phân giải.
    /// </summary>
    public decimal TransferAmount { get; set; }

    /// <summary>Nội dung chuyển khoản — nơi chứa mã tham chiếu.</summary>
    public string? Content { get; set; }

    /// <summary>Mã giao dịch của ngân hàng, VD "MBVCB.3278907687".</summary>
    public string? ReferenceCode { get; set; }
}
