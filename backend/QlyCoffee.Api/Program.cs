using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QlyCoffee.Api.Ai;
using QlyCoffee.Api.Controllers;
using QlyCoffee.Api.LiveScore;
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
    o.RestockCoverDays       = EnvInt("RESTOCK_COVER_DAYS", 3);
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
builder.Services.AddScoped<IRestockService, RestockService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IWasteRiskService, WasteRiskService>();
builder.Services.AddScoped<IPromotionPlanner, PromotionPlanner>();
builder.Services.AddScoped<IAiNarrator, ClaudeNarrator>();
builder.Services.AddScoped<IDailyPlanService, DailyPlanService>();
builder.Services.AddScoped<IWorkshopService, WorkshopService>();

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

builder.Services.AddCors(o => o.AddPolicy("QlyClient", p =>
{
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials();

    if (builder.Environment.IsDevelopment())
    {
        // Ở máy phát triển, chấp nhận MỌI nguồn trong mạng nội bộ.
        //
        // Vì sao không dùng danh sách cứng: địa chỉ IP của máy đổi mỗi lần đổi
        // wifi. Danh sách ghi sẵn "http://10.50.104.62:5180" sẽ chặn đúng cái
        // máy đang chạy ngay hôm sau, và lỗi hiện ra là "không kết nối được
        // máy chủ" — không ai đoán được nguyên nhân là CORS.
        //
        // AllowAnyOrigin() KHÔNG dùng được vì đi cùng AllowCredentials là tổ
        // hợp bị trình duyệt cấm. SetIsOriginAllowed cho phép xét từng nguồn.
        //
        // Chỉ nới trong Development. Lên máy chủ thật thì quay về danh sách
        // cứng bên dưới — mở toang CORS ở môi trường thật là để trang web bất
        // kỳ gọi API nhân danh người đã đăng nhập.
        p.SetIsOriginAllowed(IsLocalNetworkOrigin);
    }
    else
    {
        p.WithOrigins(allowedOrigins);
    }
}));

// Nguồn này có nằm trong mạng nội bộ không — dùng cho CORS ở máy phát triển.
// Các dải riêng theo RFC 1918, link-local, CGNAT, localhost và tên máy nội bộ.
static bool IsLocalNetworkOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

    var host = uri.Host;
    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
    if (host is "127.0.0.1" or "::1") return true;
    if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
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

