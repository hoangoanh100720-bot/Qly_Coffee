using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Infrastructure.Seed;

// ==============================================================================
//  DỮ LIỆU MẪU
//
//  Đây KHÔNG phải dữ liệu giả để test. Đây là menu thật của một quán cà phê
//  Việt Nam hướng tới khách trẻ, với công thức định lượng đúng thực tế pha chế
//  và màu sắc lấy từ màu thật của từng đồ uống.
//
//  Có thể dùng thẳng khi mở quán, chỉ cần chỉnh giá cho khớp thị trường.
//
//  ⚠️ THỰC ĐƠN KHÔNG CÒN NẰM TRONG FILE NÀY.
//  Nguyên liệu, danh mục, món và công thức đã chuyển sang MenuCatalog.cs, được
//  MenuSync bơm vào database. Lý do: seeder chỉ chạy đúng một lần lúc database
//  còn trống, nên món thêm sau ngày khai trương sẽ không bao giờ tới được quán
//  đang chạy. MenuSync thì chạy lại được bất cứ lúc nào mà không phá dữ liệu.
//
//  File này giờ chỉ còn ba việc mà MenuSync KHÔNG làm, vì cả ba đều là dữ liệu
//  giả lập chỉ có ý nghĩa ở lần khởi tạo đầu tiên:
//    §1  Cửa hàng và tài khoản quản trị
//    §2  Vài lô hàng cận hạn để AI có việc phân tích ngay
//    §3  Lịch sử tiêu thụ 28 ngày để mô hình dự báo có dữ liệu chạy
// ==============================================================================

public class SeedOptions
{
    public string AdminEmail { get; set; } = "admin@qlycoffee.vn";
    public string AdminPassword { get; set; } = "Admin@123456";
    public string AdminName { get; set; } = "Chủ quán";
    public string StoreName { get; set; } = "Một Chút Coffee";
    public string StoreSlug { get; set; } = "mot-chut-coffee";
    public string StoreAddress { get; set; } = "";
    public string StorePhone { get; set; } = "";
    public int HistoryDays { get; set; } = 28;

    // --- Thuế GTGT ------------------------------------------------------------
    // Xem QlyCoffee.Shared/Tax.cs để biết căn cứ pháp lý của từng giá trị.

    /// <summary>0 = không tách thuế · 1 = giá đã gồm thuế (mặc định) · 2 = giá chưa gồm thuế.</summary>
    public int TaxMode { get; set; } = Shared.VatPolicy.DefaultMode;

    /// <summary>Thuế suất phần trăm. Mặc định 8% theo Nghị quyết 204/2025/QH15.</summary>
    public int VatRatePercent { get; set; } = Shared.VatPolicy.DefaultRatePercent;

