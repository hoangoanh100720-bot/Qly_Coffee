using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QlyCoffee.Api.Ai;
using QlyCoffee.Api.Controllers;
using QlyCoffee.Application.Services;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Infrastructure.Seed;
using Serilog;

// ==============================================================================
//  ĐIỂM KHỞI ĐỘNG BACKEND
//
//  TOÀN BỘ CẤU HÌNH ĐỌC TỪ FILE .env Ở THƯ MỤC GỐC.
//  Không có chuỗi kết nối hay khóa API nào viết cứng trong mã nguồn.
//  Đổi máy chủ, đổi khóa, đổi tham số nghiệp vụ đều chỉ sửa .env rồi khởi động lại.
// ==============================================================================

// ---- Nạp .env TRƯỚC khi dựng builder --------------------------------------
// Tìm ngược lên các thư mục cha vì khi chạy `dotnet run` từ thư mục dự án,
// thư mục làm việc là backend/QlyCoffee.Api chứ không phải gốc repo.
LoadEnvFile();

var builder = WebApplication.CreateBuilder(args);

// ---- Cổng lắng nghe ---------------------------------------------------------
// Lấy từ API_HTTP_PORT trong .env để cổng và CORS_ALLOWED_ORIGINS luôn khớp nhau.
// Chỉ áp dụng khi chưa có ASPNETCORE_URLS — như vậy launchSettings, Docker hay
// reverse proxy vẫn đè lên được khi cần.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls($"http://localhost:{Cfg("API_HTTP_PORT", "5080")}");

// ---- Ghi nhật ký ------------------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Is(ParseLogLevel(Cfg("LOG_LEVEL", "Information")))
    // EF Core mặc định log rất nhiều, chỉ bật khi thực sự cần debug SQL
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
        Cfg("LOG_SQL", "false") == "true"
            ? Serilog.Events.LogEventLevel.Information
            : Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/qly-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// ==============================================================================
//  DỊCH VỤ
// ==============================================================================

// ---- Cơ sở dữ liệu ---------------------------------------------------------
var connectionString = Cfg("CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    // Không đặt chuỗi đầy đủ thì tự ghép từ các biến rời — tiện cho môi trường dev
    connectionString =
        $"Host={Cfg("DB_HOST", "localhost")};" +
        $"Port={Cfg("DB_PORT", "5432")};" +
        $"Database={Cfg("DB_NAME", "qly_coffee")};" +
        $"Username={Cfg("DB_USER", "postgres")};" +
        $"Password={Cfg("DB_PASSWORD", "postgres")}";
}

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(connectionString, npg =>
    {
        // KHÔNG bật EnableRetryOnFailure.
        //
        // Hệ thống mở transaction thủ công ở 7 chỗ, đều là đường quan trọng nhất:
        // trừ kho FEFO (Serializable + SELECT FOR UPDATE), hoàn kho khi hủy đơn,
        // nhập kho, ghi hao hụt, duyệt kế hoạch. EF Core TỪ CHỐI chạy transaction
        // thủ công khi có execution strategy — nó ném lỗi ngay, làm hỏng đúng
        // tính năng lõi.
        //
        // Muốn có cả hai thì phải bọc từng transaction trong
        // Database.CreateExecutionStrategy().ExecuteAsync(...). Đáng làm khi
        // chuyển sang DB đám mây (Neon, Supabase, RDS) nơi mạng chớp là chuyện
        // thường. Với Postgres chạy cùng máy hoặc cùng mạng LAN của quán thì
        // retry gần như không có tác dụng, mà đổi lại là rủi ro thật.
        //
        // Mất kết nối tạm thời sẽ báo lỗi ra client. Client gửi lại kèm
        // idempotency_key cũ nên không bao giờ trừ kho hai lần.
        npg.CommandTimeout(30);
    });

    if (builder.Environment.IsDevelopment())
        opt.EnableSensitiveDataLogging();
});

// ---- Tham số nghiệp vụ (đọc từ .env, chỉnh được mà không sửa code) ---------
builder.Services.Configure<PlanningOptions>(o =>
{
    o.WasteRiskHorizonDays   = EnvInt("WASTE_RISK_HORIZON_DAYS", 14);
    o.MinMarginPercent       = EnvInt("MIN_MARGIN_PERCENT", 15);
    o.DefaultPriceElasticity = EnvDouble("DEFAULT_PRICE_ELASTICITY", -1.6);
    o.MaxSuggestionsPerPlan  = EnvInt("MAX_SUGGESTIONS_PER_PLAN", 6);
    o.ForecastLookbackDays   = EnvInt("FORECAST_LOOKBACK_DAYS", 28);
    o.ForecastEwmaAlpha      = EnvDouble("FORECAST_EWMA_ALPHA", 0.25);
});

builder.Services.Configure<ClaudeOptions>(o =>
{
    o.ApiKey    = Cfg("ANTHROPIC_API_KEY", "");
    o.Model     = Cfg("ANTHROPIC_MODEL", "claude-opus-5");
    o.MaxTokens = EnvInt("ANTHROPIC_MAX_TOKENS", 8000);
    o.Enabled   = Cfg("AI_ENABLED", "true") == "true";
});

