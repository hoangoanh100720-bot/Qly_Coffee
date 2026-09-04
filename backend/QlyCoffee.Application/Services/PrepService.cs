using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DỊCH VỤ SƠ CHẾ — XUẤT NGUYÊN LIỆU THÔ ĐỂ LÀM BÁN THÀNH PHẨM
//
//  MỘT MẺ SƠ CHẾ LÀ MỘT BÚT TOÁN KÉP HOÀN CHỈNH:
//
//      80g lá hồng trà  ──ProductionOut──▶  ra khỏi kho, theo FEFO
//                                            │
//      2000ml cốt hồng trà  ◀──ProductionIn──┘  vào kho thành MỘT LÔ MỚI
//                                               hạn 6 tiếng kể từ bây giờ
//
//  BA ĐIỀU BẮT BUỘC, VI PHẠM LÀ SAI SỐ LIỆU:
//
//  1. HAI VẾ PHẢI CÙNG SỐNG HOẶC CÙNG CHẾT.
//     Trừ được lá trà mà không tạo được lô cốt trà thì quán vừa mất 80g lá trà
//     mà không có gì để bán. Vì vậy toàn bộ nằm trong một transaction.
//
//  2. GIÁ VỐN MẺ = TỔNG GIÁ VỐN THỰC TẾ CỦA CÁC LÔ ĐÃ BỊ TRỪ, chia cho sản lượng.
//     KHÔNG lấy giá bình quân của nguyên liệu. Lấy bình quân thì mẻ ủ từ lô trà
//     cận hạn giá rẻ lại mang giá vốn của lô mới nhập giá cao — sai cả hai đầu.
//
//  3. THIẾU MỘT NGUYÊN LIỆU THÌ HỦY CẢ MẺ, KHÔNG LÀM MỘT NỬA.
//     ConsumeFefoAsync ném InsufficientStockException, transaction cuốn ngược.
//     Nửa mẻ trà không phải là thứ bán được, và cũng không phải thứ ghi sổ được.
//
//  VÌ SAO KHÔNG CÓ BẢNG "PHIẾU SƠ CHẾ" RIÊNG:
//  Lô hàng SINH RA CHÍNH LÀ cái phiếu. Nó có mã, có thời điểm, có hạn dùng, có
//  giá vốn, và mọi bút toán của mẻ đều trỏ về nó qua reference_id. Thêm một bảng
//  header nữa chỉ là chép lại những gì lô đã có, rồi tới lúc nào đó hai bảng
//  lệch nhau.
// ==============================================================================

public interface IPrepService
{
    /// <summary>Chạy một mẻ sơ chế: trừ nguyên liệu thô, tạo lô bán thành phẩm.</summary>
    Task<PrepBatchResult> ProduceAsync(ProduceBatchCommand cmd, CancellationToken ct = default);

    /// <summary>Toàn cảnh khu sơ chế: công thức nào còn làm được mấy mẻ, mẻ nào sắp hết.</summary>
    Task<List<PrepStatus>> GetStatusAsync(Guid storeId, CancellationToken ct = default);
}

/// <summary>
/// Yêu cầu chạy một mẻ sơ chế.
/// <para>
/// <c>BatchCount</c> cho phép số lẻ vì nhân viên hay ủ nửa bình vào giờ vắng —
/// ủ cả bình rồi đổ đi một nửa là lãng phí có thật, không phải trường hợp hiếm.
/// </para>
/// </summary>
public record ProduceBatchCommand(
    Guid StoreId,
    Guid PrepRecipeId,
    double BatchCount,
    string? Note,
    Guid ActorUserId);

/// <summary>Kết quả một mẻ — đủ để giao diện báo lại cho nhân viên mà không phải hỏi thêm.</summary>
public record PrepBatchResult(
    Guid LotId,
    string LotCode,
    string OutputIngredientName,
    double OutputQuantity,
    string UnitLabel,
    DateTime ExpiresAt,
    int TotalInputCost,
    int UnitCost,
    IReadOnlyList<PrepConsumedLine> Consumed);

public record PrepConsumedLine(string IngredientName, double Quantity, string UnitLabel, int Cost);