    /// <summary>Mã số thuế in lên hóa đơn. Để trống thì hóa đơn bỏ dòng này.</summary>
    public string? TaxCode { get; set; }
}

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db, SeedOptions opt, ILogger logger)
    {
        // Đã có dữ liệu thì bỏ qua — tránh nhân đôi khi khởi động lại
        if (await db.Stores.AnyAsync())
        {
            logger.LogInformation("Đã có dữ liệu, bỏ qua seed");
            return;
        }

        // ======================================================================
        //  §1  CỬA HÀNG & TÀI KHOẢN
        // ======================================================================
        var store = new Store
        {
            Name       = opt.StoreName,
            Slug       = opt.StoreSlug,
            Address    = string.IsNullOrEmpty(opt.StoreAddress)
                            ? "123 Nguyễn Huệ, Bến Nghé, Quận 1, TP.HCM"
                            : opt.StoreAddress,
            Phone      = string.IsNullOrEmpty(opt.StorePhone) ? "0901234567" : opt.StorePhone,
            Email      = "xinchao@motchutcoffee.vn",
            OpenTime   = "07:00",
            CloseTime  = "22:00",
            TimeZone   = "Asia/Ho_Chi_Minh",
            BusinessDayEndHour = 23,
            IsOpen     = true,

            // Mặc định: giá niêm yết đã gồm thuế GTGT 8% — đúng Luật Giá 2023
            // Điều 29 và Nghị quyết 204/2025/QH15.
            TaxMode        = opt.TaxMode,
            VatRatePercent = opt.VatRatePercent,
            TaxCode        = string.IsNullOrWhiteSpace(opt.TaxCode) ? null : opt.TaxCode.Trim()
        };
        db.Stores.Add(store);

        db.Users.Add(new User
        {
            Email        = opt.AdminEmail.ToLowerInvariant(),
            FullName     = opt.AdminName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(opt.AdminPassword, workFactor: 12),
            Role         = UserRole.Owner,
            StoreId      = store.Id,
            IsActive     = true
        });

        db.Users.Add(new User
        {
            Email        = "nhanvien@qlycoffee.vn",
            FullName     = "Nhân viên mẫu",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Nhanvien@123", workFactor: 12),
            Role         = UserRole.Staff,
            StoreId      = store.Id,
            IsActive     = true
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo cửa hàng {Name} (Id: {Id})", store.Name, store.Id);
        logger.LogWarning("⚠️ Đặt DEFAULT_STORE_ID={Id} vào file .env rồi khởi động lại", store.Id);

        // ======================================================================
        //  THỰC ĐƠN
        //
        //  openingStock: true — database còn trống thì mọi nguyên liệu đều là mới,
        //  nên MenuSync tạo luôn lô tồn kho mở đầu cho tất cả. Không có bước này
        //  thì cả menu hiện "tạm hết" ngay từ lần chạy đầu.
        // ======================================================================
        var menu = await MenuSync.ApplyAsync(
            db, store.Id, logger, refreshRecipes: false, openingStock: true);

        var ing = menu.IngredientsBySku;
        var products = menu.AllProducts;

        // ======================================================================
        //  §2  LÔ HÀNG CẬN HẠN
        //
        //  MenuSync đã tạo lô tồn kho bình thường cho mọi nguyên liệu. Phần dưới
        //  CỐ Ý thêm vài lô sắp hết hạn để AI Engine có việc phân tích ngay từ
        //  lần chạy đầu tiên — không có lô cận hạn thì bản kế hoạch trống rỗng
        //  và người dùng không thấy được tính năng hoạt động thế nào.
        //
        //  FEFO sẽ tiêu thụ đúng những lô này trước vì chúng hết hạn sớm nhất.
        // ======================================================================
        var today = DateTime.UtcNow.Date;
        var rnd = new Random(20260810);   // hạt cố định để seed lặp lại giống nhau

        void AddNearExpiryLot(string sku, double qty, int daysToExpiry)
        {
            if (!ing.TryGetValue(sku, out var item))
            {
                logger.LogWarning("Bỏ qua lô cận hạn: không có nguyên liệu {Sku}", sku);
                return;
            }

            var lot = new InventoryLot
            {
                StoreId           = store.Id,
                IngredientId      = item.Id,
                LotCode           = $"{sku}-{today:yyMMdd}-CANHAN",
                ReceivedQuantity  = qty,
                RemainingQuantity = qty,
                UnitCost          = item.AverageUnitCost,
                ReceivedAt        = DateTime.UtcNow.AddDays(-rnd.Next(0, 3)),
                ExpiryDate        = today.AddDays(daysToExpiry),
                Status            = LotStatus.Active
            };
            db.InventoryLots.Add(lot);

            db.StockMovements.Add(new StockMovement
            {
                StoreId        = store.Id,
                IngredientId   = item.Id,
                LotId          = lot.Id,
                Type           = MovementType.PurchaseIn,
                QuantityDelta  = qty,
                UnitCost       = item.AverageUnitCost,
                TotalCost      = (int)Math.Round(qty * item.AverageUnitCost),
                ReferenceType  = "PURCHASE",
                ReferenceId    = lot.Id,
                IdempotencyKey = $"SEED:{lot.Id}",
                OccurredAt     = lot.ReceivedAt
            });
        }

        AddNearExpiryLot("DAI-MILK-01", 4000, 2);   // sữa tươi, còn 2 ngày
        AddNearExpiryLot("FRU-PEA-01",  1800, 3);   // đào ngâm, còn 3 ngày
        AddNearExpiryLot("FRU-MAN-01",  2500, 4);   // xoài, còn 4 ngày
        AddNearExpiryLot("TOP-BOB-01",  1500, 1);   // trân châu, còn 1 ngày — khẩn cấp
        AddNearExpiryLot("BAK-CRO-01",    12, 1);   // croissant, bán hết hôm nay hoặc bỏ

        await db.SaveChangesAsync();
        logger.LogInformation("Đã thêm 5 lô cận hạn để AI phân tích");

        // ======================================================================
        //  §3  LỊCH SỬ TIÊU THỤ 28 NGÀY
        //
        //  Không có bảng này thì mô hình EWMA trả về 0, dẫn tới TOÀN BỘ tồn kho
        //  bị coi là có nguy cơ phải bỏ — bản kế hoạch đầu tiên sẽ vô nghĩa.
        //
        //  Dữ liệu mô phỏng có tính đến nhịp thật của quán cà phê:
        //    · Cuối tuần đông hơn ngày thường khoảng 40%
        //    · Dao động ngẫu nhiên ±20% giữa các ngày
        // ======================================================================
        var ingredientList = ing.Values.ToList();

        for (int d = opt.HistoryDays; d >= 1; d--)
        {
            var date = today.AddDays(-d);
            var key = date.ToString("yyyy-MM-dd");

            // Cuối tuần bán nhiều hơn
            var weekendBoost = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.4 : 1.0;
            var noise = 0.8 + rnd.NextDouble() * 0.4;     // dao động 80%–120%
            var factor = weekendBoost * noise;

            // ---- Tiêu thụ nguyên liệu ---------------------------------------
            foreach (var item in ingredientList)
            {
                // Ước lượng nhu cầu nền theo nhóm nguyên liệu
                var baseDaily = item.Category switch
                {
                    IngredientCategory.Coffee    => 180.0,
                    IngredientCategory.Dairy     => item.BaseUnit == BaseUnit.Milliliter ? 900.0 : 60.0,
                    IngredientCategory.Tea       => 45.0,
                    IngredientCategory.Powder    => 30.0,
                    IngredientCategory.Topping   => 220.0,
                    IngredientCategory.Fruit     => item.BaseUnit == BaseUnit.Piece ? 6.0 : 320.0,
                    IngredientCategory.Syrup     => 380.0,
                    IngredientCategory.Sweetener => 150.0,
                    IngredientCategory.Packaging => 45.0,
                    // Bánh nhập theo cái và bán hết trong ngày — nhịp khác hẳn nguyên liệu pha chế
                    IngredientCategory.Bakery    => 8.0,
                    _                            => item.BaseUnit == BaseUnit.Piece ? 4.0 : 1800.0
                };

                var used = Math.Round(baseDaily * factor, 2);

                db.DailyConsumptions.Add(new DailyConsumption
                {
                    StoreId      = store.Id,
                    IngredientId = item.Id,
                    BusinessDate = key,
                    QuantityUsed = used,
                    CostUsed     = (int)Math.Round(used * item.AverageUnitCost)
                });
            }

            // ---- Doanh số theo món --------------------------------------------
            foreach (var p in products)
            {
                // Món gắn nhãn bán chạy thì bán gấp đôi
                var isBestSeller = p.Tags.Contains("best-seller");
                var baseUnits = isBestSeller ? 14.0 : 6.0;
                var units = (int)Math.Round(baseUnits * factor);

                if (units <= 0) continue;

                db.DailySales.Add(new DailySales
                {
                    StoreId      = store.Id,
                    ProductId    = p.Id,
                    BusinessDate = key,
                    UnitsSold    = units,
                    Revenue      = units * p.BasePrice,
                    Cost         = units * p.ComputedCost
                });
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo {Days} ngày lịch sử tiêu thụ cho mô hình dự báo", opt.HistoryDays);

        logger.LogInformation("=====================================================");
        logger.LogInformation("SEED HOÀN TẤT");
        logger.LogInformation("  Đăng nhập: {Email} / {Password}", opt.AdminEmail, opt.AdminPassword);
        logger.LogInformation("  Thực đơn: {Count} món", products.Count);
        logger.LogInformation("  DEFAULT_STORE_ID={Id}", store.Id);
        logger.LogInformation("=====================================================");
    }
}