// ---- Bóng đá trực tiếp ------------------------------------------------------
// Chỉ MÁY CHỦ hỏi nguồn tỉ số rồi đẩy xuống mọi màn hình qua SignalR: mười màn
// hình mở cùng lúc vẫn chỉ tốn một lượt gọi, và khóa API không ra tới trình
// duyệt. Chưa đặt FOOTBALL_API_KEY thì job tự tắt, không gọi ra ngoài lần nào.
builder.Services.Configure<LiveScoreOptions>(o =>
{
    o.ApiKey          = Cfg("FOOTBALL_API_KEY", "");
    o.Competitions    = Cfg("FOOTBALL_COMPETITIONS", "");
    o.LivePollSeconds = EnvInt("FOOTBALL_LIVE_POLL_SECONDS", 30);
    o.IdlePollMinutes = EnvInt("FOOTBALL_IDLE_POLL_MINUTES", 10);
});
builder.Services.AddSignalR();
builder.Services.AddHttpClient(FootballDataClient.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<FootballDataClient>();
builder.Services.AddSingleton<LiveScoreStore>();
builder.Services.AddHostedService<LiveScorePoller>();

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
app.MapHub<LiveScoreHub>("/" + QlyCoffee.Shared.LiveScoreChannel.HubPath);

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

        // ---- Lịch workshop luôn phải có buổi ở phía trước -------------------
        //
        //  Khác MenuSync ở một điểm quan trọng: thực đơn thêm món là việc thỉnh
        //  thoảng, còn lịch workshop HẾT HẠN theo thời gian. Seed 8 tuần vào ngày
        //  khai trương thì hai tháng sau trang đặt lịch trống trơn, và không ai
        //  phát hiện cho tới khi khách hỏi. Nên mặc định BẬT, và chạy mỗi lần
        //  khởi động để luôn phủ đủ số tuần phía trước.
        //
        //  Chỉ BÙ buổi còn thiếu, không đụng vào buổi đã có — chủ quán đổi giờ,
        //  đổi giá hay hủy buổi nào thì buổi đó giữ nguyên.
        if (Cfg("WORKSHOP_SYNC_ENABLED", "true") == "true")
        {
            var storeId = Guid.TryParse(Cfg("DEFAULT_STORE_ID", ""), out var wsid)
                ? wsid
                : (await db.Stores.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync())?.Id ?? Guid.Empty;

            if (storeId == Guid.Empty)
            {
                logger.LogWarning("WORKSHOP_SYNC_ENABLED=true nhưng chưa có cửa hàng nào — bỏ qua");
            }
            else
            {
                var weeks = int.TryParse(Cfg("WORKSHOP_SYNC_WEEKS", "10"), out var w) ? w : 10;
                await WorkshopSync.ApplyAsync(db, storeId, logger, weeks);
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
    // Tìm từ HAI điểm xuất phát, theo đúng thứ tự này.
    //
    // 1. Thư mục chứa file .dll (AppContext.BaseDirectory)
    //    Đây là nơi deploy/api/.env nằm khi chạy trên máy chủ. Điểm mạnh: giá
    //    trị này KHÔNG phụ thuộc vào ai khởi động ứng dụng hay khởi động kiểu
    //    gì — nó luôn là thư mục ứng dụng.
    //
    // 2. Thư mục làm việc (Directory.GetCurrentDirectory)
    //    Đây là nơi tìm ra .env ở gốc repo khi chạy `dotnet run` lúc phát triển.
    //
    //  ⚠️ VÌ SAO PHẢI CÓ ĐIỂM XUẤT PHÁT THỨ NHẤT
    //  Bản trước chỉ dùng thư mục làm việc. Chạy `dotnet run` thì đúng, nhưng
    //  dưới IIS thì thư mục làm việc do máy chủ đặt chứ không phải do ứng dụng,
    //  và tuỳ phiên bản Windows lẫn chế độ chạy (in-process hay out-of-process)
    //  nó có thể là thư mục của tiến trình IIS chứ không phải thư mục ứng dụng.
    //  Rơi vào trường hợp đó thì không tìm thấy .env, và ứng dụng chết lúc khởi
    //  động với thông báo về JWT_SECRET — một thông báo không hề gợi ý rằng
    //  nguyên nhân thật là "không đọc được file cấu hình".
    //
    //  Dò cả hai nơi thì không còn phải đoán máy chủ đang đặt thư mục làm việc
    //  ở đâu nữa.
    var roots = new[]
    {
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory()
    };

    var daTim = new List<string>();

    foreach (var root in roots)
    {
        var dir = new DirectoryInfo(root);

        // Đi ngược lên tối đa 5 cấp: lúc phát triển, thư mục làm việc là
        // backend/QlyCoffee.Api còn .env nằm ở gốc repo, cách đó hai cấp.
        for (int i = 0; i < 5 && dir is not null; i++)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (File.Exists(path))
            {
                Env.Load(path);
                Console.WriteLine($"[cấu hình] Đã nạp {path}");
                return;
            }
            daTim.Add(dir.FullName);
            dir = dir.Parent;
        }
    }

    // Không tìm thấy thì nói rõ ĐÃ TÌM Ở ĐÂU. Người deploy cần đúng thông tin
    // này để biết phải đặt file vào chỗ nào; một dòng "không tìm thấy .env"
    // trống không thì chẳng giúp được gì.
    Console.WriteLine("[cấu hình] CẢNH BÁO: không tìm thấy file .env. Đã tìm ở:");
    foreach (var d in daTim.Distinct())
        Console.WriteLine($"[cấu hình]   {d}");
    Console.WriteLine("[cấu hình] Ứng dụng sẽ dừng ngay sau đây vì thiếu JWT_SECRET.");
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