/// <summary>
/// Tình trạng một công thức sơ chế tại thời điểm hiện tại.
/// <list type="bullet">
/// <item><c>OutputStock</c> — tồn kho bán thành phẩm, theo đơn vị cơ sở.</item>
/// <item><c>ActiveBatches</c> — các mẻ còn hàng, hạn gần nhất lên trước.</item>
/// <item><c>MaxBatches</c> — số mẻ còn làm được với nguyên liệu thô hiện có.</item>
/// <item><c>BlockingIngredient</c> — nguyên liệu thô đang chặn. null = còn đủ.</item>
/// <item><c>InputStock</c> — tồn kho thô của từng dòng, tra theo IngredientId.</item>
/// </list>
/// </summary>
public record PrepStatus(
    PrepRecipe Recipe,
    double OutputStock,
    IReadOnlyList<InventoryLot> ActiveBatches,
    double MaxBatches,
    string? BlockingIngredient,
    IReadOnlyDictionary<Guid, double> InputStock);

public class PrepService : IPrepService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly ILogger<PrepService> _logger;

    /// <summary>
    /// Trần số mẻ một lần bấm. Không phải giới hạn kỹ thuật mà là lưới chắn lỗi
    /// gõ phím: "20 mẻ" thường là "2 mẻ" gõ thừa số 0, và hậu quả là quét sạch
    /// kho lá trà trong một cú bấm.
    /// </summary>
    private const double MaxBatchesPerRun = 20;

    public PrepService(AppDbContext db, IInventoryService inventory, ILogger<PrepService> logger)
    {
        _db = db;
        _inventory = inventory;
        _logger = logger;
    }

    // ==========================================================================
    //  CHẠY MỘT MẺ  ⭐ HÀM CHÍNH
    // ==========================================================================

    public async Task<PrepBatchResult> ProduceAsync(
        ProduceBatchCommand cmd, CancellationToken ct = default)
    {
        if (cmd.BatchCount <= 0)
            throw new BusinessRuleException("Số mẻ phải lớn hơn 0");

        if (cmd.BatchCount > MaxBatchesPerRun)
            throw new BusinessRuleException(
                $"Nhiều nhất {MaxBatchesPerRun:0.##} mẻ một lần. " +
                "Cần hơn thì bấm làm nhiều lượt — mỗi lượt là một bình riêng, " +
                "có mã lô và hạn dùng riêng.");

        var recipe = await _db.PrepRecipes
            .Include(r => r.OutputIngredient)
            .Include(r => r.Lines).ThenInclude(l => l.Ingredient)
            .FirstOrDefaultAsync(r => r.Id == cmd.PrepRecipeId && r.StoreId == cmd.StoreId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy công thức sơ chế");

        if (!recipe.IsActive)
            throw new BusinessRuleException($"Công thức \"{recipe.Name}\" đã ngừng dùng");

        if (recipe.Lines.Count == 0)
            throw new BusinessRuleException(
                $"Công thức \"{recipe.Name}\" chưa có dòng nguyên liệu nào — " +
                "chạy mẻ bây giờ sẽ tạo ra hàng từ hư không.");

        var output = recipe.OutputIngredient
            ?? throw new BusinessRuleException("Công thức chưa gắn bán thành phẩm đầu ra");

        var outputQuantity = Math.Round(recipe.OutputQuantity * cmd.BatchCount, 4);
        if (outputQuantity <= 0)
            throw new BusinessRuleException("Sản lượng của mẻ phải lớn hơn 0");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Tạo lô TRƯỚC khi trừ nguyên liệu, chỉ để có sẵn Id làm số chứng từ.
        // Lô chưa được ghi xuống database ở bước này — Id sinh ở tầng ứng dụng
        // (GUID v7), nên dùng được ngay mà không cần SaveChanges.
        var lot = new InventoryLot
        {
            StoreId      = cmd.StoreId,
            IngredientId = output.Id,
            PrepRecipeId = recipe.Id,
            LotCode      = GenerateBatchCode(output.Sku),
            ReceivedQuantity  = outputQuantity,
            RemainingQuantity = outputQuantity,
            ReceivedAt   = DateTime.UtcNow,
            ExpiryDate   = DateTime.UtcNow.AddHours(recipe.ShelfLifeHours),
            Status       = LotStatus.Active,
            Note         = string.IsNullOrWhiteSpace(cmd.Note) ? null : cmd.Note.Trim()
        };

        // ---- Vế XUẤT: trừ nguyên liệu thô theo FEFO ------------------------
        var totalInputCost = 0;
        var consumed = new List<PrepConsumedLine>();
        var touchedIngredients = new List<Guid>();

        foreach (var line in recipe.Lines.OrderBy(l => l.SortOrder))
        {
            var need = Math.Round(line.Quantity * cmd.BatchCount, 4);
            if (need <= 0) continue;

            var result = await _inventory.ConsumeFefoAsync(_db, new ConsumeRequest(
                StoreId:        cmd.StoreId,
                IngredientId:   line.IngredientId,
                Quantity:       need,
                ReferenceType:  "PREP",
                // Chứng từ nguồn là chính cái lô sinh ra. Nhờ vậy tra một mẻ cốt
                // trà là ra ngay nó đã ăn hết những lô lá trà nào.
                ReferenceId:    lot.Id,
                IdempotencyKey: $"PREP:{lot.Id}:ING:{line.IngredientId}",
                ActorUserId:    cmd.ActorUserId,
                Type:           MovementType.ProductionOut), ct);

            totalInputCost += result.TotalCost;
            touchedIngredients.Add(line.IngredientId);

            consumed.Add(new PrepConsumedLine(
                line.Ingredient?.Name ?? "Nguyên liệu không rõ",
                need,
                WasteRiskService.UnitLabel(line.Ingredient?.BaseUnit ?? BaseUnit.Gram),
                result.TotalCost));
        }

        // ---- Vế NHẬP: giá vốn của mẻ ---------------------------------------
        // Chia đều tổng chi phí đầu vào cho sản lượng. Phần hao hụt (lá trà ngậm
        // nước, cà phê giữ lại trong phin) đã nằm sẵn trong OutputQuantity, nên
        // không nhân thêm hệ số hao hụt ở đây — nhân nữa là tính hai lần.
        lot.UnitCost = outputQuantity > 0
            ? (int)Math.Round(totalInputCost / outputQuantity)
            : 0;

        _db.InventoryLots.Add(lot);

        _db.StockMovements.Add(new StockMovement
        {
            StoreId        = cmd.StoreId,
            IngredientId   = output.Id,
            LotId          = lot.Id,
            Type           = MovementType.ProductionIn,
            QuantityDelta  = outputQuantity,          // DƯƠNG vì là nhập kho
            UnitCost       = lot.UnitCost,
            TotalCost      = totalInputCost,
            ReferenceType  = "PREP",
            ReferenceId    = lot.Id,
            Reason         = $"Sơ chế: {recipe.Name} ({cmd.BatchCount:0.##} mẻ)",
            IdempotencyKey = $"PREP-IN:{lot.Id}",
            ActorUserId    = cmd.ActorUserId,
            OccurredAt     = lot.ReceivedAt
        });

        await _db.SaveChangesAsync(ct);

        // Mẻ mới có giá khác mẻ cũ → giá vốn bình quân của bán thành phẩm đổi,
        // kéo theo giá vốn mọi món dùng nó. Nguyên liệu thô cũng phải tính lại
        // vì vừa mất đi những lô rẻ nhất hoặc đắt nhất.
        await _inventory.RecalculateAverageCostAsync(output.Id, ct);
        foreach (var id in touchedIngredients.Distinct())
            await _inventory.RecalculateAverageCostAsync(id, ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Sơ chế {Recipe}: {Batches} mẻ → {Qty} {Unit} {Output}, lô {LotCode}, " +
            "hạn {Expiry:HH:mm dd/MM}, giá vốn {Cost}đ",
            recipe.Name, cmd.BatchCount, outputQuantity, output.BaseUnit, output.Name,
            lot.LotCode, lot.ExpiryDate, totalInputCost);

        return new PrepBatchResult(
            LotId:                lot.Id,
            LotCode:              lot.LotCode,
            OutputIngredientName: output.Name,
            OutputQuantity:       outputQuantity,
            UnitLabel:            WasteRiskService.UnitLabel(output.BaseUnit),
            ExpiresAt:            lot.ExpiryDate!.Value,
            TotalInputCost:       totalInputCost,
            UnitCost:             lot.UnitCost,
            Consumed:             consumed);
    }

    // ==========================================================================
    //  TOÀN CẢNH KHU SƠ CHẾ
    // ==========================================================================

    /// <summary>
    /// Trả lời đúng ba câu hỏi nhân viên hỏi khi mở màn hình sơ chế:
    /// còn bao nhiêu cốt, mẻ đang dùng hết hạn lúc mấy giờ, và có đủ lá trà
    /// để ủ thêm không.
    /// </summary>
    public async Task<List<PrepStatus>> GetStatusAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var recipes = await _db.PrepRecipes
            .AsNoTracking()
            .Include(r => r.OutputIngredient)
            .Include(r => r.Lines).ThenInclude(l => l.Ingredient)
            .Where(r => r.StoreId == storeId && r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(ct);

        if (recipes.Count == 0) return new List<PrepStatus>();

        // Nạp tồn kho MỘT LƯỢT cho mọi nguyên liệu liên quan, thay vì gọi
        // GetAvailableStockAsync trong vòng lặp lồng nhau. Chín công thức × bốn
        // dòng là ba mươi sáu lượt gọi database cho một lần mở màn hình.
        var ingredientIds = recipes
            .SelectMany(r => r.Lines.Select(l => l.IngredientId))
            .Concat(recipes.Select(r => r.OutputIngredientId))
            .Distinct()
            .ToList();

        var stock = await _db.InventoryLots
            .AsNoTracking()
            .Where(l => l.StoreId == storeId
                     && ingredientIds.Contains(l.IngredientId)
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0)
            .GroupBy(l => l.IngredientId)
            .Select(g => new { IngredientId = g.Key, Total = g.Sum(l => l.RemainingQuantity) })
            .ToDictionaryAsync(x => x.IngredientId, x => x.Total, ct);

        var outputIds = recipes.Select(r => r.OutputIngredientId).Distinct().ToList();

        var batches = await _db.InventoryLots
            .AsNoTracking()
            .Where(l => l.StoreId == storeId
                     && outputIds.Contains(l.IngredientId)
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0)
            .OrderBy(l => l.ExpiryDate ?? DateTime.MaxValue)
            .ToListAsync(ct);

        var result = new List<PrepStatus>();

        foreach (var r in recipes)
        {
            // Số mẻ còn làm được = min(tồn thô / lượng cần một mẻ) trên mọi dòng.
            // Cùng một phép tính với "còn làm được bao nhiêu ly", chỉ đổi đơn vị.
            var maxBatches = double.MaxValue;
            string? blocking = null;

            foreach (var line in r.Lines)
            {
                if (line.Quantity <= 0) continue;

                var have = stock.GetValueOrDefault(line.IngredientId, 0);
                var possible = have / line.Quantity;

                if (possible < maxBatches)
                {
                    maxBatches = possible;
                    blocking = line.Ingredient?.Name;
                }
            }

            if (maxBatches == double.MaxValue) maxBatches = 0;

            result.Add(new PrepStatus(
                Recipe:             r,
                OutputStock:        Math.Round(stock.GetValueOrDefault(r.OutputIngredientId, 0), 2),
                ActiveBatches:      batches.Where(b => b.IngredientId == r.OutputIngredientId).ToList(),
                MaxBatches:         Math.Floor(maxBatches * 100) / 100,
                BlockingIngredient: maxBatches < 1 ? blocking : null,
                InputStock:         r.Lines.ToDictionary(
                                        l => l.IngredientId,
                                        l => Math.Round(stock.GetValueOrDefault(l.IngredientId, 0), 2))));
        }

        return result;
    }

    /// <summary>
    /// Mã mẻ: [SKU]-[YYMMDD]-[HHmm]. Có giờ phút chứ không phải ký tự ngẫu nhiên
    /// vì một ngày có nhiều mẻ, và câu nhân viên hỏi nhau luôn là "bình mấy giờ".
    /// </summary>
    private static string GenerateBatchCode(string sku)
        => $"{sku}-{VietnamTime.Now():yyMMdd-HHmm}";
}
