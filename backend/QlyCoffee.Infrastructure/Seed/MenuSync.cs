using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Infrastructure.Seed;

// ==============================================================================
//  ĐỒNG BỘ THỰC ĐƠN VÀO DATABASE
// ==============================================================================
//
//  VẤN ĐỀ NÓ GIẢI QUYẾT
//  DatabaseSeeder chỉ chạy khi bảng Stores còn trống. Nghĩa là mọi món thêm vào
//  mã nguồn sau ngày khai trương sẽ KHÔNG BAO GIỜ tới được quán đang chạy, trừ
//  khi xóa sạch database — mà làm vậy là mất hết đơn hàng, sổ kho và lịch sử.
//
//  MenuSync bơm phần CÒN THIẾU vào một database đã có dữ liệu, chạy lại bao
//  nhiêu lần cũng ra cùng một kết quả.
//
//  BỐN NGUYÊN TẮC — vi phạm là làm hỏng dữ liệu thật của quán
//
//  1. CHỈ THÊM, KHÔNG SỬA. Nguyên liệu, danh mục, món đã có thì bỏ qua nguyên vẹn.
//
//  2. KHÔNG BAO GIỜ ĐỘNG VÀO GIÁ BÁN. Giá trong MenuCatalog chỉ dùng lúc TẠO MỚI.
//     Chủ quán chỉnh giá là quyền của người bán; ghi đè lại sau mỗi lần khởi động
//     là lỗi nghiêm trọng nhất mà một hàm đồng bộ có thể mắc.
//
//  3. CÔNG THỨC CHỈ GHI ĐÈ KHI ĐƯỢC YÊU CẦU RÕ RÀNG (refreshRecipes = true),
//     vì chủ quán cũng có quyền sửa định lượng cho hợp khẩu vị khách của mình.
//
//  4. TỒN KHO MỞ ĐẦU LÀ TÙY CHỌN VÀ MẶC ĐỊNH TẮT ở môi trường thật. Tự ý tạo lô
//     hàng nghĩa là bịa ra hàng hóa không có thật trong kho — sổ sách sai ngay.
//     Chỉ bật ở máy phát triển, để menu mới có thể bán thử được ngay.
// ==============================================================================

/// <summary>Kết quả một lượt đồng bộ — dùng cho log và cho phần seed dữ liệu lịch sử.</summary>
public record MenuSyncResult(
    Dictionary<string, Ingredient> IngredientsBySku,
    List<Product> AllProducts,
    List<Product> NewProducts,
    int NewIngredientCount,
    int NewCategoryCount,
    int RefreshedRecipeCount,
    int OpeningLotCount,
    int NewPrepRecipeCount,
    int RefreshedPrepRecipeCount);

