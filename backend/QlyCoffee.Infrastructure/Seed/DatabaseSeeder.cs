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
//  BỐ CỤC:
//    §1  Cửa hàng và tài khoản quản trị
//    §2  Nguyên liệu — 30 loại, mỗi loại có màu thật và mã hình minh họa
//    §3  Nhóm topping
//    §4  Danh mục
//    §5  Món — 22 món kèm công thức định lượng
//    §6  Lô hàng — cố tình tạo vài lô cận hạn để AI có việc phân tích
//    §7  Lịch sử tiêu thụ 28 ngày để mô hình dự báo có dữ liệu chạy
// ==============================================================================

public class SeedOptions
{
    public string AdminEmail { get; set; } = "admin@qlycoffee.vn";
    public string AdminPassword { get; set; } = "Admin@123456";
    public string AdminName { get; set; } = "Chủ quán";
    public string StoreName { get; set; } = "Qly Coffee";
    public string StoreSlug { get; set; } = "qly-coffee";
    public string StoreAddress { get; set; } = "";
    public string StorePhone { get; set; } = "";
    public int HistoryDays { get; set; } = 28;
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
            Email      = "xinchao@qlycoffee.vn",
            OpenTime   = "07:00",
            CloseTime  = "22:00",
            TimeZone   = "Asia/Ho_Chi_Minh",
            BusinessDayEndHour = 23,
            IsOpen     = true
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
        //  §2  NGUYÊN LIỆU
        //
        //  Cột ColorHex là MÀU THẬT của nguyên liệu ngoài đời. Giao diện dùng
        //  màu này để vẽ hình minh họa và chấm màu trong bảng kho.
        //
        //  Cột WastageRate là hao hụt chế biến thực tế:
        //    · Trái cây phải gọt vỏ bỏ hạt   → 10–15%
        //    · Topping phải nấu, dễ dính nồi → 6–8%
        //    · Bột, siro, đường              → 1–3%
        // ======================================================================
        var ing = new Dictionary<string, Ingredient>();

        void AddIngredient(string sku, string name, IngredientCategory cat, BaseUnit unit,
            string color, string icon, int cost, int? shelfLife, int warnDays,
            double wastage, double minStock, double reorder)
        {
            var item = new Ingredient
            {
                StoreId              = store.Id,
                Sku                  = sku,
                Name                 = name,
                Category             = cat,
                BaseUnit             = unit,
                ColorHex             = color,
                IconKey              = icon,
                AverageUnitCost      = cost,
                DefaultShelfLifeDays = shelfLife,
                ExpiryWarningDays    = warnDays,
                WastageRate          = wastage,
                MinStockLevel        = minStock,
                ReorderPoint         = reorder,
                IsActive             = true
            };
            ing[sku] = item;
            db.Ingredients.Add(item);
        }

        // --- Cà phê ---------------------------------------------------------
        AddIngredient("COF-ROB-01", "Cà phê Robusta rang xay", IngredientCategory.Coffee,
            BaseUnit.Gram, "#3B2416", "coffee-beans", 180, 180, 30, 0.02, 500, 1500);
        AddIngredient("COF-ARA-01", "Cà phê Arabica rang xay", IngredientCategory.Coffee,
            BaseUnit.Gram, "#4E3524", "coffee-beans", 320, 180, 30, 0.02, 300, 1000);