// ---- Dịch vụ nghiệp vụ ------------------------------------------------------
// Thứ tự đăng ký không quan trọng, nhưng thứ tự phụ thuộc thì có:
//   Inventory ← Recipe ← Availability ← Order
//   WasteRisk + PromotionPlanner + AiNarrator ← DailyPlan
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IWasteRiskService, WasteRiskService>();
builder.Services.AddScoped<IPromotionPlanner, PromotionPlanner>();
builder.Services.AddScoped<IAiNarrator, ClaudeNarrator>();
builder.Services.AddScoped<IDailyPlanService, DailyPlanService>();

// ---- Xác thực JWT -----------------------------------------------------------
var jwtSecret = Cfg("JWT_SECRET", "");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "JWT_SECRET phải có ít nhất 32 ký tự. Sinh khóa mới bằng: openssl rand -base64 48");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer   = Cfg("JWT_ISSUER", "QlyCoffee.Api"),
            ValidAudience = Cfg("JWT_AUDIENCE", "QlyCoffee.Client"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // Mặc định .NET cho phép lệch 5 phút; siết còn 30 giây cho chặt
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ---- CORS -------------------------------------------------------------------
// Blazor WebAssembly chạy ở cổng khác backend nên bắt buộc phải mở CORS.
var allowedOrigins = Cfg("CORS_ALLOWED_ORIGINS", "http://localhost:5180")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(o => o.AddPolicy("QlyClient", p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---- API --------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Không ghi null vào JSON — giảm dung lượng phản hồi đáng kể
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Qly Coffee API", Version = "v1" });
});

// ---- Job nền ----------------------------------------------------------------
builder.Services.AddHostedService<ScheduledJobsService>();

var app = builder.Build();

// ==============================================================================
//  PIPELINE
// ==============================================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Qly Coffee API v1"));
}

app.UseSerilogRequestLogging();
app.UseCors("QlyClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Phục vụ ảnh người dùng tải lên
app.UseStaticFiles();

// ---- Khởi tạo cơ sở dữ liệu -------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Đang áp dụng migration…");
        await db.Database.MigrateAsync();

        if (Cfg("SEED_ENABLED", "false") == "true")
        {
            logger.LogInformation("Đang nạp dữ liệu mẫu…");
            await DatabaseSeeder.SeedAsync(db, new SeedOptions
            {
                AdminEmail    = Cfg("SEED_ADMIN_EMAIL", "admin@qlycoffee.vn"),
                AdminPassword = Cfg("SEED_ADMIN_PASSWORD", "Admin@123456"),
                AdminName     = Cfg("SEED_ADMIN_NAME", "Chủ quán"),
                StoreName     = Cfg("STORE_NAME", "Qly Coffee"),
                StoreSlug     = Cfg("STORE_SLUG", "qly-coffee"),
                StoreAddress  = Cfg("STORE_ADDRESS", ""),
                StorePhone    = Cfg("STORE_PHONE", ""),
                HistoryDays   = EnvInt("SEED_HISTORY_DAYS", 28)
            }, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Không khởi tạo được cơ sở dữ liệu");
        throw;
    }
}

Log.Information("Qly Coffee API sẵn sàng tại {Url}", Cfg("API_BASE_URL", "http://localhost:5080"));

await app.RunAsync();

// ==============================================================================
//  HÀM TIỆN ÍCH ĐỌC BIẾN MÔI TRƯỜNG
// ==============================================================================

/// <summary>
/// Tìm và nạp file .env. Đi ngược lên tối đa 5 cấp thư mục cha vì thư mục
/// làm việc lúc chạy phụ thuộc vào cách khởi động (dotnet run, IDE, docker).
/// </summary>
static void LoadEnvFile()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

    for (int i = 0; i < 5 && dir is not null; i++)
    {
        var path = Path.Combine(dir.FullName, ".env");
        if (File.Exists(path))
        {
            Env.Load(path);
            Console.WriteLine($"[cấu hình] Đã nạp {path}");
            return;
        }
        dir = dir.Parent;
    }

    Console.WriteLine("[cấu hình] CẢNH BÁO: không tìm thấy file .env, dùng giá trị mặc định");
}

static string Cfg(string key, string fallback = "")
    => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

static int EnvInt(string key, int fallback)
    => int.TryParse(Cfg(key), out var v) ? v : fallback;

static double EnvDouble(string key, double fallback)
    => double.TryParse(Cfg(key), System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

static Serilog.Events.LogEventLevel ParseLogLevel(string value) => value.ToLowerInvariant() switch
{
    "trace" or "verbose" => Serilog.Events.LogEventLevel.Verbose,
    "debug"              => Serilog.Events.LogEventLevel.Debug,
    "warning"            => Serilog.Events.LogEventLevel.Warning,
    "error"              => Serilog.Events.LogEventLevel.Error,
    _                    => Serilog.Events.LogEventLevel.Information
};