public static class MenuSync
{
    /// <summary>
    /// Đưa toàn bộ <see cref="MenuCatalog"/> vào database của một cửa hàng.
    /// </summary>
    /// <param name="db">DbContext đang mở.</param>
    /// <param name="storeId">Cửa hàng nhận thực đơn.</param>
    /// <param name="logger">Ghi lại đã thêm những gì — người vận hành cần đọc được.</param>
    /// <param name="refreshRecipes">
    /// Ghi đè công thức của những món ĐÃ CÓ bằng công thức trong MenuCatalog.
    /// Xóa sạch dòng cũ rồi chép lại dòng mới. Chỉ bật khi cố ý cập nhật định lượng.
    /// </param>
    /// <param name="openingStock">
    /// Tạo lô tồn kho mở đầu cho những nguyên liệu VỪA được thêm. Không có tồn kho
    /// thì món mới hiện "tạm hết" ngay từ phút đầu và không ai đặt thử được.
    /// </param>
    public static async Task<MenuSyncResult> ApplyAsync(
        AppDbContext db,
        Guid storeId,
        ILogger logger,
        bool refreshRecipes = false,
        bool openingStock = false,
        CancellationToken ct = default)
    {
        // ======================================================================
        //  §1  NGUYÊN LIỆU
        // ======================================================================
        var existingIngredients = await db.Ingredients
            .Where(i => i.StoreId == storeId)
            .ToListAsync(ct);

        var ingBySku = existingIngredients.ToDictionary(i => i.Sku, StringComparer.Ordinal);
        var newIngredients = new List<Ingredient>();

        foreach (var spec in MenuCatalog.Ingredients)
        {
            if (ingBySku.ContainsKey(spec.Sku)) continue;

            var item = new Ingredient
            {
                StoreId              = storeId,
                Sku                  = spec.Sku,
                Name                 = spec.Name,
                Category             = spec.Category,
                BaseUnit             = spec.Unit,
                ColorHex             = spec.ColorHex,
                IconKey              = spec.IconKey,
                AverageUnitCost      = spec.UnitCost,
                DefaultShelfLifeDays = spec.ShelfLifeDays,
                ExpiryWarningDays    = spec.WarnDays,
                WastageRate          = spec.WastageRate,
                MinStockLevel        = spec.MinStock,
                ReorderPoint         = spec.ReorderPoint,
                IsPrepared           = spec.Prepared,
                IsActive             = true
            };

            db.Ingredients.Add(item);
            ingBySku[spec.Sku] = item;
            newIngredients.Add(item);
        }

        // ---- Đồng bộ cờ BÁN THÀNH PHẨM lên nguyên liệu ĐÃ CÓ ----------------
        //
        //  Đây là NGOẠI LỆ có chủ đích của nguyên tắc 1 (chỉ thêm, không sửa).
        //
        //  IsPrepared không phải lựa chọn kinh doanh mà là sự thật về cách thứ đó
        //  vào kho: nước đường thì quán nấu, siro đào thì quán mua chai. Chủ quán
        //  không "quyết định" điều đó, y như chủ quán không quyết định croissant
        //  có size L hay không (xem chỗ đồng bộ ServeStyle ở §4).
        //
        //  Cần bước này vì hai nguyên liệu Nước đường và Kem muối đã tồn tại từ
        //  trước khi có tính năng sơ chế. Không đồng bộ thì chúng vĩnh viễn bị coi
        //  là hàng mua ngoài, và màn hình Sơ chế sẽ từ chối nấu chúng.
        var preparedSkus = MenuCatalog.Ingredients
            .Where(s => s.Prepared)
            .Select(s => s.Sku)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in existingIngredients)
        {
            var shouldBePrepared = preparedSkus.Contains(item.Sku);
            if (item.IsPrepared == shouldBePrepared) continue;

            item.IsPrepared = shouldBePrepared;
            item.UpdatedAt = DateTime.UtcNow;
            logger.LogInformation(
                "Đánh dấu {Sku} ({Name}) là bán thành phẩm — từ nay vào kho qua màn hình Sơ chế",
                item.Sku, item.Name);
        }

