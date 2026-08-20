using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  TẢI ẢNH MÓN
//
//  Ảnh chụp thật của quán được tải lên đây, lưu vào wwwroot/uploads và gán vào
//  cột products.image_url. Giao diện tự ưu tiên ảnh thật; hình vẽ SVG chỉ hiện
//  khi món CHƯA có ảnh, nên có thể chụp và thay dần từng món.
//
//  BA TẦNG KIỂM TRA, KHÔNG TIN PHẦN NÀO DO CLIENT GỬI:
//    1. Đuôi file phải nằm trong danh sách cho phép
//    2. Content-Type phải khớp đuôi file
//    3. Đọc "chữ ký" mấy byte đầu để xác nhận đúng là ảnh
//
//  Tầng 3 mới là tầng thật sự có tác dụng: đuôi file và Content-Type đều do
//  client tự khai, đổi tên shell.php thành anh.jpg là qua được hai tầng đầu.
//  Tên file lưu xuống cũng do server tự sinh (uuid v7), không bao giờ dùng lại
//  tên client gửi — chặn luôn cả đường vượt thư mục (../../).
// ==============================================================================

[ApiController]
[Route("api/admin/media")]
[Authorize(Roles = "Manager,Owner")]
public class MediaController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MediaController> _logger;

    public MediaController(AppDbContext db, IWebHostEnvironment env, ILogger<MediaController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    /// <summary>Đuôi file cho phép, kèm Content-Type hợp lệ tương ứng.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".webp"] = "image/webp"
    };

    /// <summary>
    /// Chữ ký byte đầu file (magic number) của từng định dạng ảnh.
    /// WebP đặc biệt: 4 byte "RIFF", bỏ qua 4 byte độ dài, rồi tới "WEBP".
    /// </summary>
    private static bool LooksLikeImage(byte[] head)
    {
        if (head.Length < 12) return false;

        // JPEG: FF D8 FF
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
         && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return true;

        // WebP: "RIFF" .... "WEBP"
        if (head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
         && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return true;

        return false;
    }

    /// <summary>
    /// Tải ảnh cho một món. Trả về đường dẫn để gán vào products.image_url.
    /// Có productId thì gán luôn vào món đó và xóa ảnh cũ.
    /// </summary>
    [HttpPost("product-image")]
    [RequestSizeLimit(10 * 1024 * 1024)] // chặn sớm ở tầng Kestrel, trước khi đọc vào bộ nhớ
    public async Task<IActionResult> UploadProductImage(
        IFormFile file,
        [FromQuery] Guid? productId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Fail<object>(400, "NO_FILE", "Chưa chọn ảnh.");

        var maxMb = int.TryParse(Environment.GetEnvironmentVariable("MAX_UPLOAD_MB"), out var m) ? m : 5;
        if (file.Length > maxMb * 1024L * 1024L)
            return Fail<object>(400, "TOO_LARGE",
                $"Ảnh {file.Length / 1024 / 1024}MB, vượt giới hạn {maxMb}MB. Nén bớt rồi thử lại.");

        // ---- Tầng 1: đuôi file --------------------------------------------
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedTypes.TryGetValue(ext, out var expectedMime))
            return Fail<object>(400, "BAD_TYPE",
                "Chỉ nhận ảnh .jpg, .png hoặc .webp.");

        // ---- Tầng 2: Content-Type phải khớp đuôi --------------------------
        if (!string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase))
            return Fail<object>(400, "TYPE_MISMATCH",
                $"Đuôi file là {ext} nhưng nội dung khai báo là {file.ContentType}.");

        // ---- Tầng 3: chữ ký byte đầu (tầng duy nhất client không giả được) -
        var head = new byte[12];
        await using (var peek = file.OpenReadStream())
        {
            var read = await peek.ReadAsync(head.AsMemory(0, 12), ct);
            if (read < 12 || !LooksLikeImage(head))
                return Fail<object>(400, "NOT_AN_IMAGE",
                    "File này không phải ảnh thật, dù tên file có đuôi ảnh.");
        }

        // ---- Ghi xuống đĩa -------------------------------------------------
        // Tên file do SERVER sinh. Không bao giờ dùng file.FileName để đặt tên:
        // client có thể gửi "../../appsettings.json" hoặc tên trùng đè ảnh khác.
        // STORAGE_LOCAL_PATH trong .env trỏ tới wwwroot/uploads; ảnh món nằm ở
        // thư mục con products/ để sau này còn chỗ cho ảnh nguyên liệu, logo…
        var absoluteDir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"),
                                       "uploads", "products");
        Directory.CreateDirectory(absoluteDir);

        var fileName = $"{GuidV7.New():N}{ext.ToLowerInvariant()}";
        var absolutePath = Path.Combine(absoluteDir, fileName);

        await using (var dest = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(dest, ct);
        }

        // Đường dẫn công khai. Backend phục vụ file tĩnh nên frontend chỉ cần
        // ghép với API_BASE_URL là hiện được ảnh.
        var publicUrl = $"/uploads/products/{fileName}";

        // ---- Gán vào món (nếu có chỉ định) ---------------------------------
        string? replacedOld = null;
        if (productId is Guid pid)
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.Id == pid && p.StoreId == CurrentStoreId, ct);

            if (product is null)
            {
                // Đã ghi file rồi mới biết món không tồn tại → dọn lại cho sạch
                System.IO.File.Delete(absolutePath);
                return Fail<object>(404, "PRODUCT_NOT_FOUND", "Không tìm thấy món này.");
            }

            replacedOld = product.ImageUrl;
            product.ImageUrl = publicUrl;
            await _db.SaveChangesAsync(ct);

            // Xóa ảnh cũ để thư mục không phình theo mỗi lần thay ảnh.
            // Chỉ xóa file nằm trong uploads/products — không đụng ảnh liên kết ngoài.
            if (!string.IsNullOrEmpty(replacedOld) && replacedOld.StartsWith("/uploads/products/"))
            {
                var oldPath = Path.Combine(absoluteDir, Path.GetFileName(replacedOld));
                if (System.IO.File.Exists(oldPath))
                {
                    try { System.IO.File.Delete(oldPath); }
                    catch (IOException ex) { _logger.LogWarning(ex, "Không xóa được ảnh cũ {Path}", oldPath); }
                }
            }
        }

        _logger.LogInformation("Đã tải ảnh {File} ({Size} KB) cho món {ProductId}",
            fileName, file.Length / 1024, productId);

        return Ok(new { url = publicUrl, sizeKb = file.Length / 1024, replacedOld });
    }

    /// <summary>
    /// Gỡ ảnh khỏi món, quay về dùng hình vẽ SVG.
    /// Dùng khi ảnh chụp chưa ưng — SVG vẫn tử tế hơn một ảnh xấu.
    /// </summary>
    [HttpDelete("product-image/{productId:guid}")]
    public async Task<IActionResult> RemoveProductImage(Guid productId, CancellationToken ct)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == CurrentStoreId, ct);

        if (product is null)
            return Fail<object>(404, "PRODUCT_NOT_FOUND", "Không tìm thấy món này.");

        var old = product.ImageUrl;
        product.ImageUrl = null;
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(old) && old.StartsWith("/uploads/products/"))
        {
            var dir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"),
                                   "uploads", "products");
            var path = Path.Combine(dir, Path.GetFileName(old));
            if (System.IO.File.Exists(path))
            {
                try { System.IO.File.Delete(path); }
                catch (IOException ex) { _logger.LogWarning(ex, "Không xóa được ảnh {Path}", path); }
            }
        }

        return Ok(true);
    }

    /// <summary>
    /// Gán ảnh bằng đường dẫn có sẵn thay vì tải file lên.
    /// Dùng khi ảnh đã nằm trên CDN, hoặc khi lấy ảnh từ kho ảnh miễn phí.
    /// </summary>
    [HttpPut("product-image/{productId:guid}")]
    public async Task<IActionResult> SetProductImageUrl(
        Guid productId,
        [FromBody] SetImageUrlRequest req,
        CancellationToken ct)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == CurrentStoreId, ct);

        if (product is null)
            return Fail<object>(404, "PRODUCT_NOT_FOUND", "Không tìm thấy món này.");

        var url = req.Url?.Trim();

        // Chuỗi rỗng nghĩa là gỡ ảnh, quay về hình vẽ
        if (string.IsNullOrEmpty(url))
        {
            product.ImageUrl = null;
        }
        else
        {
            // Chỉ nhận http(s) hoặc đường dẫn nội bộ. Chặn javascript:, data:
            // — nếu để lọt, chúng sẽ chạy trong trình duyệt của khách.
            var ok = url.StartsWith("/uploads/", StringComparison.Ordinal)
                  || Uri.TryCreate(url, UriKind.Absolute, out var u)
                     && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

            if (!ok)
                return Fail<object>(400, "BAD_URL",
                    "Đường dẫn ảnh phải bắt đầu bằng http://, https:// hoặc /uploads/.");

            product.ImageUrl = url;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { url = product.ImageUrl });
    }

    public record SetImageUrlRequest(string? Url);
}
