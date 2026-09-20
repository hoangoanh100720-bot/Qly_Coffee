using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  CONTROLLER SƠ CHẾ — XUẤT NGUYÊN LIỆU THÔ ĐỂ LÀM BÁN THÀNH PHẨM
//
//  Đây là bước ĐỨNG TRƯỚC bán hàng trong quy trình kho:
//
//      Nhập kho  ──▶  SƠ CHẾ  ──▶  Bán hàng
//      (mua lá     (ủ thành      (bán ly trà,
//       trà về)     cốt trà)      trừ vào cốt)
//
//  Quyền: Staff làm được. Ủ trà là việc của người đứng quầy, không phải việc
//  của quản lý — bắt chờ quản lý duyệt thì đến trưa vẫn chưa có trà bán.
// ==============================================================================

[Route("api/prep")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class PrepController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IPrepService _prep;
    private readonly IRecipeService _recipe;
    private readonly IAvailabilityService _availability;

    public PrepController(
        AppDbContext db,
        IPrepService prep,
        IRecipeService recipe,
        IAvailabilityService availability)
    {
        _db = db;
        _prep = prep;
        _recipe = recipe;
        _availability = availability;
    }

    /// <summary>
    /// Toàn cảnh khu sơ chế: mỗi công thức kèm lượng cốt còn lại, các mẻ đang
    /// dùng và số mẻ còn làm được. Một lần gọi là đủ vẽ cả màn hình.
    /// </summary>
    [HttpGet("recipes")]
    public async Task<IActionResult> GetRecipes(CancellationToken ct)
    {
        var statuses = await _prep.GetStatusAsync(CurrentStoreId, ct);
        if (statuses.Count == 0) return Ok(new List<PrepRecipeDto>());

        var servingsPerUnit = await BuildServingSizeMapAsync(
            statuses.Select(s => s.Recipe.OutputIngredientId).Distinct().ToList(), ct);

        var now = DateTime.UtcNow;

        var result = statuses.Select(s =>
        {
            var r = s.Recipe;
            var output = r.OutputIngredient;
            var unit = WasteRiskService.UnitLabel(output?.BaseUnit ?? BaseUnit.Milliliter);

            // Quy lượng cốt còn lại ra SỐ LY — con số nhân viên thật sự cần.
            // "Còn 380ml cốt hồng trà" không nói lên điều gì; "còn 2 ly" thì có.
            // Dùng định lượng TRUNG BÌNH của các món dùng cốt này, vì mỗi món rót
            // một lượng khác nhau. Đây là ƯỚC TÍNH để nhân viên biết lúc nào phải
            // ủ mẻ mới, không phải con số dùng để trừ kho.
            var perServing = servingsPerUnit.GetValueOrDefault(r.OutputIngredientId, 0);
            var servingsLeft = perServing > 0
                ? (int)Math.Floor(s.OutputStock / perServing)
                : 0;

            return new PrepRecipeDto(
                Id:                 r.Id,
                Code:               r.Code,
                Name:               r.Name,
                Instructions:       r.Instructions,

                OutputIngredientId: r.OutputIngredientId,
                OutputName:         output?.Name ?? "",
                OutputColorHex:     output?.ColorHex ?? "#B8A38A",
                OutputIconKey:      output?.IconKey ?? "generic",
                UnitLabel:          unit,

                OutputQuantity:     r.OutputQuantity,
                ShelfLifeHours:     r.ShelfLifeHours,
                PrepMinutes:        r.PrepMinutes,

                OutputStock:        s.OutputStock,
                ServingsLeft:       servingsLeft,
                MaxBatches:         s.MaxBatches,
                BlockingIngredient: s.BlockingIngredient,

                Inputs: r.Lines.OrderBy(l => l.SortOrder).Select(l => new PrepInputDto(
                    IngredientId:     l.IngredientId,
                    Name:             l.Ingredient?.Name ?? "",
                    ColorHex:         l.Ingredient?.ColorHex ?? "#B8A38A",
                    IconKey:          l.Ingredient?.IconKey ?? "generic",
                    UnitLabel:        WasteRiskService.UnitLabel(l.Ingredient?.BaseUnit ?? BaseUnit.Gram),
                    QuantityPerBatch: l.Quantity,
                    StockAvailable:   s.InputStock.GetValueOrDefault(l.IngredientId, 0),
                    Note:             l.Note)).ToList(),

                ActiveBatches: s.ActiveBatches.Select(b => new PrepBatchDto(
                    LotId:             b.Id,
                    LotCode:           b.LotCode,
                    RemainingQuantity: Math.Round(b.RemainingQuantity, 2),
                    ReceivedQuantity:  Math.Round(b.ReceivedQuantity, 2),
                    UnitCost:          b.UnitCost,
                    ProducedAt:        b.ReceivedAt,
                    ExpiresAt:         b.ExpiryDate,
                    MinutesLeft:       b.ExpiryDate.HasValue
                                           ? (int)Math.Floor((b.ExpiryDate.Value - now).TotalMinutes)
                                           : int.MaxValue,
                    Note:              b.Note)).ToList());
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Chạy một mẻ: trừ nguyên liệu thô theo FEFO và tạo lô bán thành phẩm mới.
    /// </summary>
    [HttpPost("produce")]
    public async Task<IActionResult> Produce(
        [FromBody] ProduceBatchRequest req,
        [FromServices] IRestockService restock,
        CancellationToken ct)
    {
        if (req.BatchCount <= 0)
            return Fail<object>(400, "VALIDATION", "Số mẻ phải lớn hơn 0.");

        try
        {
            var batch = await _prep.ProduceAsync(new ProduceBatchCommand(
                CurrentStoreId, req.PrepRecipeId, req.BatchCount,
                req.Note, CurrentUserId ?? Guid.Empty), ct);

            // Mẻ mới làm đổi giá vốn bán thành phẩm → giá vốn mọi món dùng nó
            // cũng đổi. Và món đang "chưa sơ chế" phải bán lại được NGAY, chứ
            // không phải chờ job 15 phút — nhân viên vừa ủ xong là có khách gọi.
            await _recipe.RecomputeAllProductCostsAsync(CurrentStoreId, ct);
            await _availability.RecomputeAllAsync(CurrentStoreId, ct);

            // Bán thành phẩm cũng chặn đơn được, và cách "nhập" nó là ủ một mẻ.
            // Ủ xong thì đóng việc lại y như nhập kho, nếu không bảng cần nhập
            // vẫn đòi cốt trà trong khi bình mới đã nằm sẵn trên quầy.
            await restock.MarkRestockedAsync(
                CurrentStoreId, batch.OutputIngredientId, batch.OutputQuantity, ct);

            return Ok(new PrepBatchResultDto(
                LotId:          batch.LotId,
                LotCode:        batch.LotCode,
                OutputName:     batch.OutputIngredientName,
                OutputQuantity: batch.OutputQuantity,
                UnitLabel:      batch.UnitLabel,
                ExpiresAt:      batch.ExpiresAt,
                TotalInputCost: batch.TotalInputCost,
                UnitCost:       batch.UnitCost,
                Consumed:       batch.Consumed
                    .Select(c => new PrepConsumedDto(c.IngredientName, c.Quantity, c.UnitLabel, c.Cost))
                    .ToList()));
        }
        catch (InsufficientStockException ex)
        {
            // Câu này hiện thẳng lên màn hình quầy, nên phải nói rõ thiếu bao
            // nhiêu chứ không chỉ "không đủ nguyên liệu".
            return Fail<object>(400, "INSUFFICIENT_STOCK", ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_PREP", ex.Message);
        }
    }

    /// <summary>
    /// Định lượng TRUNG BÌNH cho một ly, theo từng bán thành phẩm.
    /// <para>
    /// Gộp một truy vấn cho tất cả bán thành phẩm thay vì hỏi từng cái, vì màn
    /// hình sơ chế nạp cả chín công thức cùng lúc.
    /// </para>
    /// <para>
    /// Bỏ qua dòng công thức có định lượng bằng 0 để chúng không kéo trung bình
    /// xuống và làm số ly ước tính phồng lên gấp đôi sự thật.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, double>> BuildServingSizeMapAsync(
        List<Guid> ingredientIds, CancellationToken ct)
    {
        if (ingredientIds.Count == 0) return new Dictionary<Guid, double>();

        return await _db.RecipeItems
            .AsNoTracking()
            .Where(r => ingredientIds.Contains(r.IngredientId) && r.Quantity > 0)
            .GroupBy(r => r.IngredientId)
            .Select(g => new { IngredientId = g.Key, Average = g.Average(r => r.Quantity) })
            .ToDictionaryAsync(x => x.IngredientId, x => x.Average, ct);
    }
}