        if (newIngredients.Count > 0 || db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        // ======================================================================
        //  §2  DANH MỤC
        // ======================================================================
        var existingCategories = await db.Categories
            .Where(c => c.StoreId == storeId)
            .ToListAsync(ct);

        var catBySlug = existingCategories.ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var catByKey = new Dictionary<string, Category>(StringComparer.Ordinal);
        var newCategoryCount = 0;

        foreach (var spec in MenuCatalog.Categories)
        {
            if (catBySlug.TryGetValue(spec.Slug, out var found))
            {
                catByKey[spec.Key] = found;
                continue;
            }

            var cat = new Category
            {
                StoreId     = storeId,
                Name        = spec.Name,
                Slug        = spec.Slug,
                Description = spec.Description,
                ColorHex    = spec.ColorHex,
                IconKey     = spec.IconKey,
                SortOrder   = spec.SortOrder,
                IsActive    = true
            };

            db.Categories.Add(cat);
            catBySlug[spec.Slug] = cat;
            catByKey[spec.Key] = cat;
            newCategoryCount++;
        }

        if (newCategoryCount > 0) await db.SaveChangesAsync(ct);

        // ======================================================================
        //  §3  NHÓM TÙY CHỌN — TOPPING, MỨC ĐƯỜNG, NÓNG/ĐÁ, MỨC ĐÁ
        //
        //  Tra theo TÊN vì tên là thứ người dùng nhìn thấy và cũng là thứ duy
        //  nhất ổn định giữa các lần chạy (Id sinh mới mỗi lần).
        //
        //  Nhưng TÊN CHỈ DÙNG ĐỂ TÌM, không dùng để suy ra ý nghĩa: cột Kind mới
        //  là thứ giao diện đọc khi cần biết nhóm nào là nhóm đá, nhóm nào là
        //  nhóm nóng/đá. Nhờ vậy quán đổi tên nhóm thành "Đá nhiều hay ít" thì
        //  trang đặt món vẫn chạy đúng.
        // ======================================================================
        var groups = await db.ModifierGroups
            .Where(g => g.StoreId == storeId)
            .ToListAsync(ct);

        // Nhóm đã có từ trước cột Kind thì tra theo tên; nhóm tạo sau thì tra
        // thẳng theo Kind, vì lúc đó tên có thể đã bị đổi.
        ModifierGroup? FindGroup(string kind, string name) =>
            groups.FirstOrDefault(g => g.Kind == kind)
            ?? groups.FirstOrDefault(g => g.Kind.Length == 0 && g.Name == name);

        var toppingGroup = FindGroup(ModifierGroupKinds.Topping,     "Topping");
        var sugarGroup   = FindGroup(ModifierGroupKinds.Sugar,       "Mức đường");
        var iceGroup     = FindGroup(ModifierGroupKinds.Ice,         "Mức đá");
        var tempGroup    = FindGroup(ModifierGroupKinds.Temperature, "Dùng nóng hay đá");

        if (toppingGroup is null)
        {
            toppingGroup = new ModifierGroup
            {
                StoreId = storeId, Name = "Topping", Kind = ModifierGroupKinds.Topping,
                MinSelect = 0, MaxSelect = 3, IsRequired = false, SortOrder = 1
            };
            db.ModifierGroups.Add(toppingGroup);
        }

        if (sugarGroup is null)
        {
            sugarGroup = new ModifierGroup
            {
                StoreId = storeId, Name = "Mức đường", Kind = ModifierGroupKinds.Sugar,
                MinSelect = 1, MaxSelect = 1, IsRequired = true, SortOrder = 2
            };
            db.ModifierGroups.Add(sugarGroup);
        }

        // Nóng/đá đứng TRƯỚC mức đá và ngay sau mức đường: mức đá chỉ hiện ra
        // sau khi khách đã chọn dùng đá, nên hai nhóm phải nằm cạnh nhau.
        if (tempGroup is null)
        {
            tempGroup = new ModifierGroup
            {
                StoreId = storeId, Name = "Dùng nóng hay đá", Kind = ModifierGroupKinds.Temperature,
                MinSelect = 1, MaxSelect = 1, IsRequired = true, SortOrder = 3
            };
            db.ModifierGroups.Add(tempGroup);
        }

        if (iceGroup is null)
        {
            iceGroup = new ModifierGroup
            {
                StoreId = storeId, Name = "Mức đá", Kind = ModifierGroupKinds.Ice,
                MinSelect = 1, MaxSelect = 1, IsRequired = true, SortOrder = 4
            };
            db.ModifierGroups.Add(iceGroup);
        }

        // Điền Kind cho những nhóm sinh ra trước khi cột này tồn tại. Chỉ điền
        // khi còn trống — không bao giờ ghi đè giá trị đã có.
        if (toppingGroup.Kind.Length == 0) toppingGroup.Kind = ModifierGroupKinds.Topping;
        if (sugarGroup.Kind.Length   == 0) sugarGroup.Kind   = ModifierGroupKinds.Sugar;
        if (iceGroup.Kind.Length     == 0) iceGroup.Kind     = ModifierGroupKinds.Ice;
        if (tempGroup.Kind.Length    == 0) tempGroup.Kind    = ModifierGroupKinds.Temperature;

        await db.SaveChangesAsync(ct);

        var groupIds = new[] { toppingGroup.Id, sugarGroup.Id, iceGroup.Id, tempGroup.Id };
        var existingModifiers = await db.Modifiers
            .Where(m => groupIds.Contains(m.ModifierGroupId))
            .ToListAsync(ct);

        bool HasModifier(Guid groupId, string name) =>
            existingModifiers.Any(m => m.ModifierGroupId == groupId && m.Name == name);

        // Topping CÓ tiêu tốn nguyên liệu → mỗi cái kèm một công thức riêng.
        foreach (var t in MenuCatalog.Toppings)
        {
            if (HasModifier(toppingGroup.Id, t.Name)) continue;
            if (!ingBySku.TryGetValue(t.Sku, out var toppingIng)) continue;

            var m = new Modifier
            {
                ModifierGroupId = toppingGroup.Id, Name = t.Name,
                PriceDelta = t.PriceDelta, ColorHex = t.ColorHex,
                SortOrder = t.SortOrder, IsActive = true
            };
            db.Modifiers.Add(m);
            db.ModifierRecipeItems.Add(new ModifierRecipeItem
            {
                ModifierId = m.Id, IngredientId = toppingIng.Id, Quantity = t.Quantity
            });
        }

        for (int i = 0; i < MenuCatalog.SugarLevels.Length; i++)
        {
            if (HasModifier(sugarGroup.Id, MenuCatalog.SugarLevels[i])) continue;
            db.Modifiers.Add(new Modifier
            {
                ModifierGroupId = sugarGroup.Id, Name = MenuCatalog.SugarLevels[i],
                PriceDelta = 0, ColorHex = "#D9B368", SortOrder = i, IsActive = true
            });
        }

        for (int i = 0; i < MenuCatalog.IceLevels.Length; i++)
        {
            if (HasModifier(iceGroup.Id, MenuCatalog.IceLevels[i])) continue;
            db.Modifiers.Add(new Modifier
            {
                ModifierGroupId = iceGroup.Id, Name = MenuCatalog.IceLevels[i],
                PriceDelta = 0, ColorHex = "#DCEAF2", SortOrder = i, IsActive = true
            });
        }

        // "Dùng nóng" / "Dùng đá" — không phụ thu, cũng không tốn thêm nguyên
        // liệu nào ngoài đá đã nằm sẵn trong công thức dưới dạng dòng tùy chọn.
        for (int i = 0; i < MenuCatalog.TemperatureChoices.Length; i++)
        {
            var name = MenuCatalog.TemperatureChoices[i];
            if (HasModifier(tempGroup.Id, name)) continue;
            db.Modifiers.Add(new Modifier
            {
                ModifierGroupId = tempGroup.Id, Name = name, PriceDelta = 0,
                ColorHex = ModifierGroupKinds.IsHotChoice(name) ? "#E8944A" : "#DCEAF2",
                SortOrder = i, IsActive = true
            });
        }

        await db.SaveChangesAsync(ct);

        // ======================================================================
        //  §4  MÓN, BIẾN THỂ, TÙY CHỌN VÀ CÔNG THỨC
        // ======================================================================
        var existingProducts = await db.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync(ct);

        var prodBySlug = existingProducts.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        var newProducts = new List<Product>();
        var refreshedRecipes = 0;
        var backfilledPairings = 0;

        foreach (var spec in MenuCatalog.Products)
        {
            if (!catByKey.TryGetValue(spec.CategoryKey, out var category))
            {
                logger.LogWarning(
                    "Bỏ qua món {Slug}: không tìm thấy danh mục {Key}", spec.Slug, spec.CategoryKey);
                continue;
            }

            // ---- Món đã có: chỉ làm mới công thức nếu được yêu cầu ------------
            if (prodBySlug.TryGetValue(spec.Slug, out var existing))
            {
                // Gợi ý thưởng thức là trường MỚI, nên món cũ nào cũng đang trống.
                // Điền vào khi còn trống — nhưng không bao giờ đè lên câu chủ quán
                // đã tự viết, vì đó là lời của quán chứ không phải của lập trình viên.
                if (string.IsNullOrWhiteSpace(existing.PairingNote))
                {
                    var pairing = MenuCatalog.PairingFor(spec.Slug);
                    if (pairing is not null)
                    {
                        existing.PairingNote = pairing;
                        existing.UpdatedAt = DateTime.UtcNow;
                        backfilledPairings++;
                    }
                }

                if (!refreshRecipes) continue;

                var oldLines = await db.RecipeItems
                    .Where(r => r.ProductId == existing.Id)
                    .ToListAsync(ct);

                db.RecipeItems.RemoveRange(oldLines);
                AddRecipeLines(db, existing.Id, spec, ingBySku, logger);

                // Giá vốn đổi theo công thức. GIÁ BÁN thì KHÔNG — xem nguyên tắc 2.
                existing.ComputedCost = MenuCatalog.EstimateCost(spec);

                // Cách phục vụ là thuộc tính cấu trúc chứ không phải lựa chọn kinh
                // doanh (đồ ăn thì mãi mãi không có size L), nên đồng bộ luôn.
                existing.ServeStyle = (int)spec.Serve;

                // Món MỞ topping trong thực đơn nguồn mà chưa gắn nhóm Topping thì
                // gắn thêm. CHỈ THÊM, không bao giờ gỡ: chủ quán tự tắt topping ở
                // món nào thì đó là quyết định của quán, lần đồng bộ sau không
                // được bật lại. Và chỉ chạy khi cố ý làm mới (MENU_SYNC_RECIPES),
                // cùng lúc với công thức — hai thứ đi cùng nhau khi đổi thực đơn.
                if (spec.AllowTopping && spec.Serve != ServeStyle.Food
                    && !await db.ProductModifiers.AnyAsync(
                        l => l.ProductId == existing.Id && l.ModifierGroupId == toppingGroup.Id, ct))
                {
                    db.ProductModifiers.Add(new ProductModifier
                        { ProductId = existing.Id, ModifierGroupId = toppingGroup.Id, SortOrder = 1 });
                    logger.LogInformation("Mở topping cho món {Name}", existing.Name);
                }

                existing.UpdatedAt = DateTime.UtcNow;
                refreshedRecipes++;
                continue;
            }

            // ---- Món mới -----------------------------------------------------
            var product = new Product
            {
                StoreId         = storeId,
                CategoryId      = category.Id,
                Name            = spec.Name,
                Slug            = spec.Slug,
                Description     = spec.Description,
                PairingNote     = MenuCatalog.PairingFor(spec.Slug),
                BasePrice       = spec.Price,
                ComputedCost    = MenuCatalog.EstimateCost(spec),
                ColorPrimaryHex = spec.ColorPrimaryHex,
                ColorAccentHex  = spec.ColorAccentHex,
                Tags            = spec.Tags,
                ServeStyle      = (int)spec.Serve,
                IsActive        = true,
                IsFeatured      = spec.Featured,
                SortOrder       = spec.SortOrder,
                IsAvailable     = true,
                MaxServings     = 9999,
                PrepSeconds     = spec.PrepSeconds > 0
                    ? spec.PrepSeconds
                    : MenuCatalog.PrepSecondsByCategory.GetValueOrDefault(spec.CategoryKey, 90)
            };

            db.Products.Add(product);
            prodBySlug[spec.Slug] = product;
            newProducts.Add(product);

            AddRecipeLines(db, product.Id, spec, ingBySku, logger);

            // ---- Biến thể size -----------------------------------------------
            // Đồ ăn KHÔNG có size: một chiếc croissant không nhân 1,4 lần được.
            if (spec.Serve != ServeStyle.Food)
            {
                db.ProductVariants.Add(new ProductVariant
                {
                    ProductId = product.Id, Name = "Size M", PriceDelta = 0,
                    RecipeMultiplier = 1.0, IsDefault = true, SortOrder = 0, IsActive = true
                });
                db.ProductVariants.Add(new ProductVariant
                {
                    ProductId = product.Id, Name = "Size L", PriceDelta = 8000,
                    RecipeMultiplier = 1.4, IsDefault = false, SortOrder = 1, IsActive = true
                });
            }

            // ---- Nhóm tùy chọn ------------------------------------------------
            if (spec.AllowTopping && spec.Serve != ServeStyle.Food)
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = toppingGroup.Id, SortOrder = 1 });

