using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  CONTROLLER QUẢN LÝ (yêu cầu đăng nhập)
//
//  PHÂN QUYỀN:
//    Staff   — xem đơn, đổi trạng thái đơn, nhập kho, ghi hao hụt
//    Manager — thêm quyền: sửa món & công thức, duyệt kế hoạch AI, xem báo cáo
//    Owner   — toàn quyền
//
//  Quyền được kiểm ở BA nơi: middleware, thuộc tính [Authorize] trên controller,
//  và [Authorize] trên từng action nếu chặt hơn. Chỉ chặn ở giao diện là KHÔNG ĐỦ.
// ==============================================================================

// ==============================================================================
//  KHO
// ==============================================================================

[Route("api/inventory")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class InventoryController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAvailabilityService _availability;
    private readonly IRecipeService _recipe;

    public InventoryController(
        AppDbContext db,
        IInventoryService inventory,
        IAvailabilityService availability,
        IRecipeService recipe)
    {
        _db = db;
        _inventory = inventory;
        _availability = availability;
        _recipe = recipe;
    }

    /// <summary>Danh sách nguyên liệu kèm tồn kho tổng hợp và cảnh báo cận hạn.</summary>
    [HttpGet("ingredients")]
    public async Task<IActionResult> GetIngredients(CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;

        var ingredients = await _db.Ingredients
            .AsNoTracking()
            .Include(i => i.Lots.Where(l => l.Status == LotStatus.Active && l.RemainingQuantity > 0))
            .Where(i => i.StoreId == CurrentStoreId && i.IsActive)
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .ToListAsync(ct);

        var result = ingredients.Select(i =>
        {
            var activeLots = i.Lots.ToList();
            var totalStock = activeLots.Sum(l => l.RemainingQuantity);

            var withExpiry = activeLots.Where(l => l.ExpiryDate.HasValue).ToList();
            var nearestDays = withExpiry.Count > 0
                ? (int?)withExpiry.Min(l => (l.ExpiryDate!.Value.Date - today).TotalDays)
                : null;

            // Lượng đang nằm ở các lô sắp hết hạn theo ngưỡng riêng của nguyên liệu
            var nearExpiryQty = withExpiry
                .Where(l => (l.ExpiryDate!.Value.Date - today).TotalDays <= i.ExpiryWarningDays)
                .Sum(l => l.RemainingQuantity);

            return new IngredientStockDto(
                Id: i.Id,
                Name: i.Name,
                Sku: i.Sku,
                Category: (int)i.Category,
                CategoryLabel: CategoryLabel(i.Category),
                BaseUnit: (int)i.BaseUnit,
                UnitLabel: WasteRiskService.UnitLabel(i.BaseUnit),
                ColorHex: i.ColorHex,
                IconKey: i.IconKey,
                TotalStock: Math.Round(totalStock, 2),
                MinStockLevel: i.MinStockLevel,
                ReorderPoint: i.ReorderPoint,
                AverageUnitCost: i.AverageUnitCost,
                StockValue: (int)Math.Round(totalStock * i.AverageUnitCost),
                ActiveLotCount: activeLots.Count,
                NearestExpiryDays: nearestDays,
                QuantityNearExpiry: Math.Round(nearExpiryQty, 2),
                IsBelowMinimum: i.MinStockLevel > 0 && totalStock < i.MinStockLevel,
                IsBelowReorderPoint: i.ReorderPoint > 0 && totalStock < i.ReorderPoint);
        }).ToList();

        return Ok(result);
    }

    /// <summary>Danh sách lô hàng, sắp theo FEFO (hết hạn sớm nhất lên đầu).</summary>
    [HttpGet("lots")]
    public async Task<IActionResult> GetLots(CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;

        var lots = await _db.InventoryLots
            .AsNoTracking()
            .Include(l => l.Ingredient)
            .Include(l => l.Supplier)
            .Where(l => l.StoreId == CurrentStoreId
                     && l.Status != LotStatus.Disposed
                     && l.RemainingQuantity > 0)
            .OrderBy(l => l.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(l => l.ReceivedAt)
            .ToListAsync(ct);

        var result = lots.Select(l =>
        {
            var days = l.ExpiryDate.HasValue
                ? (int?)(l.ExpiryDate.Value.Date - today).TotalDays
                : null;

            return new InventoryLotDto(
                Id: l.Id,
                IngredientId: l.IngredientId,
                IngredientName: l.Ingredient?.Name ?? "",
                IngredientColorHex: l.Ingredient?.ColorHex ?? "#B8A38A",
                IngredientIconKey: l.Ingredient?.IconKey ?? "generic",
                UnitLabel: l.Ingredient is null ? "" : WasteRiskService.UnitLabel(l.Ingredient.BaseUnit),
                LotCode: l.LotCode,
                ReceivedQuantity: l.ReceivedQuantity,
                RemainingQuantity: l.RemainingQuantity,
                UnitCost: l.UnitCost,
                RemainingValue: (int)Math.Round(l.RemainingQuantity * l.UnitCost),
                ReceivedAt: l.ReceivedAt,
                ExpiryDate: l.ExpiryDate,
                DaysUntilExpiry: days,
                Status: (int)l.Status,
                StatusLabel: LotStatusLabel(l.Status),
                Severity: (int)ClassifySeverity(days, (int)Math.Round(l.RemainingQuantity * l.UnitCost)),
                SupplierName: l.Supplier?.Name);
        }).ToList();

        return Ok(result);
    }

    /// <summary>Nhập kho — tạo lô mới và cập nhật giá vốn bình quân.</summary>
    [HttpPost("receive")]
    public async Task<IActionResult> Receive([FromBody] ReceiveStockRequest req, CancellationToken ct)
    {
        if (req.Quantity <= 0)
            return Fail<object>(400, "VALIDATION", "Số lượng nhập phải lớn hơn 0.");
        if (req.UnitCost < 0)
            return Fail<object>(400, "VALIDATION", "Giá vốn không được âm.");

        try
        {
            var lot = await _inventory.ReceiveStockAsync(new ReceiveStockCommand(
                CurrentStoreId, req.IngredientId, req.Quantity, req.PurchaseUnitId,
                req.UnitCost, req.ExpiryDate, req.LotCode, req.SupplierId, req.Note,
                CurrentUserId ?? Guid.Empty), ct);

            // Nhập hàng làm đổi giá vốn nguyên liệu → giá vốn mọi món dùng nó cũng đổi
            await _recipe.RecomputeAllProductCostsAsync(CurrentStoreId, ct);
            await _availability.RecomputeForIngredientsAsync(new[] { req.IngredientId }, ct);

            return Ok(new { lot.Id, lot.LotCode, lot.RemainingQuantity, lot.ExpiryDate });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_RECEIVE", ex.Message);
        }
    }

    /// <summary>Ghi nhận hao hụt. Bắt buộc có lý do.</summary>
    [HttpPost("waste")]
    public async Task<IActionResult> RecordWaste([FromBody] RecordWasteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Fail<bool>(400, "VALIDATION", "Phải nhập lý do hao hụt.");

        try
        {
            await _inventory.RecordWasteAsync(new WasteCommand(
                CurrentStoreId, req.IngredientId, req.LotId, req.Quantity,
                req.Reason, CurrentUserId ?? Guid.Empty), ct);

            await _availability.RecomputeForIngredientsAsync(new[] { req.IngredientId }, ct);
            return Ok(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InsufficientStockException)
        {
            return Fail<bool>(400, "INVALID_WASTE", ex.Message);
        }
    }

    /// <summary>Tiêu hủy lô đã quá hạn và ghi bút toán hao hụt.</summary>
    [HttpPost("lots/{id:guid}/dispose")]
    public async Task<IActionResult> DisposeLot(Guid id, CancellationToken ct)
    {
        var lot = await _db.InventoryLots.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lot is null) return Fail<bool>(404, "NOT_FOUND", "Không tìm thấy lô hàng.");

        if (lot.RemainingQuantity > 0)
        {
            await _inventory.RecordWasteAsync(new WasteCommand(
                lot.StoreId, lot.IngredientId, lot.Id, lot.RemainingQuantity,
                $"Tiêu hủy lô quá hạn {lot.LotCode}", CurrentUserId ?? Guid.Empty), ct);
        }

        lot.Status = LotStatus.Disposed;
        await _db.SaveChangesAsync(ct);
        await _availability.RecomputeForIngredientsAsync(new[] { lot.IngredientId }, ct);

        return Ok(true);
    }

    // --- Nhãn hiển thị --------------------------------------------------------

    private static string CategoryLabel(IngredientCategory c) => c switch
    {
        IngredientCategory.Coffee    => "Cà phê",
        IngredientCategory.Tea       => "Trà",
        IngredientCategory.Dairy     => "Sữa & kem",
        IngredientCategory.Syrup     => "Siro & sốt",
        IngredientCategory.Fruit     => "Trái cây",
        IngredientCategory.Topping   => "Topping",
        IngredientCategory.Powder    => "Bột",
        IngredientCategory.Sweetener => "Chất tạo ngọt",
        IngredientCategory.Packaging => "Bao bì",
        IngredientCategory.Bakery    => "Bánh & đồ ăn",
        _                            => "Khác"
    };

    private static string LotStatusLabel(LotStatus s) => s switch
    {
        LotStatus.Active   => "Đang dùng",
        LotStatus.Depleted => "Đã hết",
        LotStatus.Expired  => "Quá hạn",
        LotStatus.Disposed => "Đã hủy",
        _                  => s.ToString()
    };

    private static RiskSeverity ClassifySeverity(int? days, int value)
    {
        if (days is null) return RiskSeverity.Low;
        if (days <= 1) return RiskSeverity.Critical;
        if (days <= 3) return value >= 200_000 ? RiskSeverity.Critical : RiskSeverity.High;
        if (days <= 7) return value >= 500_000 ? RiskSeverity.High : RiskSeverity.Medium;
        return RiskSeverity.Low;
    }
}

// ==============================================================================
//  MÓN & CÔNG THỨC
// ==============================================================================

[Route("api/admin/products")]
[Authorize(Roles = "Manager,Owner")]
public class ProductsAdminController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IRecipeService _recipe;
    private readonly IAvailabilityService _availability;
    private readonly IInventoryService _inventory;

    public ProductsAdminController(
        AppDbContext db, IRecipeService recipe,
        IAvailabilityService availability, IInventoryService inventory)
    {
        _db = db;
        _recipe = recipe;
        _availability = availability;
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // Lấy về rồi mới dựng DTO, KHÔNG Select thẳng trong truy vấn: phần định giá
        // gọi Pricing.Suggest() và Pricing.Verdict() — hàm C# thuần, EF không dịch
        // sang SQL được. Bảng món của một quán chỉ vài chục dòng nên nạp hết vô hại.
        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.RecipeItems)
            .Where(p => p.StoreId == CurrentStoreId)
            .OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.SortOrder)
            .ToListAsync(ct);

        return Ok(products.Select(MapProduct).ToList());
    }

    /// <summary>
    /// Dựng DTO món cho trang quản lý, kèm phần gợi ý giá.
    /// <para>
    /// Gom về một chỗ vì cả bảng danh sách lẫn màn hình soạn công thức đều cần
    /// đúng những con số này — tách ra hai nơi là sớm muộn lệch nhau.
    /// </para>
    /// </summary>
    private static ProductAdminDto MapProduct(Domain.Entities.Product p)
    {
        var targetRatio = p.ServeStyle == 2
            ? Pricing.FoodCostRatioPercent
            : Pricing.DrinkCostRatioPercent;

        return new ProductAdminDto(
            p.Id, p.Name, p.Slug, p.Category?.Name ?? "",
            p.ColorPrimaryHex, p.ColorAccentHex,
            p.BasePrice, p.ComputedCost,
            Pricing.MarginPercent(p.BasePrice, p.ComputedCost),
            p.IsActive, p.IsAvailable, p.MaxServings, p.UnavailableReason,
            p.RecipeItems.Count, p.ImageUrl,
            p.ServeStyle,
            targetRatio,
            Pricing.Suggest(p.ComputedCost, targetRatio),
            Pricing.CostRatioPercent(p.BasePrice, p.ComputedCost),
            Pricing.Verdict(p.BasePrice, p.ComputedCost, targetRatio));
    }

    /// <summary>
    /// Đổi RIÊNG giá bán của một món, không đụng tới công thức.
    /// <para>
    /// Có endpoint riêng thay vì bắt đi qua màn hình soạn công thức vì đổi giá là
    /// việc làm hàng tuần (theo giá nguyên liệu, theo mùa, theo đối thủ), còn sửa
    /// công thức là việc làm vài tháng một lần. Bắt mở cả trang công thức chỉ để
    /// sửa một con số là tạo cơ hội sửa nhầm định lượng.
    /// </para>
    /// <para>
    /// KHÔNG chặn giá thấp hơn giá vốn. Quán có quyền bán lỗ một món để kéo khách
    /// hoặc để xả nguyên liệu cận hạn — hệ thống chỉ CẢNH BÁO qua PriceVerdict,
    /// không quyết thay người bán.
    /// </para>
    /// </summary>
    [HttpPatch("{id:guid}/price")]
    public async Task<IActionResult> UpdatePrice(
        Guid id, [FromBody] UpdatePriceRequest req, CancellationToken ct)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.RecipeItems)
            .FirstOrDefaultAsync(p => p.Id == id && p.StoreId == CurrentStoreId, ct);

        if (product is null)
            return Fail<ProductAdminDto>(404, "NOT_FOUND", "Không tìm thấy món.");

        if (req.BasePrice <= 0)
            return Fail<ProductAdminDto>(400, "VALIDATION", "Giá bán phải lớn hơn 0.");

        // Trần 10 triệu một phần: không phải quy tắc kinh doanh mà là chốt chặn
        // lỗi gõ phím — thêm ba số 0 vào 45.000 ra 45.000.000 thì đơn hàng, báo cáo
        // và biểu đồ doanh thu đều hỏng theo.
        if (req.BasePrice > 10_000_000)
            return Fail<ProductAdminDto>(400, "VALIDATION",
                "Giá bán vượt quá 10.000.000đ một phần. Kiểm tra lại xem có gõ thừa số 0 không.");

        product.BasePrice = req.BasePrice;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(MapProduct(product));
    }

    public record UpdatePriceRequest(int BasePrice);

    /// <summary>
    /// Nạp một lượt ba khối dữ liệu cho màn hình soạn công thức.
    /// Gộp thay vì ba lần gọi để trang không hiện từng phần rời rạc.
    /// </summary>
    [HttpGet("{id:guid}/recipe")]
    public async Task<IActionResult> GetRecipe(Guid id, CancellationToken ct)
    {
        var p = await _db.Products.AsNoTracking()
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (p is null) return Fail<object>(404, "NOT_FOUND", "Không tìm thấy món.");

        var recipeItems = await _db.RecipeItems
            .AsNoTracking()
            .Include(r => r.Ingredient)
            .Where(r => r.ProductId == id)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

        var lines = new List<RecipeLineDto>();
        foreach (var r in recipeItems)
        {
            if (r.Ingredient is null) continue;
            lines.Add(new RecipeLineDto
            {
                Id = r.Id,
                IngredientId = r.IngredientId,
                IngredientName = r.Ingredient.Name,
                IngredientColorHex = r.Ingredient.ColorHex,
                IngredientIconKey = r.Ingredient.IconKey,
                UnitLabel = WasteRiskService.UnitLabel(r.Ingredient.BaseUnit),
                Quantity = r.Quantity,
                IsOptional = r.IsOptional,
                Note = r.Note,
                UnitCost = r.Ingredient.AverageUnitCost,
                WastageRate = r.Ingredient.WastageRate,
                CurrentStock = await _inventory.GetAvailableStockAsync(r.IngredientId, ct)
            });
        }

        // Danh sách nguyên liệu để chọn thêm
        var ingredients = await _db.Ingredients
            .AsNoTracking()
            .Where(i => i.StoreId == CurrentStoreId && i.IsActive)
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .ToListAsync(ct);

        var ingredientDtos = new List<IngredientStockDto>();
        foreach (var i in ingredients)
        {
            var stock = await _inventory.GetAvailableStockAsync(i.Id, ct);
            ingredientDtos.Add(new IngredientStockDto(
                i.Id, i.Name, i.Sku, (int)i.Category, i.Category.ToString(),
                (int)i.BaseUnit, WasteRiskService.UnitLabel(i.BaseUnit),
                i.ColorHex, i.IconKey,
                stock, i.MinStockLevel, i.ReorderPoint, i.AverageUnitCost,
                (int)Math.Round(stock * i.AverageUnitCost),
                0, null, 0, false, false));
        }

        // p nạp bằng AsNoTracking nên RecipeItems rỗng — gán lại để MapProduct
        // đếm đúng số dòng công thức thay vì báo 0.
        p.RecipeItems = recipeItems;
        var dto = MapProduct(p);

        return Ok(new { Product = dto, Lines = lines, Ingredients = ingredientDtos });
    }

    /// <summary>
    /// Lưu công thức. Thay thế TOÀN BỘ các dòng cũ — đơn giản và không sợ sót
    /// dòng đã xóa ở giao diện.
    /// </summary>
    [HttpPut("{id:guid}/recipe")]
    public async Task<IActionResult> SaveRecipe(
        Guid id, [FromBody] SaveRecipeRequest req, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return Fail<bool>(404, "NOT_FOUND", "Không tìm thấy món.");

        if (req.BasePrice <= 0)
            return Fail<bool>(400, "VALIDATION", "Giá bán phải lớn hơn 0.");
        if (req.Lines.Count == 0)
            return Fail<bool>(400, "VALIDATION",
                "Công thức phải có ít nhất một nguyên liệu, nếu không hệ thống không trừ được kho.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        product.BasePrice = req.BasePrice;
        product.ColorPrimaryHex = req.ColorPrimaryHex;
        product.ColorAccentHex = req.ColorAccentHex;

        var old = await _db.RecipeItems.Where(r => r.ProductId == id).ToListAsync(ct);
        _db.RecipeItems.RemoveRange(old);

        var order = 0;
        foreach (var line in req.Lines)
        {
            _db.RecipeItems.Add(new Domain.Entities.RecipeItem
            {
                ProductId = id,
                IngredientId = line.IngredientId,
                Quantity = line.Quantity,
                IsOptional = line.IsOptional,
                Note = line.Note,
                SortOrder = order++
            });
        }

        await _db.SaveChangesAsync(ct);

        // Công thức đổi → giá vốn và trạng thái khả dụng đều phải tính lại
        await _recipe.ComputeProductCostAsync(id, ct);
        await _availability.RecomputeForIngredientsAsync(
            req.Lines.Select(l => l.IngredientId), ct);

        await tx.CommitAsync(ct);
        return Ok(true);
    }

    public record SaveRecipeRequest(
        int BasePrice,
        string ColorPrimaryHex,
        string ColorAccentHex,
        List<RecipeLineInput> Lines);

    public record RecipeLineInput(Guid IngredientId, double Quantity, bool IsOptional, string? Note);
}

