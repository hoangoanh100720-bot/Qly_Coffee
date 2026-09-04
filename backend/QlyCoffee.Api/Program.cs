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

builder.Services.Configure<BarOptions>(o =>
{
    o.Stations       = EnvInt("BAR_STATIONS", 2);
    o.HandoffSeconds = EnvInt("BAR_HANDOFF_SECONDS", 45);
    o.BatchFactor    = EnvDouble("BAR_BATCH_FACTOR", 0.55);
    o.RoundToSeconds = EnvInt("BAR_ROUND_SECONDS", 60);
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
//   Inventory ← Prep
//   Inventory ← Recipe ← Availability ← Order
//   WasteRisk + PromotionPlanner + AiNarrator ← DailyPlan
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPrepService, PrepService>();
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IBarQueueService, BarQueueService>();
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
    c.SwaggerDoc("v1", new() { Title = "Một Chút Coffee API", Version = "v1" });
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Một Chút Coffee API v1"));
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
                StoreName     = Cfg("STORE_NAME", "Một Chút Coffee"),
                StoreSlug     = Cfg("STORE_SLUG", "mot-chut-coffee"),
                StoreAddress  = Cfg("STORE_ADDRESS", ""),
                StorePhone    = Cfg("STORE_PHONE", ""),
                HistoryDays   = EnvInt("SEED_HISTORY_DAYS", 28),

                // Thuế GTGT — xem QlyCoffee.Shared/Tax.cs để biết căn cứ pháp lý
                TaxMode        = EnvInt("STORE_TAX_MODE", QlyCoffee.Shared.VatPolicy.DefaultMode),
                VatRatePercent = EnvInt("STORE_VAT_RATE", QlyCoffee.Shared.VatPolicy.DefaultRatePercent),
                TaxCode        = Cfg("STORE_TAX_CODE", "")
            }, logger);
        }

        // ---- Đồng bộ thực đơn vào database ĐÃ CÓ dữ liệu -------------------
        //
        //  Seeder ở trên chỉ chạy khi database còn trống. Nhưng thực đơn thì lớn
        //  dần theo thời gian, và quán đang chạy không thể xóa database đi để
        //  nhận món mới — mất hết đơn hàng và sổ kho.
        //
        //  MenuSync bơm phần còn thiếu vào, chạy lại bao nhiêu lần cũng như nhau.
        //  Nó KHÔNG BAO GIỜ ghi đè giá bán mà chủ quán đã tự chỉnh.
        if (Cfg("MENU_SYNC_ENABLED", "false") == "true")
        {
            var storeId = Guid.TryParse(Cfg("DEFAULT_STORE_ID", ""), out var sid)
                ? sid
                : (await db.Stores.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync())?.Id ?? Guid.Empty;

            if (storeId == Guid.Empty)
            {
                logger.LogWarning("MENU_SYNC_ENABLED=true nhưng chưa có cửa hàng nào — bỏ qua");
            }
            else
            {
                var result = await MenuSync.ApplyAsync(
                    db, storeId, logger,
                    // Ghi đè công thức của món ĐÃ CÓ. Mặc định tắt vì chủ quán có
                    // quyền sửa định lượng cho hợp khẩu vị khách của mình.
                    refreshRecipes: Cfg("MENU_SYNC_RECIPES", "false") == "true",
                    // Tạo lô tồn kho mở đầu cho nguyên liệu mới. Ở quán thật đây là
                    // bịa ra hàng hóa không có — chỉ bật ở máy phát triển.
                    openingStock: Cfg("MENU_SYNC_OPENING_STOCK", "false") == "true");

                // Có món mới hoặc công thức mới thì giá vốn và trạng thái còn bán
                // được đều phải tính lại, nếu không món mới hiện "tạm hết" mãi mãi.
                if (result.NewProducts.Count > 0 || result.RefreshedRecipeCount > 0)
                {
                    var recipe = scope.ServiceProvider.GetRequiredService<IRecipeService>();
                    var availability = scope.ServiceProvider.GetRequiredService<IAvailabilityService>();

                    await recipe.RecomputeAllProductCostsAsync(storeId);
                    await availability.RecomputeAllAsync(storeId);

                    logger.LogInformation("Đã tính lại giá vốn và tồn kho khả dụng cho toàn bộ thực đơn");
                }
            }
        }

        // ---- Thông tin bên bán và cấu hình thuế -----------------------------
        //
        //  Đồng bộ từ .env vào bản ghi cửa hàng mỗi lần khởi động, vì hai lý do:
        //
        //  1. THUẾ SUẤT DO QUỐC HỘI QUYẾT, không do lập trình viên. Mức 8% hiện
        //     hành hết hiệu lực 31/12/2026; sau đó nếu không gia hạn thì về 10%.
        //     Phải đổi được bằng một dòng .env, không phải build lại.
        //
        //  2. TÊN VÀ ĐỊA CHỈ BÊN BÁN GIỜ ĐƯỢC IN LÊN BẢNG KÊ TIỀN HÀNG. Trước
        //     đây các cột này chỉ nằm im trong database nên không ai để ý chúng
        //     đã lệch khỏi .env — cho tới khi bảng kê hiện ra một cái tên khác
        //     hẳn tên trên tiêu đề trang. Trên chứng từ giao khách, sai tên bên
        //     bán không phải lỗi hiển thị mà là sai thông tin.
        //
        //  Seeder chỉ đặt các giá trị này ở lần khởi tạo đầu tiên, nên nếu không
        //  có bước này thì mọi thay đổi trong .env về sau đều không tới nơi.
        if (Cfg("STORE_INFO_APPLY", "false") == "true")
        {
            var store = await db.Stores.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync();
            if (store is not null)
            {
                // Chỉ ghi đè khi .env thực sự có giá trị. Biến để trống nghĩa là
                // "không quản lý trường này", chứ không phải "xóa nó đi".
                void ApplyText(string key, Action<string> set)
                {
                    var value = Cfg(key, "");
                    if (!string.IsNullOrWhiteSpace(value)) set(value.Trim());
                }

                ApplyText("STORE_NAME",       v => store.Name = v);
                ApplyText("STORE_ADDRESS",    v => store.Address = v);
                ApplyText("STORE_PHONE",      v => store.Phone = v);
                ApplyText("STORE_EMAIL",      v => store.Email = v);
                ApplyText("STORE_OPEN_TIME",  v => store.OpenTime = v);
                ApplyText("STORE_CLOSE_TIME", v => store.CloseTime = v);
                ApplyText("STORE_TAX_CODE",   v => store.TaxCode = v);

                store.TaxMode        = EnvInt("STORE_TAX_MODE", store.TaxMode);
                store.VatRatePercent = EnvInt("STORE_VAT_RATE", store.VatRatePercent);

                await db.SaveChangesAsync();
                logger.LogInformation(
                    "Cửa hàng {Name}: chế độ thuế {Mode}, thuế suất {Rate}%",
                    store.Name, store.TaxMode, store.VatRatePercent);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Không khởi tạo được cơ sở dữ liệu");
        throw;
    }
}

Log.Information("Một Chút Coffee API sẵn sàng tại {Url}", Cfg("API_BASE_URL", "http://localhost:5080"));

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
