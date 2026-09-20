using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DỊCH VỤ CÔNG THỨC ĐỊNH LƯỢNG
//
//  Nhiệm vụ: dịch một dòng đơn hàng ("2 ly cà phê muối size L, thêm kem cheese")
//  thành danh sách nguyên liệu cần trừ khỏi kho.
//
//  CÔNG THỨC TỔNG:
//
//      lượng cần = [ (định lượng gốc × hệ số size) + Σ(định lượng topping) ]
//                  × (1 + tỉ lệ hao hụt)
//                  × số ly
//
//  BA CHỖ CỰC KỲ DỄ SAI:
//
//  1. Hệ số size CHỈ nhân công thức gốc, KHÔNG nhân topping.
//     Thêm trân châu vào size L vẫn là 40g, không phải 56g. Người pha chế
//     múc một muỗng trân châu như nhau bất kể ly to hay nhỏ.
//
//  2. Hao hụt nhân SAU KHI đã cộng topping, không nhân riêng từng phần.
//
//  3. Nhân số ly ở BƯỚC CUỐI, sau khi đã gộp hết. Nhân sớm sẽ sai khi
//     cùng một nguyên liệu xuất hiện ở cả công thức gốc lẫn topping.
// ==============================================================================

public interface IRecipeService
{
    Task<List<IngredientRequirement>> ExplodeOrderItemAsync(ExplodeRequest request, CancellationToken ct = default);
    Task<List<IngredientRequirement>> AggregateOrderAsync(IEnumerable<ExplodeRequest> items, CancellationToken ct = default);
    Task<int> ComputeProductCostAsync(Guid productId, CancellationToken ct = default);
    Task RecomputeAllProductCostsAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Phiếu pha của một dòng đơn cho màn hình Pha chế. null nếu không có dòng đó.</summary>
    Task<BarRecipeDto?> GetBrewCardAsync(Guid orderItemId, CancellationToken ct = default);
}

public record ExplodeRequest(
    Guid ProductId,
    Guid? VariantId,
    IReadOnlyList<Guid> ModifierIds,
    int Quantity);

/// <summary>Nhu cầu nguyên liệu sau khi đã phân rã công thức.</summary>
public record IngredientRequirement(
    Guid IngredientId,
    string IngredientName,
    double Quantity,
    int UnitCost)
{
    /// <summary>Giá vốn của phần nguyên liệu này.</summary>
    public int Cost => (int)Math.Round(Quantity * UnitCost);
}

public class RecipeService : IRecipeService
{
    private readonly AppDbContext _db;

    public RecipeService(AppDbContext db) => _db = db;

    // ==========================================================================
    //  PHÂN RÃ MỘT DÒNG ĐƠN HÀNG
    // ==========================================================================

    public async Task<List<IngredientRequirement>> ExplodeOrderItemAsync(
        ExplodeRequest req, CancellationToken ct = default)
    {
        // ---- Nạp công thức gốc của món -------------------------------------
        var recipeItems = await _db.RecipeItems
            .AsNoTracking()
            .Include(r => r.Ingredient)
            .Where(r => r.ProductId == req.ProductId)
            .ToListAsync(ct);

        // ---- Hệ số size -----------------------------------------------------
        var multiplier = 1.0;
        if (req.VariantId is Guid vId)
        {
            var variant = await _db.ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vId, ct);
            multiplier = variant?.RecipeMultiplier ?? 1.0;
        }

        // ---- Nạp công thức của các topping đã chọn --------------------------
        var modifierItems = req.ModifierIds.Count > 0
            ? await _db.ModifierRecipeItems
                .AsNoTracking()
                .Include(m => m.Ingredient)
                .Where(m => req.ModifierIds.Contains(m.ModifierId))
                .ToListAsync(ct)
            : new List<ModifierRecipeItem>();

        // ---- Mức đá, mức đường, nóng hay đá khách đã chọn --------------------
        // Ba nhóm này không có công thức riêng, nhưng chúng thay đổi lượng đá
        // và nước đường THẬT SỰ rời khỏi kho. Bỏ qua chúng thì ly "không đá"
        // vẫn trừ 150g đá, ly nóng vẫn trừ đá, ly "không đường" vẫn trừ nước
        // đường — cuối tháng kiểm kê sẽ thấy đá và nước đường "thừa" hoài.
        var choices = new List<(string Name, string Kind)>();
        if (req.ModifierIds.Count > 0)
        {
            var rows = await _db.Modifiers
                .AsNoTracking()
                .Where(m => req.ModifierIds.Contains(m.Id))
                .Select(m => new { m.Name, m.Group!.Kind })
                .ToListAsync(ct);
            choices = rows.Select(r => (r.Name, r.Kind)).ToList();
        }

