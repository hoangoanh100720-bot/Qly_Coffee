using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  XÁC THỰC
// ==============================================================================

[Route("api/auth")]
public class AuthController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, ILogger<AuthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);

        // ⚠️ Trả về CÙNG MỘT thông báo cho cả hai trường hợp sai email và sai mật khẩu.
        // Nếu phân biệt, kẻ tấn công có thể dò xem email nào tồn tại trong hệ thống.
        if (user is null || string.IsNullOrEmpty(user.PasswordHash) ||
            !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            _logger.LogWarning("Đăng nhập thất bại cho {Email}", email);
            return Fail<LoginResult>(401, "INVALID_CREDENTIALS",
                "Email hoặc mật khẩu không đúng.");
        }

        var (token, expiresAt) = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId      = user.Id,
            Token       = refreshToken,
            ExpiresAt   = DateTime.UtcNow.AddDays(EnvInt("JWT_REFRESH_EXPIRY_DAYS", 30)),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new LoginResult(token, refreshToken, expiresAt,
            new UserDto(user.Id, user.Email, user.FullName,
                (int)user.Role, RoleLabel(user.Role), user.AvatarUrl)));
    }

    private (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET")!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var expiresAt = DateTime.UtcNow.AddMinutes(EnvInt("JWT_EXPIRY_MINUTES", 480));

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("userId", user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            // Dùng tên vai trò dạng chữ để thuộc tính [Authorize(Roles="Manager")] hoạt động
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "QlyCoffee.Api",
            audience: Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "QlyCoffee.Client",
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string RoleLabel(UserRole r) => r switch
    {
        UserRole.Owner    => "Chủ quán",
        UserRole.Manager  => "Quản lý",
        UserRole.Staff    => "Nhân viên",
        _                 => "Khách hàng"
    };

    private static int EnvInt(string key, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;
}

// ==============================================================================
//  ENDPOINT CHO JOB NỀN
//
//  Bảo vệ bằng khóa bí mật trong header X-Cron-Secret thay vì JWT, vì job chạy
//  bằng máy chứ không phải người dùng đăng nhập.
// ==============================================================================

[Route("api/cron")]
public class CronController : BaseApiController
{
    private readonly IDailyPlanService _plans;
    private readonly IInventoryService _inventory;
    private readonly IAvailabilityService _availability;
    private readonly ILogger<CronController> _logger;

    public CronController(
        IDailyPlanService plans,
        IInventoryService inventory,
        IAvailabilityService availability,
        ILogger<CronController> logger)
    {
        _plans = plans;
        _inventory = inventory;
        _availability = availability;
        _logger = logger;
    }

    /// <summary>Job 23:00 — sinh bản kế hoạch cuối ngày.</summary>
    [HttpPost("daily-plan")]
    public async Task<IActionResult> DailyPlan(CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var plan = await _plans.GenerateAsync(CurrentStoreId, null, ct);
        return Ok(new { plan.Id, plan.BusinessDate, plan.TotalValueAtRisk });
    }

    /// <summary>Job 08:00 — đánh dấu lô quá hạn trước khi quán mở cửa.</summary>
    [HttpPost("expiry-check")]
    public async Task<IActionResult> ExpiryCheck(CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var count = await _inventory.ExpireOverdueLotsAsync(CurrentStoreId, ct);
        await _availability.RecomputeAllAsync(CurrentStoreId, ct);
        return Ok(new { expiredLots = count });
    }

    /// <summary>Job mỗi 15 phút — đồng bộ lại trạng thái khả dụng của món.</summary>
    [HttpPost("refresh-availability")]
    public async Task<IActionResult> RefreshAvailability(CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        await _availability.RecomputeAllAsync(CurrentStoreId, ct);
        return Ok(new { ok = true });
    }

    private bool IsAuthorized()
    {
        var expected = Environment.GetEnvironmentVariable("CRON_SECRET");
        if (string.IsNullOrEmpty(expected)) return false;

        var provided = Request.Headers["X-Cron-Secret"].ToString();

        // So sánh theo thời gian hằng số để chống tấn công dò từng ký tự
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}

// ==============================================================================
//  JOB NỀN TỰ CHẠY TRONG ỨNG DỤNG
//
//  Vì sao chạy trong ứng dụng thay vì cron hệ thống: một quán một máy chủ thì
//  đơn giản hơn nhiều, không cần cấu hình thêm. Khi lên nhiều máy chủ thì phải
//  chuyển sang Hangfire hoặc cron ngoài, nếu không job sẽ chạy trùng nhiều lần.
// ==============================================================================

public class ScheduledJobsService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledJobsService> _logger;

    // Mốc giờ đã chạy trong ngày — chống chạy lại nhiều lần trong cùng một giờ
    private string _lastPlanDate = "";
    private string _lastExpiryDate = "";
    private DateTime _lastAvailabilityRun = DateTime.MinValue;

    public ScheduledJobsService(IServiceProvider services, ILogger<ScheduledJobsService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job nền đã khởi động");

        // Chờ ứng dụng khởi động xong và migration chạy xong
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Job lỗi KHÔNG được làm sập ứng dụng — quán vẫn phải bán được hàng
                _logger.LogError(ex, "Job nền gặp lỗi, sẽ thử lại ở chu kỳ sau");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunDueJobsAsync(CancellationToken ct)
    {
        var now = VietnamTime.Now();
        var today = now.ToString("yyyy-MM-dd");

        var storeIdRaw = Environment.GetEnvironmentVariable("DEFAULT_STORE_ID");
        if (!Guid.TryParse(storeIdRaw, out var storeId)) return;

        using var scope = _services.CreateScope();

        // ---- Job 23:00 — sinh kế hoạch cuối ngày ---------------------------
        var planHour = EnvInt("DAILY_PLAN_HOUR", 23);
        if (now.Hour >= planHour && _lastPlanDate != today)
        {
            _logger.LogInformation("Chạy job sinh kế hoạch ngày {Date}", today);
            var plans = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();
            await plans.GenerateAsync(storeId, today, ct);
            _lastPlanDate = today;
        }

        // ---- Job 08:00 — kiểm tra hạn dùng ---------------------------------
        var expiryHour = EnvInt("EXPIRY_CHECK_HOUR", 8);
        if (now.Hour >= expiryHour && _lastExpiryDate != today)
        {
            _logger.LogInformation("Chạy job kiểm tra hạn dùng");
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var availability = scope.ServiceProvider.GetRequiredService<IAvailabilityService>();
            await inventory.ExpireOverdueLotsAsync(storeId, ct);
            await availability.RecomputeAllAsync(storeId, ct);
            _lastExpiryDate = today;
        }

        // ---- Job mỗi 15 phút — đồng bộ khả dụng ----------------------------
        //
        //  Quét hạn dùng CHẠY LẠI Ở ĐÂY, không chỉ ở mốc 08:00.
        //
        //  Lý do: từ khi có sơ chế, kho chứa cả những lô hết hạn theo GIỜ — bình
        //  cốt trà ủ lúc 08:30 hỏng lúc 14:30 chứ không phải nửa đêm. Job mỗi
        //  ngày một lần thì suốt buổi chiều hệ thống vẫn cho bán trà đã thiu.
        //
        //  Đặt ngay TRƯỚC phần tính lại khả dụng để hai việc ăn khớp: mẻ vừa bị
        //  đánh dấu hết hạn thì món dùng nó lập tức chuyển sang "chưa sơ chế".
        //  Hàm chạy lại bao nhiêu lần cũng vô hại — lô đã Expired không lọt vào
        //  truy vấn nữa.
        var interval = TimeSpan.FromMinutes(EnvInt("AVAILABILITY_REFRESH_MINUTES", 15));
        if (DateTime.UtcNow - _lastAvailabilityRun >= interval)
        {
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var availability = scope.ServiceProvider.GetRequiredService<IAvailabilityService>();

            var expired = await inventory.ExpireOverdueLotsAsync(storeId, ct);
            if (expired > 0)
                _logger.LogInformation(
                    "Quét giữa ngày: {Count} lô vừa hết hạn (thường là mẻ sơ chế quá giờ)", expired);

            await availability.RecomputeAllAsync(storeId, ct);
            _lastAvailabilityRun = DateTime.UtcNow;
        }
    }

    private static int EnvInt(string key, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;
}