            if (spec.Serve != ServeStyle.Food)
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = sugarGroup.Id, SortOrder = 2 });

            // Hỏi nóng hay đá chỉ có nghĩa với món pha được cả hai kiểu.
            if (spec.Serve == ServeStyle.HotOrIced)
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = tempGroup.Id, SortOrder = 3 });

            // Mức đá chỉ có nghĩa với đồ uống lạnh. Món nóng-hoặc-đá cũng được
            // gắn, nhưng giao diện chỉ hiện ra sau khi khách chọn "Dùng đá".
            if (spec.Serve is ServeStyle.Iced or ServeStyle.HotOrIced)
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = iceGroup.Id, SortOrder = 4 });
        }

        await db.SaveChangesAsync(ct);

        // ======================================================================
        //  §4b  RÀ LẠI NHÓM NÓNG/ĐÁ VÀ MỨC ĐÁ CHO MÓN ĐÃ CÓ
        //
        //  Đây là NGOẠI LỆ DUY NHẤT của nguyên tắc "chỉ thêm, không sửa", và nó
        //  có lý do rõ ràng: món nào hỏi mức đá là hệ quả của CÁCH PHỤC VỤ, y
        //  như chuyện đồ ăn không có size L. Nó không phải lựa chọn kinh doanh
        //  của chủ quán, nên đồng bộ được mà không giẫm lên quyền của ai.
        //
        //  Không rà lại thì hai lỗi sau tồn tại vĩnh viễn trên quán đang chạy:
        //    - Ly cà phê NÓNG vẫn hỏi khách "bao nhiêu phần trăm đá" (món tạo ra
        //      từ trước khi có cột serve_style đều bị gắn nhóm mức đá).
        //    - Món vừa đổi sang nóng-hoặc-đá thì không bao giờ hiện ô chọn
        //      nóng/đá, vì liên kết nhóm chỉ được tạo lúc thêm món mới.
        //
        //  Chỉ đụng tới hai nhóm này. Topping và mức đường giữ nguyên tuyệt đối:
        //  đó mới là chỗ chủ quán tự quyết món nào cho thêm gì.
        // ======================================================================
        var serveBySlug = MenuCatalog.Products.ToDictionary(s => s.Slug, s => s.Serve, StringComparer.Ordinal);

        var syncedProducts = await db.Products
            .Where(p => p.StoreId == storeId)
            .Include(p => p.ModifierLinks)
            .ToListAsync(ct);

        var relinked = 0;

        foreach (var product in syncedProducts)
        {
            // Món do quán tự thêm không có trong MenuCatalog — vẫn xử lý được
            // vì cách phục vụ đã nằm sẵn trong cột serve_style của chính nó.
            var serve = serveBySlug.TryGetValue(product.Slug, out var s)
                ? s
                : (ServeStyle)product.ServeStyle;

            // Cột serve_style phải khớp với thực đơn nguồn, kể cả khi không làm
            // mới công thức: chỉ có nó mới nói được vì sao món này hỏi nóng/đá.
            // Để lệch thì mọi thứ đọc cột này về sau đều suy luận sai.
            if ((int)serve != product.ServeStyle)
            {
                product.ServeStyle = (int)serve;
                product.UpdatedAt = DateTime.UtcNow;
            }

            var wantsTemp = serve == ServeStyle.HotOrIced;
            var wantsIce  = serve is ServeStyle.Iced or ServeStyle.HotOrIced;

            var tempLink = product.ModifierLinks.FirstOrDefault(l => l.ModifierGroupId == tempGroup.Id);
            var iceLink  = product.ModifierLinks.FirstOrDefault(l => l.ModifierGroupId == iceGroup.Id);

            if (wantsTemp && tempLink is null)
            {
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = tempGroup.Id, SortOrder = 3 });
                relinked++;
            }
            else if (!wantsTemp && tempLink is not null)
            {
                db.ProductModifiers.Remove(tempLink);
                relinked++;
            }

            if (wantsIce && iceLink is null)
            {
                db.ProductModifiers.Add(new ProductModifier
                    { ProductId = product.Id, ModifierGroupId = iceGroup.Id, SortOrder = 4 });
                relinked++;
            }
            else if (!wantsIce && iceLink is not null)
            {
                db.ProductModifiers.Remove(iceLink);
                relinked++;
            }
            else if (iceLink is not null && iceLink.SortOrder < 4)
            {
                // Mức đá phải đứng SAU nhóm nóng/đá, nếu không giao diện hiện
                // mức đá trước cả câu hỏi có dùng đá hay không.
                iceLink.SortOrder = 4;
            }
        }

        // Không dựa vào riêng bộ đếm: cách phục vụ có thể đổi mà không sinh
        // thêm hay bớt liên kết nhóm nào.
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        if (backfilledPairings > 0)
            logger.LogInformation("Đã điền gợi ý thưởng thức cho {Count} món", backfilledPairings);

        if (relinked > 0)
            logger.LogInformation("Đã sửa {Count} liên kết nhóm nóng/đá và mức đá", relinked);

        // ======================================================================
        //  §5  CÔNG THỨC SƠ CHẾ
        //
        //  Tra theo Code trong phạm vi cửa hàng. Cùng luật với món: đã có thì giữ
        //  nguyên, vì chủ quán có quyền chỉnh sản lượng mẻ và hạn dùng cho vừa
        //  cỡ bình và tủ mát của quán mình.
        // ======================================================================
        var existingPreps = await db.PrepRecipes
            .Include(p => p.Lines)
            .Where(p => p.StoreId == storeId)
            .ToListAsync(ct);

        var prepByCode = existingPreps.ToDictionary(p => p.Code, StringComparer.Ordinal);
        var newPrepCount = 0;
        var refreshedPrepCount = 0;

        foreach (var spec in MenuCatalog.PrepRecipes)
        {
            if (!ingBySku.TryGetValue(spec.OutputSku, out var outputIngredient))
            {
                logger.LogWarning(
                    "Bỏ qua công thức sơ chế {Code}: không có bán thành phẩm {Sku} trong kho",
                    spec.Code, spec.OutputSku);
                continue;
            }

            if (prepByCode.TryGetValue(spec.Code, out var existingPrep))
            {
                if (!refreshRecipes) continue;

                db.PrepRecipeLines.RemoveRange(existingPrep.Lines);
                AddPrepLines(db, existingPrep.Id, spec, ingBySku, logger);

                existingPrep.OutputQuantity = spec.OutputQuantity;
                existingPrep.ShelfLifeHours = spec.ShelfLifeHours;
                existingPrep.PrepMinutes    = spec.PrepMinutes;
                existingPrep.Instructions   = spec.Instructions;
                existingPrep.UpdatedAt      = DateTime.UtcNow;
                refreshedPrepCount++;
                continue;
            }

            var prep = new PrepRecipe
            {
                StoreId            = storeId,
                Code               = spec.Code,
                Name               = spec.Name,
                OutputIngredientId = outputIngredient.Id,
                OutputQuantity     = spec.OutputQuantity,
                ShelfLifeHours     = spec.ShelfLifeHours,
                PrepMinutes        = spec.PrepMinutes,
                Instructions       = spec.Instructions,
                SortOrder          = spec.SortOrder,
                IsActive           = true
            };

            db.PrepRecipes.Add(prep);
            prepByCode[spec.Code] = prep;
            newPrepCount++;

            AddPrepLines(db, prep.Id, spec, ingBySku, logger);
        }

        await db.SaveChangesAsync(ct);

        // ======================================================================
        //  §6  TỒN KHO MỞ ĐẦU CHO NGUYÊN LIỆU VỪA THÊM
        //
        //  Chỉ chạy khi được bật rõ ràng. Xem nguyên tắc 4 ở đầu file.
        //
        //  BÁN THÀNH PHẨM BỊ LOẠI TRỪ Ở ĐÂY, kể cả trên máy phát triển. Bịa ra
        //  một bình cốt trà là bịa ra thứ chưa ai ủ, và lô đó sẽ mang hạn dùng
        //  theo NGÀY thay vì theo GIỜ — tức là sai đúng cái nghiệp vụ mà tính
        //  năng sơ chế sinh ra để làm cho đúng. Nguyên liệu THÔ vẫn được tạo,
        //  nên mở máy lên là bấm ủ được ngay.
        // ======================================================================
        var openingLots = 0;

        if (openingStock && newIngredients.Count > 0)
        {
            var today = DateTime.UtcNow.Date;

            foreach (var item in newIngredients)
            {
                if (item.IsPrepared) continue;

                // Nhập gấp đôi điểm đặt hàng lại: đủ bán vài ngày mà không thành
                // một kho ảo to bất thường làm lệch báo cáo giá trị tồn.
                var qty = Math.Max(item.ReorderPoint * 2, item.MinStockLevel);
                if (qty <= 0) continue;

                var lot = new InventoryLot
                {
                    StoreId           = storeId,
                    IngredientId      = item.Id,
                    LotCode           = $"{item.Sku}-{today:yyMMdd}-KHOIDAU",
                    ReceivedQuantity  = qty,
                    RemainingQuantity = qty,
                    UnitCost          = item.AverageUnitCost,
                    ReceivedAt        = DateTime.UtcNow,
                    ExpiryDate        = item.DefaultShelfLifeDays is int days
                                            ? today.AddDays(days)
                                            : null,
                    Status            = LotStatus.Active
                };
                db.InventoryLots.Add(lot);

                db.StockMovements.Add(new StockMovement
                {
                    StoreId        = storeId,
                    IngredientId   = item.Id,
                    LotId          = lot.Id,
                    Type           = MovementType.PurchaseIn,
                    QuantityDelta  = qty,
                    UnitCost       = item.AverageUnitCost,
                    TotalCost      = (int)Math.Round(qty * item.AverageUnitCost),
                    ReferenceType  = "MENU_SYNC",
                    ReferenceId    = lot.Id,
                    IdempotencyKey = $"MENUSYNC:{lot.Id}",
                    OccurredAt     = lot.ReceivedAt
                });

                openingLots++;
            }

            await db.SaveChangesAsync(ct);
        }

        var allProducts = await db.Products.Where(p => p.StoreId == storeId).ToListAsync(ct);

        // In kèm TRẠNG THÁI CỜ chứ không chỉ số đếm. "0 công thức làm mới" một
        // mình là câu vô nghĩa: nó vừa có thể là "cờ đang tắt", vừa có thể là
        // "cờ bật nhưng chẳng có gì phải sửa". Hai tình huống đó cần hai hành
        // động khác hẳn nhau, mà không phân biệt được thì phải đi đọc mã nguồn.
        logger.LogInformation(
            "Đồng bộ thực đơn: +{Ing} nguyên liệu, +{Cat} danh mục, +{Prod} món, "
          + "{Recipe} công thức làm mới ({RecipeFlag}), +{Prep} công thức sơ chế, "
          + "{PrepRefresh} công thức sơ chế làm mới, {Lot} lô khởi đầu ({StockFlag})",
            newIngredients.Count, newCategoryCount, newProducts.Count,
            refreshedRecipes, refreshRecipes ? "MENU_SYNC_RECIPES=true" : "MENU_SYNC_RECIPES=false",
            newPrepCount, refreshedPrepCount,
            openingLots, openingStock ? "MENU_SYNC_OPENING_STOCK=true" : "MENU_SYNC_OPENING_STOCK=false");

        if (newPrepCount > 0)
            logger.LogInformation(
                "Có {Count} công thức sơ chế mới. Món dùng cốt trà hoặc cà phê phin sẽ hiện "
              + "\"chưa sơ chế\" cho tới khi nhân viên chạy mẻ đầu tiên ở trang /admin/so-che. "
              + "Đây là hành vi ĐÚNG, không phải lỗi tồn kho.",
                newPrepCount);

        return new MenuSyncResult(
            ingBySku, allProducts, newProducts,
            newIngredients.Count, newCategoryCount, refreshedRecipes, openingLots,
            NewPrepRecipeCount: newPrepCount, RefreshedPrepRecipeCount: refreshedPrepCount);
    }

    /// <summary>
    /// Chép các dòng nguyên liệu thô của một công thức sơ chế vào database.
    /// <para>
    /// Thiếu nguyên liệu thì GHI CẢNH BÁO chứ không ném lỗi, cùng lý do với
    /// <see cref="AddRecipeLines"/>: một Sku gõ sai không đáng làm sập lần khởi
    /// động, nhưng cũng không được im lặng bỏ qua — thiếu một dòng nghĩa là mẻ
    /// đó không trừ thứ nguyên liệu ấy, và kho dư ảo mãi mãi.
    /// </para>
    /// </summary>
    private static void AddPrepLines(
        AppDbContext db,
        Guid prepRecipeId,
        MenuCatalog.PrepSpec spec,
        Dictionary<string, Ingredient> ingBySku,
        ILogger logger)
    {
        var order = 0;

        foreach (var line in spec.Inputs)
        {
            if (!ingBySku.TryGetValue(line.Sku, out var ingredient))
            {
                logger.LogWarning(
                    "Công thức sơ chế {Code}: không có nguyên liệu {Sku} trong kho, bỏ qua dòng này",
                    spec.Code, line.Sku);
                continue;
            }

            db.PrepRecipeLines.Add(new PrepRecipeLine
            {
                PrepRecipeId = prepRecipeId,
                IngredientId = ingredient.Id,
                Quantity     = line.Quantity,
                Note         = line.Note,
                SortOrder    = order++
            });
        }
    }

    /// <summary>
    /// Chép công thức của một món từ catalog vào database.
    /// <para>
    /// Nguyên liệu không tìm thấy thì GHI CẢNH BÁO chứ không ném lỗi: một dòng
    /// sai chính tả Sku không đáng làm sập cả lần khởi động, nhưng cũng không
    /// được im lặng bỏ qua — thiếu một dòng công thức là kho không trừ thứ đó.
    /// </para>
    /// </summary>
    private static void AddRecipeLines(
        AppDbContext db,
        Guid productId,
        ProductSpec spec,
        Dictionary<string, Ingredient> ingBySku,
        ILogger logger)
    {
        var order = 0;

        foreach (var line in spec.Recipe)
        {
            if (!ingBySku.TryGetValue(line.Sku, out var ingredient))
            {
                logger.LogWarning(
                    "Món {Slug}: không có nguyên liệu {Sku} trong kho, bỏ qua dòng công thức này",
                    spec.Slug, line.Sku);
                continue;
            }

            db.RecipeItems.Add(new RecipeItem
            {
                ProductId    = productId,
                IngredientId = ingredient.Id,
                Quantity     = line.Quantity,
                IsOptional   = line.Optional,
                Note         = line.Note,
                SortOrder    = order++
            });
        }
    }
}