        var iceFactor = IceFactor(
            choices.Where(c => c.Kind == ModifierGroupKinds.Ice).Select(c => c.Name).FirstOrDefault(),
            choices.Where(c => c.Kind == ModifierGroupKinds.Temperature).Select(c => c.Name).FirstOrDefault());

        var sugarFactor = SugarFactor(
            choices.Where(c => c.Kind == ModifierGroupKinds.Sugar).Select(c => c.Name).FirstOrDefault());

        // ---- Gộp lại --------------------------------------------------------
        // Dùng Dictionary vì một nguyên liệu có thể xuất hiện ở cả công thức gốc
        // lẫn topping (VD: sữa có trong trà sữa, và topping "thêm sữa" cũng dùng sữa).
        var acc = new Dictionary<Guid, (string Name, double Qty, double Wastage, int Cost)>();

        // Bước 1 — công thức gốc, CÓ nhân hệ số size và hệ số đá / đường
        foreach (var r in recipeItems)
        {
            if (r.Ingredient is null) continue;

            var factor = r.Ingredient.Sku == IceSku ? iceFactor
                       // Chỉ dòng đường TÙY CHỌN mới theo mức đường khách chọn.
                       // Dòng bắt buộc là đường thuộc về cấu trúc món (ngâm topping…).
                       : r.Ingredient.Sku == SugarSyrupSku && r.IsOptional ? sugarFactor
                       : 1.0;

            if (factor <= 0) continue;
            Accumulate(acc, r.Ingredient, r.Quantity * multiplier * factor);
        }

        // Bước 2 — topping, KHÔNG nhân hệ số size
        foreach (var m in modifierItems)
        {
            if (m.Ingredient is null) continue;
            Accumulate(acc, m.Ingredient, m.Quantity);
        }