// ==============================================================================
//  KẾ HOẠCH AI
// ==============================================================================

[Route("api/plans")]
[Authorize(Roles = "Manager,Owner")]
public class PlansController : BaseApiController
{
    private readonly IDailyPlanService _plans;
    private readonly AppDbContext _db;

    public PlansController(IDailyPlanService plans, AppDbContext db)
    {
        _plans = plans;
        _db = db;
    }

    /// <summary>Lịch sử các bản kế hoạch, mới nhất lên đầu.</summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var plans = await _db.DailyPlans
            .AsNoTracking()
            .Include(p => p.Suggestions)
            .Include(p => p.Decisions)
            .Where(p => p.StoreId == CurrentStoreId)
            .OrderByDescending(p => p.BusinessDate)
            .Take(60)
            .Select(p => new DailyPlanSummaryDto(
                p.Id, p.BusinessDate, p.Headline,
                p.TotalValueAtRisk, p.CriticalCount,
                p.Suggestions.Count,
                p.Suggestions.Count - p.Decisions.Count,
                (int)p.Status))
            .ToListAsync(ct);

        return Ok(plans);
    }

    /// <summary>Chi tiết bản kế hoạch của một ngày.</summary>
    [HttpGet("{businessDate}")]
    public async Task<IActionResult> Get(string businessDate, CancellationToken ct)
    {
        var plan = await _plans.GetAsync(CurrentStoreId, businessDate, ct);
        return plan is null
            ? Fail<DailyPlanDto>(404, "NOT_FOUND", "Chưa có kế hoạch cho ngày này.")
            : Ok(plan);
    }

    /// <summary>
    /// Chạy ngay job phân tích thay vì chờ 23:00.
    /// Dùng để xem thử, hoặc chạy lại khi job đêm bị lỗi.
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var plan = await _plans.GenerateAsync(CurrentStoreId, null, ct);
        var dto = await _plans.GetAsync(CurrentStoreId, plan.BusinessDate, ct);
        return Ok(dto!);
    }

    /// <summary>Duyệt, sửa hoặc bỏ qua một đề xuất. Duyệt sẽ tạo khuyến mãi thật.</summary>
    [HttpPost("decide")]
    public async Task<IActionResult> Decide(
        [FromBody] DecideSuggestionRequest req, CancellationToken ct)
    {
        try
        {
            await _plans.DecideSuggestionAsync(req, CurrentUserId ?? Guid.Empty, ct);
            return Ok(true);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<bool>(400, "INVALID_DECISION", ex.Message);
        }
    }
}

