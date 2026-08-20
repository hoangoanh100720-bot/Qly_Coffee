using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Infrastructure.Persistence;

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

        // ---- Gộp lại --------------------------------------------------------
        // Dùng Dictionary vì một nguyên liệu có thể xuất hiện ở cả công thức gốc
        // lẫn topping (VD: sữa có trong trà sữa, và topping "thêm sữa" cũng dùng sữa).
        var acc = new Dictionary<Guid, (string Name, double Qty, double Wastage, int Cost)>();

        // Bước 1 — công thức gốc, CÓ nhân hệ số size
        foreach (var r in recipeItems)
        {
            if (r.Ingredient is null) continue;
            Accumulate(acc, r.Ingredient, r.Quantity * multiplier);
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
    Task<(int MaxServings, string? BlockingIngredient)> ComputeMaxServingsAsync(
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
    public async Task<(int MaxServings, string? BlockingIngredient)> ComputeMaxServingsAsync(
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
                blocking = item.Ingredient.Name;
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
                .SetProperty(p => p.UnavailableReason,
                    maxServings > 0 ? null : $"Hết {blocking}"), ct);
    }
}