        // Bước 3 — nhân hao hụt rồi nhân số ly
        return acc.Select(kv => new IngredientRequirement(
            kv.Key,
            kv.Value.Name,
            Math.Round(kv.Value.Qty * (1 + kv.Value.Wastage) * req.Quantity, 4),
            kv.Value.Cost
        )).ToList();
    }

    // ==========================================================================
    //  MỨC ĐÁ / MỨC ĐƯỜNG
    // ==========================================================================

    /// <summary>Mã đá viên trong kho (MenuCatalog). Dòng mang mã này theo mức đá.</summary>
    public const string IceSku = "OTH-ICE-01";

    /// <summary>Mã nước đường nhà nấu. Dòng TÙY CHỌN mang mã này theo mức đường.</summary>
    public const string SugarSyrupSku = "SYR-SUG-01";

    // ==========================================================================
    //  PHIẾU PHA — công thức hiện cho người pha ở màn hình Pha chế
    // ==========================================================================

    /// <summary>
    /// Công thức của MỘT dòng đơn, đúng như ly khách gọi: nhân hệ số size, áp
    /// mức đá / mức đường, cộng topping. Định lượng là cho MỘT ly và KHÔNG gồm
    /// hao hụt — hao hụt là phần rơi vãi của kho, người pha đong đúng công thức.
    /// <para>
    /// Dùng CÙNG hệ số với <see cref="ExplodeOrderItemAsync"/>, nên thứ người
    /// pha nhìn thấy khớp với thứ kho sẽ trừ khi bấm Hoàn tất.
    /// </para>
    /// </summary>
    public async Task<BarRecipeDto?> GetBrewCardAsync(Guid orderItemId, CancellationToken ct = default)
    {
        var item = await _db.OrderItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == orderItemId, ct);
        if (item is null) return null;

        var multiplier = 1.0;
        if (item.VariantId is Guid vId)
            multiplier = await _db.ProductVariants.AsNoTracking()
                .Where(v => v.Id == vId)
                .Select(v => (double?)v.RecipeMultiplier)
                .FirstOrDefaultAsync(ct) ?? 1.0;

        var modifierIds = OrderService.ParseModifierIds(item.ModifiersJson);

        var chosen = await _db.Modifiers.AsNoTracking()
            .Where(m => modifierIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Group!.Kind, m.SortOrder })
            .ToListAsync(ct);

        string? Pick(string kind) => chosen.Where(c => c.Kind == kind).Select(c => c.Name).FirstOrDefault();
        var iceChoice   = Pick(ModifierGroupKinds.Ice);
        var tempChoice  = Pick(ModifierGroupKinds.Temperature);
        var sugarChoice = Pick(ModifierGroupKinds.Sugar);
        var iceFactor   = IceFactor(iceChoice, tempChoice);
        var sugarFactor = SugarFactor(sugarChoice);

        var recipe = await _db.RecipeItems.AsNoTracking()
            .Include(r => r.Ingredient)
            .Where(r => r.ProductId == item.ProductId)
            .OrderBy(r => r.IsOptional).ThenBy(r => r.Ingredient!.Category)
            .ToListAsync(ct);

        var lines = new List<BarRecipeLineDto>();

        foreach (var r in recipe)
        {
            if (r.Ingredient is null) continue;
            var ing = r.Ingredient;

            // Bao bì không phải thứ "đong" — người pha lấy ly theo size, không cần đọc.
            if (ing.Category == Domain.Enums.IngredientCategory.Packaging) continue;

            var isIce = ing.Sku == IceSku;
            var isSugar = ing.Sku == SugarSyrupSku && r.IsOptional;
            var factor = isIce ? iceFactor : isSugar ? sugarFactor : 1.0;

            string? skip = null;
            if (factor <= 0)
                skip = isIce
                    ? (tempChoice is not null && ModifierGroupKinds.IsHotChoice(tempChoice)
                        ? "Dùng nóng — không cho đá" : "Khách chọn không đá")
                    : "Khách chọn không đường";

            var note = r.Note;
            if (factor is > 0 and < 1)
                note = string.IsNullOrEmpty(note)
                    ? $"Theo lựa chọn: {(isIce ? iceChoice : sugarChoice)}"
                    : $"{note} · {(isIce ? iceChoice : sugarChoice)}";

            lines.Add(new BarRecipeLineDto(
                ing.Name, ing.ColorHex,
                Math.Round(r.Quantity * multiplier * Math.Max(factor, 0), 1),
                UnitLabelOf(ing.BaseUnit), r.IsOptional, IsTopping: false, note, skip));
        }

        // Topping: không nhân size — một muỗng là một muỗng, ly to hay nhỏ.
        var toppingIds = chosen.Where(c => c.Kind == ModifierGroupKinds.Topping).Select(c => c.Id).ToList();
        if (toppingIds.Count > 0)
        {
            var toppingLines = await _db.ModifierRecipeItems.AsNoTracking()
                .Include(m => m.Ingredient)
                .Where(m => toppingIds.Contains(m.ModifierId))
                .ToListAsync(ct);

            foreach (var t in toppingLines.Where(t => t.Ingredient is not null))
                lines.Add(new BarRecipeLineDto(
                    t.Ingredient!.Name, t.Ingredient.ColorHex, Math.Round(t.Quantity, 1),
                    UnitLabelOf(t.Ingredient.BaseUnit), IsOptional: false, IsTopping: true,
                    "Topping khách chọn thêm", null));
        }

        var choices = string.Join(" · ", chosen
            .Where(c => c.Kind != ModifierGroupKinds.Topping)
            .Select(c => c.Name));

        return new BarRecipeDto(item.Id, item.ProductName, item.VariantName, item.Quantity, choices, lines);
    }

    private static string UnitLabelOf(Domain.Enums.BaseUnit u) => u switch
    {
        Domain.Enums.BaseUnit.Gram       => "g",
        Domain.Enums.BaseUnit.Milliliter => "ml",
        _                                => "cái"
    };

    /// <summary>
    /// Tỉ lệ đá thật sự dùng. Chọn "Dùng nóng" là 0 bất kể mức đá (nhóm mức đá
    /// bị ẩn nhưng có thể còn giá trị mặc định cũ gửi lên).
    /// Không chọn gì = dùng đủ công thức, giữ nguyên hành vi cũ cho đơn cũ.
    /// </summary>
    public static double IceFactor(string? iceChoice, string? temperatureChoice)
    {
        if (temperatureChoice is not null && ModifierGroupKinds.IsHotChoice(temperatureChoice))
            return 0;

        if (string.IsNullOrWhiteSpace(iceChoice)) return 1;

        var s = iceChoice.ToLowerInvariant();
        if (s.Contains("không")) return 0;
        if (s.Contains("ít"))    return 0.3;
        return ParsePercent(s) ?? 1;
    }

    /// <summary>"70% đường" → 0,7 · "Không đường" → 0 · không chọn → 1.</summary>
    public static double SugarFactor(string? sugarChoice)
    {
        if (string.IsNullOrWhiteSpace(sugarChoice)) return 1;

        var s = sugarChoice.ToLowerInvariant();
        if (s.Contains("không")) return 0;
        if (s.Contains("ít"))    return 0.3;
        return ParsePercent(s) ?? 1;
    }

    private static double? ParsePercent(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d{1,3})\s*%");
        return m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value) / 100.0, 0, 1) : null;
    }

    private static void Accumulate(
        Dictionary<Guid, (string Name, double Qty, double Wastage, int Cost)> acc,
        Ingredient ing,
        double quantity)
    {
        if (acc.TryGetValue(ing.Id, out var cur))
            acc[ing.Id] = (cur.Name, cur.Qty + quantity, cur.Wastage, cur.Cost);
        else
            acc[ing.Id] = (ing.Name, quantity, ing.WastageRate, ing.AverageUnitCost);
    }

    // ==========================================================================
    //  GỘP CẢ ĐƠN HÀNG
    // ==========================================================================

    /// <summary>
    /// Gộp nhu cầu nguyên liệu của TOÀN BỘ đơn hàng trước khi trừ kho.
    /// <para>
    /// Vì sao phải gộp: đơn có 2 ly cà phê sữa và 1 ly bạc xỉu đều dùng sữa đặc.
    /// Nếu trừ riêng từng dòng thì gọi FEFO ba lần trên cùng một nguyên liệu,
    /// sinh ba bút toán rời rạc và tốn ba lần khóa hàng. Gộp trước thì chỉ một lần.
    /// </para>
    /// </summary>
    public async Task<List<IngredientRequirement>> AggregateOrderAsync(
        IEnumerable<ExplodeRequest> items, CancellationToken ct = default)
    {
        var acc = new Dictionary<Guid, IngredientRequirement>();

        foreach (var item in items)
        {
            var reqs = await ExplodeOrderItemAsync(item, ct);
            foreach (var r in reqs)
            {
                acc[r.IngredientId] = acc.TryGetValue(r.IngredientId, out var cur)
                    ? cur with { Quantity = Math.Round(cur.Quantity + r.Quantity, 4) }
                    : r;
            }
        }

        return acc.Values.ToList();
    }

    // ==========================================================================
    //  TÍNH GIÁ VỐN MÓN
    // ==========================================================================

    /// <summary>
    /// Giá vốn món = Σ (định lượng × giá vốn bình quân nguyên liệu × (1 + hao hụt)).
    /// <para>
    /// Đây là số DẪN XUẤT, không bao giờ nhập tay. Phải tính lại khi:
    /// (1) sửa công thức, (2) nhập hàng làm đổi giá vốn nguyên liệu.
    /// </para>
    /// </summary>
    public async Task<int> ComputeProductCostAsync(Guid productId, CancellationToken ct = default)
    {
        var items = await _db.RecipeItems
            .AsNoTracking()
            .Include(r => r.Ingredient)
            .Where(r => r.ProductId == productId)
            .ToListAsync(ct);

        var cost = items
            .Where(r => r.Ingredient is not null)
            .Sum(r => r.Quantity * r.Ingredient!.AverageUnitCost * (1 + r.Ingredient.WastageRate));

        var rounded = (int)Math.Round(cost);

        await _db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ComputedCost, rounded), ct);

        return rounded;
    }

    /// <summary>
    /// Tính lại giá vốn cho mọi món. Chạy sau mỗi lần nhập kho (vì giá vốn bình
    /// quân nguyên liệu vừa đổi) và trong job đêm.
    /// </summary>
    public async Task RecomputeAllProductCostsAsync(Guid storeId, CancellationToken ct = default)
    {
        var productIds = await _db.Products
            .Where(p => p.StoreId == storeId && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var id in productIds)
            await ComputeProductCostAsync(id, ct);
    }
}