// ==============================================================================
//  BẢNG ĐIỀU KHIỂN
// ==============================================================================

[Route("api/admin")]
[Authorize(Roles = "Manager,Owner")]
public class DashboardController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IWasteRiskService _risk;

    public DashboardController(AppDbContext db, IWasteRiskService risk)
    {
        _db = db;
        _risk = risk;
    }

    /// <summary>
    /// Bốn con số cho huy hiệu trên thanh điều hướng.
    /// <para>
    /// Gộp thành MỘT endpoint vì thanh điều hướng gọi lại mỗi 60 giây trên mọi
    /// trang quản lý. Bốn lần gọi riêng lẻ nhân với số nhân viên đang mở máy là
    /// lượng truy vấn hoàn toàn vô ích.
    /// </para>
    /// </summary>
    [HttpGet("badges")]
    [Authorize(Roles = "Staff,Manager,Owner")]
    public async Task<IActionResult> GetBadges(CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;
        var horizonUtc = VietnamTime.CalendarDayUtc(today.AddDays(2));

        // Đơn online chờ nhân viên bấm xác nhận
        var pendingOrders = await _db.Orders.CountAsync(
            o => o.StoreId == CurrentStoreId && o.Status == OrderStatus.Pending, ct);

        // Số LY đang nằm trong hàng pha, không phải số đơn — người pha quan tâm
        // mình còn phải làm mấy ly, chứ mấy bill thì không nói lên khối lượng.
        var cupsInQueue = await _db.OrderItems
            .Where(i => i.Order!.StoreId == CurrentStoreId
                     && (i.Order.Status == OrderStatus.Confirmed
                      || i.Order.Status == OrderStatus.Preparing
                      || i.Order.Status == OrderStatus.Ready))
            .SumAsync(i => i.Quantity, ct);

        // Lô còn dưới 2 ngày là hết hạn
        var criticalLots = await _db.InventoryLots.CountAsync(
            l => l.StoreId == CurrentStoreId
              && l.Status == LotStatus.Active
              && l.RemainingQuantity > 0
              && l.ExpiryDate != null
              && l.ExpiryDate <= horizonUtc, ct);

        // Đề xuất của kế hoạch mới nhất mà chưa ai duyệt hay bỏ qua
        var latestPlanId = await _db.DailyPlans
            .Where(p => p.StoreId == CurrentStoreId)
            .OrderByDescending(p => p.BusinessDate)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        var undecided = 0;
        if (latestPlanId is Guid planId)
        {
            var total = await _db.PlanSuggestions.CountAsync(s => s.DailyPlanId == planId, ct);
            var decided = await _db.PlanDecisions.CountAsync(d => d.DailyPlanId == planId, ct);
            undecided = Math.Max(0, total - decided);
        }

        // Bán thành phẩm CẦN Ủ LẠI: hết sạch, hoặc còn dưới ngưỡng tối thiểu.
        //
        //  Ngưỡng tối thiểu của nhóm bán thành phẩm mang nghĩa "sắp phải ủ mẻ mới"
        //  chứ không phải "sắp phải gọi nhà cung cấp" (xem MenuCatalog §1), nên
        //  đếm ở đây là đếm đúng số việc nhân viên phải làm ngay.
        //
        //  Tính trong bộ nhớ chứ không trong SQL: một quán chỉ có chưa tới hai
        //  chục bán thành phẩm, mà so tổng tồn với ngưỡng riêng của từng cái
        //  trong một câu SQL thì phải gom nhóm rồi nối bảng, dài mà chẳng nhanh hơn.
        var prepared = await _db.Ingredients
            .AsNoTracking()
            .Where(i => i.StoreId == CurrentStoreId && i.IsActive && i.IsPrepared)
            .Select(i => new
            {
                i.Id,
                i.MinStockLevel,
                Stock = i.Lots
                    .Where(l => l.Status == LotStatus.Active && l.RemainingQuantity > 0)
                    .Sum(l => l.RemainingQuantity)
            })
            .ToListAsync(ct);

        var prepNeeded = prepared.Count(p => p.Stock <= 0 || p.Stock < p.MinStockLevel);

        return Ok(new NavBadgesDto(pendingOrders, cupsInQueue, criticalLots, undecided, prepNeeded));
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;
        var from = VietnamTime.ToUtc(today);
        var to = VietnamTime.ToUtc(today.AddDays(1));

        // ---- Số liệu hôm nay -----------------------------------------------
        var todayOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == CurrentStoreId
                     && o.PlacedAt >= from && o.PlacedAt < to
                     && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.GrandTotal, o.CostTotal, o.Status })
            .ToListAsync(ct);

        var revenue = todayOrders.Where(o => o.Status != OrderStatus.Pending).Sum(o => o.GrandTotal);
        var cost = todayOrders.Where(o => o.Status != OrderStatus.Pending).Sum(o => o.CostTotal);
        var pending = todayOrders.Count(o => o.Status == OrderStatus.Pending);

        // ---- Rủi ro hết hạn -------------------------------------------------
        var risks = await _risk.AnalyzeAsync(CurrentStoreId, ct);

        // ---- Xu hướng 7 ngày ------------------------------------------------
        var trend = new List<DailyRevenuePoint>();
        for (int i = 6; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            var key = d.ToString("yyyy-MM-dd");
            var dayFrom = VietnamTime.ToUtc(d);
            var dayTo = VietnamTime.ToUtc(d.AddDays(1));

            var stats = await _db.Orders
                .AsNoTracking()
                .Where(o => o.StoreId == CurrentStoreId
                         && o.PlacedAt >= dayFrom && o.PlacedAt < dayTo
                         && o.Status != OrderStatus.Cancelled
                         && o.Status != OrderStatus.Pending)
                .Select(o => o.GrandTotal)
                .ToListAsync(ct);

            trend.Add(new DailyRevenuePoint(key, stats.Sum(), stats.Count));
        }

        // ---- Món bán chạy hôm nay -------------------------------------------
        // KHÔNG dùng Include ở đây. Include yêu cầu EF trả về nguyên thực thể,
        // mà GroupBy lại đòi gộp dòng — hai thứ này không dịch chung sang SQL được.
        // Chỉ cần tham chiếu i.Product trong phép gộp là EF tự sinh JOIN.
        //
        // Gộp vào kiểu ẩn danh trước rồi mới dựng DTO: constructor có tham số
        // đặt ngay trong Select sau GroupBy cũng là dạng EF không dịch nổi.
        var topRaw = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == CurrentStoreId
                     && i.Order.PlacedAt >= from && i.Order.PlacedAt < to
                     && i.Order.Status != OrderStatus.Cancelled
                     && i.Order.Status != OrderStatus.Pending)
            // Gộp thêm ImageUrl để thẻ "bán chạy" hiện đúng ảnh của món đó, thay
            // vì một hình ly vẽ chỉ khác nhau ở màu.
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Product!.ColorPrimaryHex, i.Product.ImageUrl })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.ColorPrimaryHex,
                g.Key.ImageUrl,
                UnitsSold = g.Sum(x => x.Quantity),
                Revenue   = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.UnitsSold)
            .Take(5)
            .ToListAsync(ct);

        var topProducts = topRaw
            .Select(x => new TopProductDto(
                x.ProductId, x.ProductName, x.ColorPrimaryHex, x.UnitsSold, x.Revenue, x.ImageUrl))
            .ToList();

        // ---- Kế hoạch AI mới nhất -------------------------------------------
        var latestPlan = await _db.DailyPlans
            .AsNoTracking()
            .Include(p => p.Suggestions)
            .Include(p => p.Decisions)
            .Where(p => p.StoreId == CurrentStoreId)
            .OrderByDescending(p => p.BusinessDate)
            .Select(p => new DailyPlanSummaryDto(
                p.Id, p.BusinessDate, p.Headline,
                p.TotalValueAtRisk, p.CriticalCount,
                p.Suggestions.Count,
                p.Suggestions.Count - p.Decisions.Count,
                (int)p.Status))
            .FirstOrDefaultAsync(ct);

        // ---- Nguyên liệu dưới ngưỡng ----------------------------------------
        var lowStockCount = 0;
        var ingredients = await _db.Ingredients
            .AsNoTracking()
            .Include(i => i.Lots.Where(l => l.Status == LotStatus.Active))
            .Where(i => i.StoreId == CurrentStoreId && i.IsActive && i.MinStockLevel > 0)
            .ToListAsync(ct);

        foreach (var i in ingredients)
            if (i.Lots.Sum(l => l.RemainingQuantity) < i.MinStockLevel) lowStockCount++;

        return Ok(new DashboardDto(
            TodayRevenue: revenue,
            TodayOrderCount: todayOrders.Count(o => o.Status != OrderStatus.Pending),
            TodayGrossProfit: revenue - cost,
            ValueAtRisk: risks.Sum(r => r.ValueAtRisk),
            CriticalLotCount: risks.Count(r => r.Severity == (int)RiskSeverity.Critical),
            LowStockIngredientCount: lowStockCount,
            PendingOrderCount: pending,
            RevenueTrend: trend,
            TopProducts: topProducts,
            LatestPlan: latestPlan));
    }

    /// <summary>Danh sách đơn cho màn hình xử lý đơn.</summary>
    [HttpGet("orders")]
    [Authorize(Roles = "Staff,Manager,Owner")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int? status, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 30;

        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.StoreId == CurrentStoreId);

        if (status.HasValue)
            query = query.Where(o => o.Status == (OrderStatus)status.Value);

        var total = await query.CountAsync(ct);

        var orders = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = orders.Select(o => new OrderDto(
            o.Id, o.Code, o.CustomerName, o.CustomerPhone,
            (int)o.OrderType, (int)o.Status, OrderService.StatusLabel(o.Status),
            (int)o.PaymentMethod, (int)o.PaymentStatus,
            o.Subtotal, o.DiscountTotal, o.GrandTotal, o.CostTotal,
            o.TaxMode, o.TaxRatePercent, o.NetAmount, o.TaxAmount,
            o.Note, o.PlacedAt, o.ConfirmedAt, o.ReadyAt, o.CompletedAt,
            o.Items.Select(i => new OrderItemDto(
                i.Id, i.ProductId, i.ProductName, i.VariantName,
                i.Product?.ColorPrimaryHex ?? "#4A2C17",
                i.Product?.ColorAccentHex ?? "#C89968",
                i.Quantity, i.UnitPrice, i.LineTotal,
                new List<CartModifier>(), i.Note, i.Product?.ImageUrl)).ToList()))
            .ToList();

        return Ok(new PagedResult<OrderDto>(items, total, page, pageSize));
    }
}