        // --- Sữa & kem ------------------------------------------------------
        // Sữa tươi hạn rất ngắn (7 ngày) — đây là nguyên liệu hay xuất hiện
        // trong cảnh báo cận hạn nhất.
        AddIngredient("DAI-MILK-01", "Sữa tươi không đường", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#F5EDE0", "milk", 32, 7, 3, 0.03, 2000, 5000);
        AddIngredient("DAI-CON-01", "Sữa đặc có đường", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#E8C89A", "condensed-milk", 55, 90, 14, 0.02, 800, 2000);
        AddIngredient("DAI-WHIP-01", "Kem sữa béo (whipping)", IngredientCategory.Dairy,
            BaseUnit.Milliliter, "#FBF6EC", "cream", 120, 14, 5, 0.05, 500, 1200);
        AddIngredient("DAI-CHE-01", "Kem cheese", IngredientCategory.Dairy,
            BaseUnit.Gram, "#FAF0DC", "cheese-foam", 180, 10, 4, 0.05, 400, 1000);

        // --- Trà -------------------------------------------------------------
        AddIngredient("TEA-BLK-01", "Hồng trà", IngredientCategory.Tea,
            BaseUnit.Gram, "#8B3A1A", "tea-leaf", 250, 365, 30, 0.02, 200, 500);
        AddIngredient("TEA-OOL-01", "Trà ô long", IngredientCategory.Tea,
            BaseUnit.Gram, "#6B7A3F", "tea-leaf", 380, 365, 30, 0.02, 150, 400);
        AddIngredient("TEA-JAS-01", "Trà lài", IngredientCategory.Tea,
            BaseUnit.Gram, "#9FA85E", "tea-leaf", 300, 365, 30, 0.02, 150, 400);

        // --- Bột --------------------------------------------------------------
        AddIngredient("POW-MAT-01", "Bột matcha Nhật", IngredientCategory.Powder,
            BaseUnit.Gram, "#7CA24A", "matcha", 1400, 180, 20, 0.03, 100, 300);
        AddIngredient("POW-CAC-01", "Bột cacao", IngredientCategory.Powder,
            BaseUnit.Gram, "#5C3A21", "cacao", 450, 365, 30, 0.03, 200, 500);

        // --- Topping ----------------------------------------------------------
        // Trân châu chỉ để được 3 ngày sau khi nấu — hay bị bỏ nhất.
        AddIngredient("TOP-BOB-01", "Trân châu đen", IngredientCategory.Topping,
            BaseUnit.Gram, "#2A1F1A", "boba", 45, 3, 1, 0.08, 500, 1500);
        AddIngredient("TOP-BOW-01", "Trân châu trắng", IngredientCategory.Topping,
            BaseUnit.Gram, "#EDE4D6", "boba", 60, 3, 1, 0.08, 300, 900);
        AddIngredient("TOP-JEL-01", "Thạch dừa", IngredientCategory.Topping,
            BaseUnit.Gram, "#F2EDE3", "jelly", 40, 14, 5, 0.05, 400, 1000);
        AddIngredient("TOP-PUD-01", "Pudding trứng", IngredientCategory.Topping,
            BaseUnit.Gram, "#F3D68A", "pudding", 70, 3, 1, 0.06, 300, 800);

        // --- Trái cây ---------------------------------------------------------
        AddIngredient("FRU-PEA-01", "Đào ngâm", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F0A868", "peach", 95, 5, 2, 0.05, 800, 2000);
        AddIngredient("FRU-MAN-01", "Xoài tươi", IngredientCategory.Fruit,
            BaseUnit.Gram, "#F2B33D", "mango", 55, 4, 2, 0.12, 1000, 2500);
        AddIngredient("FRU-LEM-01", "Chanh tươi", IngredientCategory.Fruit,
            BaseUnit.Piece, "#D8E04A", "lemon", 3000, 10, 3, 0.10, 20, 50);
        AddIngredient("FRU-LGR-01", "Sả tươi", IngredientCategory.Fruit,
            BaseUnit.Gram, "#C3CE8A", "lemongrass", 35, 7, 3, 0.15, 200, 500);
        AddIngredient("FRU-STR-01", "Dâu tây", IngredientCategory.Fruit,
            BaseUnit.Gram, "#D63B3B", "peach", 180, 3, 1, 0.10, 400, 1000);

        // --- Siro & đường ------------------------------------------------------
        AddIngredient("SYR-SLT-01", "Kem muối", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#F7F2E8", "cream", 140, 5, 2, 0.04, 500, 1200);
        AddIngredient("SYR-SUG-01", "Nước đường", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#D9B368", "syrup", 18, 30, 7, 0.01, 1500, 3000);
        AddIngredient("SYR-BRW-01", "Đường đen (brown sugar)", IngredientCategory.Syrup,
            BaseUnit.Milliliter, "#6B4423", "syrup", 65, 30, 7, 0.02, 500, 1200);
        AddIngredient("SWE-SUG-01", "Đường cát", IngredientCategory.Sweetener,
            BaseUnit.Gram, "#F7F3EA", "sugar", 22, 730, 60, 0.01, 2000, 5000);

        // --- Khác ---------------------------------------------------------------
        AddIngredient("OTH-ICE-01", "Đá viên", IngredientCategory.Other,
            BaseUnit.Gram, "#DCEAF2", "ice", 2, null, 0, 0.10, 10000, 20000);
        AddIngredient("OTH-EGG-01", "Trứng gà", IngredientCategory.Other,
            BaseUnit.Piece, "#F0D9A8", "egg", 3500, 21, 5, 0.05, 20, 50);

        // --- Bao bì ---------------------------------------------------------------
        AddIngredient("PKG-CUP-M", "Ly nhựa 500ml + nắp", IngredientCategory.Packaging,
            BaseUnit.Piece, "#E8EEF0", "cup", 1800, null, 0, 0.02, 200, 500);
        AddIngredient("PKG-CUP-L", "Ly nhựa 700ml + nắp", IngredientCategory.Packaging,
            BaseUnit.Piece, "#E8EEF0", "cup", 2200, null, 0, 0.02, 150, 400);
        AddIngredient("PKG-STR-01", "Ống hút", IngredientCategory.Packaging,
            BaseUnit.Piece, "#C46A52", "straw", 350, null, 0, 0.03, 300, 800);

        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo {Count} nguyên liệu", ing.Count);

        // ======================================================================
        //  §3  NHÓM TOPPING
        // ======================================================================
        var toppingGroup = new ModifierGroup
        {
            StoreId = store.Id, Name = "Topping",
            MinSelect = 0, MaxSelect = 3, IsRequired = false, SortOrder = 1
        };
        var sugarGroup = new ModifierGroup
        {
            StoreId = store.Id, Name = "Mức đường",
            MinSelect = 1, MaxSelect = 1, IsRequired = true, SortOrder = 2
        };
        var iceGroup = new ModifierGroup
        {
            StoreId = store.Id, Name = "Mức đá",
            MinSelect = 1, MaxSelect = 1, IsRequired = true, SortOrder = 3
        };
        db.ModifierGroups.AddRange(toppingGroup, sugarGroup, iceGroup);

        // Topping CÓ tiêu tốn nguyên liệu → có công thức riêng
        void AddTopping(string name, int price, string colorHex, string sku, double qty, int order)
        {
            var m = new Modifier
            {
                ModifierGroupId = toppingGroup.Id, Name = name,
                PriceDelta = price, ColorHex = colorHex, SortOrder = order, IsActive = true
            };
            db.Modifiers.Add(m);
            db.ModifierRecipeItems.Add(new ModifierRecipeItem
            {
                ModifierId = m.Id, IngredientId = ing[sku].Id, Quantity = qty
            });
        }

        AddTopping("Trân châu đen",   8000,  "#2A1F1A", "TOP-BOB-01", 40, 1);
        AddTopping("Trân châu trắng", 10000, "#EDE4D6", "TOP-BOW-01", 35, 2);
        AddTopping("Thạch dừa",       7000,  "#F2EDE3", "TOP-JEL-01", 40, 3);
        AddTopping("Pudding trứng",   10000, "#F3D68A", "TOP-PUD-01", 45, 4);
        AddTopping("Kem cheese",      12000, "#FAF0DC", "DAI-CHE-01", 35, 5);

        // Mức đường và mức đá KHÔNG có công thức riêng.
        // Tác động của chúng nhỏ và làm phức tạp logic trừ kho không đáng.
        // Nếu sau này cần chính xác hơn thì thêm cột QuantityDelta cho phép giá trị âm.
        var sugarLevels = new[] { "100% đường", "70% đường", "50% đường", "30% đường", "Không đường" };
        for (int i = 0; i < sugarLevels.Length; i++)
            db.Modifiers.Add(new Modifier
            {
                ModifierGroupId = sugarGroup.Id, Name = sugarLevels[i],
                PriceDelta = 0, ColorHex = "#D9B368", SortOrder = i, IsActive = true
            });

        var iceLevels = new[] { "100% đá", "70% đá", "50% đá", "Ít đá", "Không đá" };
        for (int i = 0; i < iceLevels.Length; i++)
            db.Modifiers.Add(new Modifier
            {
                ModifierGroupId = iceGroup.Id, Name = iceLevels[i],
                PriceDelta = 0, ColorHex = "#DCEAF2", SortOrder = i, IsActive = true
            });

        await db.SaveChangesAsync();

        // ======================================================================
        //  §4  DANH MỤC
        //
        //  ColorHex lấy từ màu đặc trưng của nhóm đồ uống — dùng tô chip lọc
        //  ở trang thực đơn.
        // ======================================================================
        var cats = new Dictionary<string, Category>();

        void AddCategory(string key, string name, string slug, string desc, string color, string icon, int order)
        {
            var c = new Category
            {
                StoreId = store.Id, Name = name, Slug = slug, Description = desc,
                ColorHex = color, IconKey = icon, SortOrder = order, IsActive = true
            };
            cats[key] = c;
            db.Categories.Add(c);
        }

        AddCategory("coffee",  "Cà phê",         "ca-phe",
            "Pha phin truyền thống và espresso", "#3B2416", "cup-hot", 1);
        AddCategory("milktea", "Trà sữa",        "tra-sua",
            "Ủ trà tươi mỗi 4 tiếng",            "#C9A57B", "bubble-tea", 2);
        AddCategory("fruit",   "Trà trái cây",   "tra-trai-cay",
            "Trái cây tươi, không dùng siro pha sẵn", "#E8944A", "leaf", 3);
        AddCategory("blended", "Đá xay & Sinh tố", "da-xay",
            "Xay tại chỗ khi có đơn",            "#8FC3D8", "blender", 4);
        AddCategory("matcha",  "Matcha & Cacao", "matcha-cacao",
            "Matcha Uji và cacao nguyên chất",   "#7CA24A", "leaf", 5);

        await db.SaveChangesAsync();

        // ======================================================================
        //  §5  MÓN & CÔNG THỨC ĐỊNH LƯỢNG
        //
        //  ColorPrimary = màu thân nước, ColorAccent = màu lớp kem/foam.
        //  Hai màu này được truyền vào component DrinkGlass để vẽ ly minh họa.
        // ======================================================================
        var products = new List<Product>();

        Product AddProduct(string catKey, string name, string slug, string desc,
            int price, string colorPrimary, string colorAccent, string tags,
            (string Sku, double Qty, bool Optional)[] recipe,
            bool featured = false, int order = 0)
        {
            var p = new Product
            {
                StoreId         = store.Id,
                CategoryId      = cats[catKey].Id,
                Name            = name,
                Slug            = slug,
                Description     = desc,
                BasePrice       = price,
                ColorPrimaryHex = colorPrimary,
                ColorAccentHex  = colorAccent,
                Tags            = tags,
                IsActive        = true,
                IsFeatured      = featured,
                SortOrder       = order,
                IsAvailable     = true,
                MaxServings     = 9999
            };
            db.Products.Add(p);
            products.Add(p);

            var i = 0;
            foreach (var (sku, qty, optional) in recipe)
            {
                db.RecipeItems.Add(new RecipeItem
                {
                    ProductId = p.Id, IngredientId = ing[sku].Id,
                    Quantity = qty, IsOptional = optional, SortOrder = i++
                });
            }

            // Hai size cho mọi món. Size L nhân công thức 1.4 lần.
            db.ProductVariants.Add(new ProductVariant
            {
                ProductId = p.Id, Name = "Size M", PriceDelta = 0,
                RecipeMultiplier = 1.0, IsDefault = true, SortOrder = 0, IsActive = true
            });
            db.ProductVariants.Add(new ProductVariant
            {
                ProductId = p.Id, Name = "Size L", PriceDelta = 8000,
                RecipeMultiplier = 1.4, IsDefault = false, SortOrder = 1, IsActive = true
            });

            // Món nào cũng cho chọn mức đường và mức đá
            db.ProductModifiers.Add(new ProductModifier
                { ProductId = p.Id, ModifierGroupId = sugarGroup.Id, SortOrder = 2 });
            db.ProductModifiers.Add(new ProductModifier
                { ProductId = p.Id, ModifierGroupId = iceGroup.Id, SortOrder = 3 });

            return p;
        }

        // ---- CÀ PHÊ ---------------------------------------------------------
        AddProduct("coffee", "Cà phê muối", "ca-phe-muoi",
            "Cà phê phin đậm, phủ lớp kem muối béo mặn đặc trưng xứ Huế.",
            35000, "#3D2415", "#F7F2E8", "best-seller,signature",
            new[] {
                ("COF-ROB-01", 20.0, false), ("DAI-CON-01", 20.0, false),
                ("SYR-SLT-01", 45.0, false), ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-M", 1.0, false),   ("PKG-STR-01", 1.0, false)
            }, featured: true, order: 1);

        AddProduct("coffee", "Cà phê kem trứng", "ca-phe-kem-trung",
            "Cà phê nóng phủ kem trứng đánh bông, thơm béo kiểu Hà Nội.",
            42000, "#2E1A0F", "#F3D68A", "signature",
            new[] {
                ("COF-ROB-01", 22.0, false), ("OTH-EGG-01", 1.0, false),
                ("DAI-CON-01", 25.0, false), ("DAI-WHIP-01", 20.0, false),
                ("PKG-CUP-M", 1.0, false)
            }, order: 2);

        AddProduct("coffee", "Bạc xỉu", "bac-xiu",
            "Nhiều sữa, ít cà phê — vị ngọt dịu quen thuộc.",
            32000, "#B08B60", "#F5EDE0", "best-seller",
            new[] {
                ("COF-ROB-01", 12.0, false), ("DAI-CON-01", 30.0, false),
                ("DAI-MILK-01", 120.0, false), ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-M", 1.0, false), ("PKG-STR-01", 1.0, false)
            }, order: 3);

        AddProduct("coffee", "Cà phê sữa đá", "ca-phe-sua-da",
            "Phin truyền thống, đậm và ngọt vừa.",
            30000, "#4A2C17", "#C89968", "best-seller",
            new[] {
                ("COF-ROB-01", 20.0, false), ("DAI-CON-01", 25.0, false),
                ("OTH-ICE-01", 180.0, true), ("PKG-CUP-M", 1.0, false),
                ("PKG-STR-01", 1.0, false)
            }, featured: true, order: 4);

        AddProduct("coffee", "Cold brew", "cold-brew",
            "Ủ lạnh 18 tiếng, vị trong và chua thanh, ít đắng.",
            45000, "#5A3A22", "#8B6242", "new",
            new[] {
                ("COF-ARA-01", 28.0, false), ("OTH-ICE-01", 200.0, true),
                ("PKG-CUP-L", 1.0, false), ("PKG-STR-01", 1.0, false)
            }, order: 5);

        AddProduct("coffee", "Americano", "americano",
            "Espresso pha loãng, không sữa không đường.",
            38000, "#4A3020", "#7A5A42", "",
            new[] {
                ("COF-ARA-01", 18.0, false), ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-M", 1.0, false)
            }, order: 6);

        AddProduct("coffee", "Latte", "latte",
            "Espresso và sữa tươi đánh bông mịn.",
            45000, "#B99A78", "#F5EDE0", "",
            new[] {
                ("COF-ARA-01", 18.0, false), ("DAI-MILK-01", 200.0, false),
                ("SYR-SUG-01", 15.0, true), ("OTH-ICE-01", 120.0, true),
                ("PKG-CUP-M", 1.0, false)
            }, order: 7);

        // ---- TRÀ SỮA ----------------------------------------------------------
        AddProduct("milktea", "Trà sữa trân châu đường đen", "tra-sua-tran-chau-duong-den",
            "Trân châu nấu đường đen, ủ nóng liên tục để luôn dẻo.",
            45000, "#8E6647", "#EFE3D2", "best-seller,signature",
            new[] {
                ("TEA-BLK-01", 8.0, false), ("DAI-MILK-01", 200.0, false),
                ("SYR-BRW-01", 30.0, false), ("TOP-BOB-01", 60.0, false),
                ("OTH-ICE-01", 150.0, true), ("PKG-CUP-M", 1.0, false),
                ("PKG-STR-01", 1.0, false)
            }, featured: true, order: 1);

        AddProduct("milktea", "Trà sữa ô long", "tra-sua-o-long",
            "Ô long rang, hậu vị ngọt nhẹ, ít gắt.",
            42000, "#A98B62", "#F0E6D6", "",
            new[] {
                ("TEA-OOL-01", 8.0, false), ("DAI-MILK-01", 200.0, false),
                ("SYR-SUG-01", 25.0, false), ("OTH-ICE-01", 150.0, true),
                ("PKG-CUP-M", 1.0, false), ("PKG-STR-01", 1.0, false)
            }, order: 2);

        AddProduct("milktea", "Trà sữa matcha", "tra-sua-matcha",
            "Matcha Uji nguyên chất, không dùng bột pha sẵn.",
            50000, "#8FAE6B", "#EFF3E4", "new",
            new[] {
                ("POW-MAT-01", 5.0, false), ("DAI-MILK-01", 210.0, false),
                ("SYR-SUG-01", 22.0, false), ("OTH-ICE-01", 150.0, true),
                ("PKG-CUP-M", 1.0, false), ("PKG-STR-01", 1.0, false)
            }, order: 3);

        AddProduct("milktea", "Hồng trà sữa kem cheese", "hong-tra-sua-kem-cheese",
            "Lớp kem cheese mặn mịn phủ trên hồng trà.",
            52000, "#B58358", "#FAF0DC", "best-seller",
            new[] {
                ("TEA-BLK-01", 8.0, false), ("DAI-MILK-01", 180.0, false),
                ("DAI-CHE-01", 40.0, false), ("SYR-SUG-01", 22.0, false),
                ("OTH-ICE-01", 140.0, true), ("PKG-CUP-L", 1.0, false)
            }, order: 4);

        AddProduct("milktea", "Trà sữa khoai môn", "tra-sua-khoai-mon",
            "Vị khoai môn bùi, màu tím nhạt tự nhiên.",
            46000, "#B4A0C4", "#EDE6F2", "",
            new[] {
                ("TEA-JAS-01", 7.0, false), ("DAI-MILK-01", 200.0, false),
                ("SYR-SUG-01", 28.0, false), ("TOP-BOW-01", 40.0, true),
                ("OTH-ICE-01", 150.0, true), ("PKG-CUP-M", 1.0, false)
            }, order: 5);

        // ---- TRÀ TRÁI CÂY -----------------------------------------------------
        // Trà đào dùng đào ngâm HSD 5 ngày — món hay xuất hiện trong kế hoạch AI
        AddProduct("fruit", "Trà đào cam sả", "tra-dao-cam-sa",
            "Đào ngâm, sả tươi đập dập, thơm mát.",
            45000, "#E8944A", "#F7D9B4", "best-seller,signature",
            new[] {
                ("TEA-BLK-01", 6.0, false),  ("FRU-PEA-01", 80.0, false),
                ("FRU-LEM-01", 0.5, false),  ("FRU-LGR-01", 15.0, false),
                ("SYR-SUG-01", 30.0, false), ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-L", 1.0, false),   ("PKG-STR-01", 1.0, false)
            }, featured: true, order: 1);

        AddProduct("fruit", "Trà vải", "tra-vai",
            "Vải ngâm nguyên trái, trà lài ướp hương.",
            43000, "#F0D3D8", "#FAEAEC", "",
            new[] {
                ("TEA-JAS-01", 6.0, false), ("SYR-SUG-01", 32.0, false),
                ("FRU-LEM-01", 0.3, false), ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-L", 1.0, false), ("PKG-STR-01", 1.0, false)
            }, order: 2);

        AddProduct("fruit", "Trà chanh giã tay", "tra-chanh-gia-tay",
            "Chanh tươi giã cùng đá, chua mát, giải nhiệt.",
            35000, "#D8E04A", "#EEF2AE", "best-seller",
            new[] {
                ("TEA-BLK-01", 6.0, false),  ("FRU-LEM-01", 1.5, false),
                ("SYR-SUG-01", 28.0, false), ("OTH-ICE-01", 200.0, true),
                ("PKG-CUP-L", 1.0, false),   ("PKG-STR-01", 1.0, false)
            }, order: 3);

        AddProduct("fruit", "Soda dâu tây", "soda-dau-tay",
            "Dâu tây tươi dằm, soda mát lạnh.",
            48000, "#D63B3B", "#F5B8B8", "new",
            new[] {
                ("FRU-STR-01", 70.0, false), ("SYR-SUG-01", 30.0, false),
                ("FRU-LEM-01", 0.3, false),  ("OTH-ICE-01", 180.0, true),
                ("PKG-CUP-L", 1.0, false),   ("PKG-STR-01", 1.0, false)
            }, order: 4);

        // ---- ĐÁ XAY & SINH TỐ --------------------------------------------------
        AddProduct("blended", "Sinh tố xoài", "sinh-to-xoai",
            "Xoài cát tươi xay cùng sữa, không dùng siro.",
            50000, "#F2B33D", "#FCEBC4", "best-seller",
            new[] {
                ("FRU-MAN-01", 180.0, false), ("DAI-MILK-01", 80.0, false),
                ("SYR-SUG-01", 25.0, false),  ("OTH-ICE-01", 200.0, false),
                ("PKG-CUP-L", 1.0, false),    ("PKG-STR-01", 1.0, false)
            }, featured: true, order: 1);

        AddProduct("blended", "Sinh tố dâu", "sinh-to-dau",
            "Dâu tây tươi xay mịn cùng sữa tươi.",
            55000, "#E05C6E", "#F8CDD4", "",
            new[] {
                ("FRU-STR-01", 150.0, false), ("DAI-MILK-01", 90.0, false),
                ("SYR-SUG-01", 28.0, false),  ("OTH-ICE-01", 200.0, false),
                ("PKG-CUP-L", 1.0, false),    ("PKG-STR-01", 1.0, false)
            }, order: 2);

        AddProduct("blended", "Cacao đá xay", "cacao-da-xay",
            "Cacao nguyên chất xay đá, phủ kem tươi.",
            52000, "#4E3225", "#FBF6EC", "",
            new[] {
                ("POW-CAC-01", 18.0, false),  ("DAI-MILK-01", 150.0, false),
                ("SYR-SUG-01", 25.0, false),  ("DAI-WHIP-01", 30.0, false),
                ("OTH-ICE-01", 200.0, false), ("PKG-CUP-L", 1.0, false)
            }, order: 3);

        AddProduct("blended", "Matcha đá xay", "matcha-da-xay",
            "Matcha xay đá, phủ kem cheese mặn nhẹ.",
            58000, "#8FAE6B", "#FAF0DC", "new",
            new[] {
                ("POW-MAT-01", 7.0, false),   ("DAI-MILK-01", 150.0, false),
                ("SYR-SUG-01", 25.0, false),  ("DAI-CHE-01", 35.0, false),
                ("OTH-ICE-01", 200.0, false), ("PKG-CUP-L", 1.0, false)
            }, order: 4);

        // ---- MATCHA & CACAO -----------------------------------------------------
        AddProduct("matcha", "Matcha latte", "matcha-latte",
            "Matcha Uji đánh tay, sữa tươi nguyên kem.",
            48000, "#7CA24A", "#EFF3E4", "best-seller",
            new[] {
                ("POW-MAT-01", 6.0, false),  ("DAI-MILK-01", 220.0, false),
                ("SYR-SUG-01", 20.0, true),  ("OTH-ICE-01", 150.0, true),
                ("PKG-CUP-M", 1.0, false)
            }, featured: true, order: 1);

        AddProduct("matcha", "Cacao nóng", "cacao-nong",
            "Cacao nguyên chất đánh cùng sữa nóng.",
            42000, "#5C3A21", "#C9A57B", "",
            new[] {
                ("POW-CAC-01", 20.0, false), ("DAI-MILK-01", 200.0, false),
                ("SYR-SUG-01", 22.0, false), ("PKG-CUP-M", 1.0, false)
            }, order: 2);

        // Gắn nhóm topping cho các món trà sữa và đá xay
        foreach (var p in products.Where(x =>
            x.CategoryId == cats["milktea"].Id || x.CategoryId == cats["blended"].Id))
        {
            db.ProductModifiers.Add(new ProductModifier
                { ProductId = p.Id, ModifierGroupId = toppingGroup.Id, SortOrder = 1 });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo {Count} món kèm công thức định lượng", products.Count);

        // ======================================================================
        //  §6  LÔ HÀNG
        //
        //  CỐ Ý tạo vài lô cận hạn để AI Engine có việc phân tích ngay từ lần
        //  chạy đầu tiên. Không có lô cận hạn thì bản kế hoạch sẽ trống rỗng
        //  và người dùng không thấy được tính năng hoạt động thế nào.
        // ======================================================================
        var today = DateTime.UtcNow.Date;
        var rnd = new Random(20260810);   // hạt cố định để seed lặp lại giống nhau

        void AddLot(string sku, double qty, int daysToExpiry, string? suffix = null)
        {
            var item = ing[sku];
            var lot = new InventoryLot
            {
                StoreId           = store.Id,
                IngredientId      = item.Id,
                LotCode           = $"{sku}-{today:yyMMdd}-{suffix ?? "A"}",
                ReceivedQuantity  = qty,
                RemainingQuantity = qty,
                UnitCost          = item.AverageUnitCost,
                ReceivedAt        = DateTime.UtcNow.AddDays(-rnd.Next(0, 3)),
                ExpiryDate        = item.DefaultShelfLifeDays is null
                                        ? null
                                        : today.AddDays(daysToExpiry),
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

        // ⭐ Bốn lô CẬN HẠN — đây là thứ AI sẽ phát hiện và đề xuất xả hàng
        AddLot("DAI-MILK-01", 4000, 2);    // sữa tươi, còn 2 ngày
        AddLot("FRU-PEA-01",  1800, 3);    // đào ngâm, còn 3 ngày
        AddLot("FRU-MAN-01",  2500, 4);    // xoài, còn 4 ngày
        AddLot("TOP-BOB-01",  1500, 1);    // trân châu, còn 1 ngày — khẩn cấp

        // Các lô còn hạn dài — kho vận hành bình thường
        AddLot("COF-ROB-01",  5000, 150);
        AddLot("COF-ARA-01",  3000, 150);
        AddLot("DAI-CON-01",  4000, 80);
        AddLot("DAI-WHIP-01", 2000, 12);
        AddLot("DAI-CHE-01",  1500, 8);
        AddLot("TEA-BLK-01",  1200, 300);
        AddLot("TEA-OOL-01",  800,  300);
        AddLot("TEA-JAS-01",  800,  300);
        AddLot("POW-MAT-01",  600,  150);
        AddLot("POW-CAC-01",  1000, 300);
        AddLot("TOP-BOW-01",  900,  2);
        AddLot("TOP-JEL-01",  1200, 12);
        AddLot("TOP-PUD-01",  800,  2);
        AddLot("FRU-LEM-01",  60,   8);
        AddLot("FRU-LGR-01",  500,  6);
        AddLot("FRU-STR-01",  1000, 3);
        AddLot("SYR-SLT-01",  1500, 4);
        AddLot("SYR-SUG-01",  5000, 25);
        AddLot("SYR-BRW-01",  2000, 25);
        AddLot("SWE-SUG-01",  8000, 700);
        AddLot("OTH-ICE-01",  40000, 0);
        AddLot("OTH-EGG-01",  60,   18);
        AddLot("PKG-CUP-M",   800,  0);
        AddLot("PKG-CUP-L",   600,  0);
        AddLot("PKG-STR-01",  1500, 0);

        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo lô hàng, trong đó 4 lô cận hạn để AI phân tích");

        // ======================================================================
        //  §7  LỊCH SỬ TIÊU THỤ 28 NGÀY
        //
        //  Không có bảng này thì mô hình EWMA trả về 0, dẫn tới TOÀN BỘ tồn kho
        //  bị coi là có nguy cơ phải bỏ — bản kế hoạch đầu tiên sẽ vô nghĩa.
        //
        //  Dữ liệu mô phỏng có tính đến nhịp thật của quán cà phê:
        //    · Cuối tuần đông hơn ngày thường khoảng 40%
        //    · Dao động ngẫu nhiên ±20% giữa các ngày
        // ======================================================================
        var ingredientList = ing.Values.ToList();
        var productList = products.ToList();

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
            foreach (var p in productList)
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
        logger.LogInformation("  DEFAULT_STORE_ID={Id}", store.Id);
        logger.LogInformation("=====================================================");
    }
}