// ==============================================================================
//  DỊCH VỤ KHẢ DỤNG — món nào còn bán được
// ==============================================================================

public interface IAvailabilityService
{
    /// <summary>
    /// <c>BlockingReason</c> là câu ĐÃ SẴN SÀNG HIỂN THỊ, không phải tên nguyên liệu trần.
    /// <para>
    /// Lý do: ba tình huống chặn bán cần ba câu khác nhau vì chúng dẫn nhân viên
    /// tới ba hành động khác nhau — "Hết Sữa tươi" thì gọi nhà cung cấp,
    /// "Chưa sơ chế Cốt hồng trà" thì đi ủ một bình, "Chưa có công thức" thì báo
    /// quản lý. Ghép chuỗi ở từng nơi gọi thì sớm muộn cũng ra
    /// "Hết Chưa có công thức định lượng".
    /// </para>
    /// </summary>
    Task<(int MaxServings, string? BlockingReason)> ComputeMaxServingsAsync(
        Guid productId, Guid? variantId = null, CancellationToken ct = default);
    Task RecomputeForIngredientsAsync(IEnumerable<Guid> ingredientIds, CancellationToken ct = default);
    Task RecomputeAllAsync(Guid storeId, CancellationToken ct = default);
}

public class AvailabilityService : IAvailabilityService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public AvailabilityService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    /// <summary>
    /// Số ly tối đa còn làm được = min( tồn kho / lượng cần cho 1 ly )
    /// trên mọi nguyên liệu BẮT BUỘC.
    /// <para>
    /// Nguyên liệu tùy chọn (đá, đường) không tính vào, vì hết đá thì vẫn pha
    /// được ly không đá.
    /// </para>
    /// </summary>
    public async Task<(int MaxServings, string? BlockingReason)> ComputeMaxServingsAsync(
        Guid productId, Guid? variantId = null, CancellationToken ct = default)
    {
        var recipe = await _db.RecipeItems
            .AsNoTracking()
            .Include(r => r.Ingredient)
            .Where(r => r.ProductId == productId && !r.IsOptional)
            .ToListAsync(ct);

        // Món chưa có công thức thì không kiểm soát được tồn kho.
        // Trả 0 để buộc người quản lý phải soạn công thức trước khi bán.
        if (recipe.Count == 0) return (0, "Chưa có công thức định lượng");

        var multiplier = 1.0;
        if (variantId is Guid vId)
        {
            var v = await _db.ProductVariants.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == vId, ct);
            multiplier = v?.RecipeMultiplier ?? 1.0;
        }

        var min = int.MaxValue;
        string? blocking = null;

        foreach (var item in recipe)
        {
            if (item.Ingredient is null) continue;

            var stock = await _inventory.GetAvailableStockAsync(item.IngredientId, ct);
            var needPerServing = item.Quantity * multiplier * (1 + item.Ingredient.WastageRate);

            if (needPerServing <= 0) continue;

            var possible = (int)Math.Floor(stock / needPerServing);
            if (possible < min)
            {
                min = possible;

                // Bán thành phẩm hết thì việc phải làm là ĐI Ủ MỘT MẺ, không phải
                // gọi nhà cung cấp. Câu chữ ở đây là thứ nhân viên đọc rồi hành
                // động theo, nên nó phải nói đúng việc cần làm.
                blocking = item.Ingredient.IsPrepared
                    ? $"Chưa sơ chế {item.Ingredient.Name}"
                    : $"Hết {item.Ingredient.Name}";
            }
        }

        return min == int.MaxValue
            ? (9999, null)
            : (Math.Max(min, 0), min == 0 ? blocking : null);
    }

    /// <summary>
    /// Cập nhật lại trạng thái khả dụng của mọi món dùng các nguyên liệu vừa đổi.
    /// Gọi sau MỖI lần kho thay đổi.
    /// </summary>
    public async Task RecomputeForIngredientsAsync(
        IEnumerable<Guid> ingredientIds, CancellationToken ct = default)
    {
        var ids = ingredientIds.ToList();
        if (ids.Count == 0) return;

        var productIds = await _db.RecipeItems
            .Where(r => ids.Contains(r.IngredientId))
            .Select(r => r.ProductId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var pid in productIds)
            await UpdateProductAvailabilityAsync(pid, ct);
    }

    public async Task RecomputeAllAsync(Guid storeId, CancellationToken ct = default)
    {
        var productIds = await _db.Products
            .Where(p => p.StoreId == storeId && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var pid in productIds)
            await UpdateProductAvailabilityAsync(pid, ct);
    }

    private async Task UpdateProductAvailabilityAsync(Guid productId, CancellationToken ct)
    {
        var (maxServings, blocking) = await ComputeMaxServingsAsync(productId, null, ct);

        await _db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.MaxServings, maxServings)
                .SetProperty(p => p.IsAvailable, maxServings > 0)
                // blocking đã là câu hoàn chỉnh ("Hết Sữa tươi" / "Chưa sơ chế
                // Cốt hồng trà"), không ghép thêm tiền tố ở đây.
                .SetProperty(p => p.UnavailableReason,
                    maxServings > 0 ? null : blocking), ct);
    }
}
