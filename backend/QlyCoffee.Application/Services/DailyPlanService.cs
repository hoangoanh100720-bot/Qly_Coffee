using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  LỚP 3 — SINH BẢN KẾ HOẠCH HẰNG NGÀY
//
//  Đây là nơi ghép ba lớp lại với nhau:
//
//    Lớp 1 (code)  Tổng hợp tiêu thụ trong ngày, đánh dấu lô quá hạn
//    Lớp 2 (code)  Phân tích rủi ro + sinh phương án khuyến mãi có tính lãi lỗ
//    Lớp 3 (AI)    Claude nhận SỐ ĐÃ TÍNH XONG rồi viết diễn giải và nội dung banner
//
//  ⚠️ RANH GIỚI TUYỆT ĐỐI
//  AI chỉ điền các trường CHỮ: Headline, Summary, Title, Reasoning, BannerCopy.
//  Mọi trường SỐ trong PlanSuggestion đều lấy từ PromotionCandidate do code tính.
//  Sau khi AI trả lời, hệ thống còn lọc bỏ mọi candidateId không tồn tại —
//  phòng trường hợp mô hình bịa ra mã không có thật.
//
//  ⚠️ LỖI AI KHÔNG ĐƯỢC LÀM MẤT KẾ HOẠCH
//  Gọi Claude thất bại thì dùng bản diễn giải mẫu cố định. Số liệu và đề xuất
//  vẫn đầy đủ vì chúng do code tính, không phụ thuộc AI.
// ==============================================================================

public interface IDailyPlanService
{
    Task<DailyPlan> GenerateAsync(Guid storeId, string? businessDate = null, CancellationToken ct = default);
    Task<DailyPlanDto?> GetAsync(Guid storeId, string businessDate, CancellationToken ct = default);
    Task<Promotion?> DecideSuggestionAsync(DecideSuggestionRequest req, Guid actorId, CancellationToken ct = default);
    Task AggregateDailyDataAsync(Guid storeId, string businessDate, CancellationToken ct = default);
}

/// <summary>Trừu tượng hóa việc gọi AI — để thay nhà cung cấp hoặc tắt hẳn mà không sửa logic.</summary>
public interface IAiNarrator
{
    Task<AiPlanNarrative> WriteAsync(PlanNarrativeInput input, CancellationToken ct = default);
}

/// <summary>Dữ liệu đưa cho AI. Tất cả đều là kết quả đã tính, AI không phải tính lại gì.</summary>
public record PlanNarrativeInput(
    string BusinessDate,
    int Revenue,
    int OrderCount,
    int WasteValue,
    IReadOnlyList<LotRiskDto> Risks,
    IReadOnlyList<PromotionCandidate> Candidates,
    IReadOnlyList<(string CandidateId, SuggestionType Type, string Detail)> OtherSuggestions);

/// <summary>Phần chữ do AI viết. Không có trường số nào.</summary>
public record AiPlanNarrative(
    string Headline,
    string Summary,
    IReadOnlyList<AiSuggestionText> Suggestions,
    string? ModelUsed = null,
    int TokensUsed = 0,
    string? Error = null);

public record AiSuggestionText(
    string CandidateId,
    SuggestionPriority Priority,
    string Title,
    string Reasoning,
    string? BannerCopy);

// ==============================================================================

public class DailyPlanService : IDailyPlanService
{
    private readonly AppDbContext _db;
    private readonly IWasteRiskService _riskService;
    private readonly IPromotionPlanner _planner;
    private readonly IInventoryService _inventory;
    private readonly IAiNarrator _narrator;
    private readonly PlanningOptions _opt;
    private readonly ILogger<DailyPlanService> _logger;

    public DailyPlanService(
        AppDbContext db,
        IWasteRiskService riskService,
        IPromotionPlanner planner,
        IInventoryService inventory,
        IAiNarrator narrator,
        IOptions<PlanningOptions> opt,
        ILogger<DailyPlanService> logger)
    {
        _db = db;
        _riskService = riskService;
        _planner = planner;
        _inventory = inventory;
        _narrator = narrator;
        _opt = opt.Value;
        _logger = logger;
    }

    // ==========================================================================
    //  SINH BẢN KẾ HOẠCH — job chạy lúc 23:00 mỗi ngày
    // ==========================================================================

    public async Task<DailyPlan> GenerateAsync(
        Guid storeId, string? businessDate = null, CancellationToken ct = default)
    {
        var date = businessDate ?? VietnamTime.BusinessDate();

        _logger.LogInformation("Bắt đầu sinh kế hoạch ngày {Date}", date);

        // ---- BƯỚC 0: chống tạo trùng --------------------------------------
        var existing = await _db.DailyPlans
            .FirstOrDefaultAsync(p => p.StoreId == storeId && p.BusinessDate == date, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Đã có kế hoạch cho ngày {Date}, bỏ qua", date);
            return existing;
        }

        // ---- BƯỚC 1: tổng hợp số liệu trong ngày ---------------------------
        await AggregateDailyDataAsync(storeId, date, ct);

        // ---- BƯỚC 2: đánh dấu lô quá hạn -----------------------------------
        await _inventory.ExpireOverdueLotsAsync(storeId, ct);

        // ---- BƯỚC 3: phân tích rủi ro (Lớp 2, code thuần) ------------------
        var risks = await _riskService.AnalyzeAsync(storeId, ct);

        // ---- BƯỚC 4: sinh phương án khuyến mãi (Lớp 2, code thuần) --------
        var candidates = risks
            .SelectMany(r => _planner.Generate(r))
            .OrderByDescending(c => c.NetBenefit)
            .Take(_opt.MaxSuggestionsPerPlan)
            .Select((c, i) => c with { CandidateId = $"cand-{i + 1}" })
            .ToList();

        // ---- BƯỚC 5: các đề xuất không phải giảm giá ----------------------
        var others = await GenerateOtherSuggestionsAsync(storeId, risks, candidates, ct);

        // ---- BƯỚC 6: số liệu tổng của ngày --------------------------------
        var summary = await GetDailySummaryAsync(storeId, date, ct);

        // ---- BƯỚC 7: gọi AI viết diễn giải (Lớp 3) -----------------------
        AiPlanNarrative narrative;
        try
        {
            // Bỏ bớt trường IngredientId khi đưa cho AI — nó chỉ cần mã, loại
            // và phần mô tả để viết diễn giải, không cần khóa ngoại.
            var othersForAi = others
                .Select(o => (o.CandidateId, o.Type, o.Detail))
                .ToList();

            narrative = await _narrator.WriteAsync(new PlanNarrativeInput(
                date, summary.Revenue, summary.OrderCount, summary.WasteValue,
                risks, candidates, othersForAi), ct);
        }
        catch (Exception ex)
        {
            // Lỗi AI KHÔNG được làm mất kế hoạch — dùng bản mẫu cố định
            _logger.LogError(ex, "Gọi AI thất bại, dùng bản diễn giải mẫu");
            narrative = BuildFallbackNarrative(risks, candidates, summary) with { Error = ex.Message };
        }

        // ---- BƯỚC 8: lọc mã đề xuất AI bịa ra ------------------------------
        var validIds = candidates.Select(c => c.CandidateId)
            .Concat(others.Select(o => o.CandidateId))
            .ToHashSet();

        var validTexts = narrative.Suggestions
            .Where(s => validIds.Contains(s.CandidateId))
            .ToList();

        if (validTexts.Count != narrative.Suggestions.Count)
            _logger.LogWarning("AI trả về {N} mã đề xuất không tồn tại, đã loại bỏ",
                narrative.Suggestions.Count - validTexts.Count);

        // ---- BƯỚC 9: lưu ---------------------------------------------------
        var plan = new DailyPlan
        {
            StoreId          = storeId,
            BusinessDate     = date,
            GeneratedAt      = DateTime.UtcNow,

            // Số liệu: LẤY TỪ CODE
            TotalValueAtRisk = risks.Sum(r => r.ValueAtRisk),
            CriticalCount    = risks.Count(r => r.Severity == (int)RiskSeverity.Critical),
            WarningCount     = risks.Count(r => r.Severity is (int)RiskSeverity.High or (int)RiskSeverity.Medium),
            LowStockCount    = await CountLowStockAsync(storeId, ct),
            Revenue          = summary.Revenue,
            OrderCount       = summary.OrderCount,
            WasteValue       = summary.WasteValue,
            RiskAnalysisJson = JsonSerializer.Serialize(risks),

            // Diễn giải: LẤY TỪ AI
            Headline     = narrative.Headline,
            Summary      = narrative.Summary,
            AiModel      = narrative.ModelUsed,
            AiTokensUsed = narrative.TokensUsed,
            AiError      = narrative.Error,

            Status = PlanStatus.New
        };

        var sortOrder = 0;
        foreach (var text in validTexts)
        {
            var cand = candidates.FirstOrDefault(c => c.CandidateId == text.CandidateId);
            var other = others.FirstOrDefault(o => o.CandidateId == text.CandidateId);

            plan.Suggestions.Add(new PlanSuggestion
            {
                Type     = cand is not null ? SuggestionType.Discount : other.Type,
                Priority = text.Priority,

                IngredientId = cand?.IngredientId ?? other.IngredientId,
                ProductId    = cand?.ProductId,

                // CHỮ — do AI viết
                Title      = text.Title,
                Reasoning  = text.Reasoning,
                BannerCopy = text.BannerCopy,

                // SỐ — do CODE tính. AI không được chạm vào các trường này.
                DiscountPercent      = cand?.DiscountPercent,
                TargetUnits          = cand?.TargetUnits,
                ExpectedWasteAvoided = cand?.WasteAvoided,
                ExpectedNetBenefit   = cand?.NetBenefit,
                DaysUntilExpiry      = cand?.DaysAvailable,
                QuantityAtRisk       = cand?.QuantityAtRisk,

                SuggestedFrom = cand is not null ? DateTime.UtcNow : null,
                SuggestedTo   = cand is not null ? DateTime.UtcNow.AddDays(cand.DaysAvailable) : null,

                SortOrder = sortOrder++
            });
        }

        _db.DailyPlans.Add(plan);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Đã sinh kế hoạch {Date}: {Risks} lô rủi ro, {Value}đ có nguy cơ, {Sugg} đề xuất",
            date, risks.Count, plan.TotalValueAtRisk, plan.Suggestions.Count);

        return plan;
    }

    // ==========================================================================
    //  TỔNG HỢP SỐ LIỆU TRONG NGÀY
    // ==========================================================================

    /// <summary>
    /// Gom tiêu thụ nguyên liệu và doanh số theo món vào hai bảng tổng hợp.
    /// <para>
    /// Vì sao cần: mô hình dự báo phải đọc 28 ngày lịch sử ở mỗi lần chạy.
    /// Quét bảng sổ cái (hàng trăm nghìn dòng) mỗi lần thì rất chậm.
    /// Ghi sẵn một lần vào đây, sau đó chỉ việc đọc.
    /// </para>
    /// </summary>
    public async Task AggregateDailyDataAsync(
        Guid storeId, string businessDate, CancellationToken ct = default)
    {
        var day = DateTime.Parse(businessDate);
        var from = VietnamTime.ToUtc(day);
        var to = VietnamTime.ToUtc(day.AddDays(1));

        // ---- Tiêu thụ nguyên liệu -----------------------------------------
        var consumption = await _db.StockMovements
            .Where(m => m.StoreId == storeId
                     && m.OccurredAt >= from && m.OccurredAt < to
                     && (m.Type == MovementType.SaleOut || m.Type == MovementType.ProductionOut))
            .GroupBy(m => m.IngredientId)
            .Select(g => new
            {
                IngredientId = g.Key,
                Quantity = g.Sum(m => Math.Abs(m.QuantityDelta)),
                Cost = g.Sum(m => m.TotalCost)
            })
            .ToListAsync(ct);

        foreach (var c in consumption)
        {
            var existing = await _db.DailyConsumptions.FirstOrDefaultAsync(
                x => x.StoreId == storeId
                  && x.IngredientId == c.IngredientId
                  && x.BusinessDate == businessDate, ct);

            if (existing is null)
            {
                _db.DailyConsumptions.Add(new DailyConsumption
                {
                    StoreId = storeId,
                    IngredientId = c.IngredientId,
                    BusinessDate = businessDate,
                    QuantityUsed = c.Quantity,
                    CostUsed = c.Cost
                });
            }
            else
            {
                existing.QuantityUsed = c.Quantity;
                existing.CostUsed = c.Cost;
            }
        }

        // ---- Doanh số theo món --------------------------------------------
        var sales = await _db.OrderItems
            .Where(i => i.Order!.StoreId == storeId
                     && i.Order.PlacedAt >= from && i.Order.PlacedAt < to
                     && i.Order.Status != OrderStatus.Cancelled
                     && i.Order.Status != OrderStatus.Pending)
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Units = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .ToListAsync(ct);

        foreach (var s in sales)
        {
            var existing = await _db.DailySales.FirstOrDefaultAsync(
                x => x.StoreId == storeId
                  && x.ProductId == s.ProductId
                  && x.BusinessDate == businessDate, ct);

            if (existing is null)
            {
                _db.DailySales.Add(new DailySales
                {
                    StoreId = storeId,
                    ProductId = s.ProductId,
                    BusinessDate = businessDate,
                    UnitsSold = s.Units,
                    Revenue = s.Revenue
                });
            }
            else
            {
                existing.UnitsSold = s.Units;
                existing.Revenue = s.Revenue;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<(int Revenue, int OrderCount, int WasteValue)> GetDailySummaryAsync(
        Guid storeId, string businessDate, CancellationToken ct)
    {
        var day = DateTime.Parse(businessDate);
        var from = VietnamTime.ToUtc(day);
        var to = VietnamTime.ToUtc(day.AddDays(1));

        var orders = await _db.Orders
            .Where(o => o.StoreId == storeId
                     && o.PlacedAt >= from && o.PlacedAt < to
                     && o.Status != OrderStatus.Cancelled
                     && o.Status != OrderStatus.Pending)
            .Select(o => o.GrandTotal)
            .ToListAsync(ct);

        var waste = await _db.StockMovements
            .Where(m => m.StoreId == storeId
                     && m.OccurredAt >= from && m.OccurredAt < to
                     && (m.Type == MovementType.Waste || m.Type == MovementType.ExpiredOut))
            .SumAsync(m => (int?)m.TotalCost, ct) ?? 0;

        return (orders.Sum(), orders.Count, waste);
    }

    private async Task<int> CountLowStockAsync(Guid storeId, CancellationToken ct)
    {
        var ingredients = await _db.Ingredients
            .Where(i => i.StoreId == storeId && i.IsActive && i.MinStockLevel > 0)
            .Select(i => new { i.Id, i.MinStockLevel })
            .ToListAsync(ct);

        var count = 0;
        foreach (var ing in ingredients)
        {
            var stock = await _inventory.GetAvailableStockAsync(ing.Id, ct);
            if (stock < ing.MinStockLevel) count++;
        }
        return count;
    }

    // ==========================================================================
    //  ĐỀ XUẤT KHÔNG PHẢI GIẢM GIÁ
    // ==========================================================================

    /// <summary>
    /// Không phải rủi ro nào cũng giải được bằng giảm giá. Bốn tình huống khác:
    ///   · Sắp hết hạn mà không có phương án nào có lợi  → chuẩn bị tiêu hủy
    ///   · Sắp hết hạn nhưng món chở hàng bán quá chậm   → nhân viên mời khách
    ///   · Tồn kho dưới ngưỡng                            → cần nhập thêm
    /// </summary>
    private async Task<List<(string CandidateId, SuggestionType Type, string Detail, Guid? IngredientId)>>
        GenerateOtherSuggestionsAsync(
            Guid storeId,
            List<LotRiskDto> risks,
            List<PromotionCandidate> candidates,
            CancellationToken ct)
    {
        var result = new List<(string, SuggestionType, string, Guid?)>();
        var index = 1;

        // ---- Rủi ro không cứu được bằng giảm giá --------------------------
        var coveredIngredients = candidates.Select(c => c.IngredientId).ToHashSet();

        foreach (var risk in risks.Where(r => !coveredIngredients.Contains(r.IngredientId)))
        {
            if (risk.Severity < (int)RiskSeverity.High) continue;

            var type = risk.DaysUntilExpiry <= 1
                ? SuggestionType.Dispose
                : SuggestionType.StaffPush;

            var detail = type == SuggestionType.Dispose
                ? $"{risk.IngredientName}: {risk.QuantityAtRisk:0.##} {risk.UnitLabel} " +
                  $"({risk.ValueAtRisk:N0}đ) hết hạn ngày mai, không có phương án giảm giá nào có lợi."
                : $"{risk.IngredientName}: còn {risk.DaysUntilExpiry} ngày, " +
                  $"{risk.QuantityAtRisk:0.##} {risk.UnitLabel} có nguy cơ bỏ. " +
                  $"Các món dùng nguyên liệu này bán quá chậm để giảm giá có tác dụng.";

            result.Add(($"other-{index++}", type, detail, risk.IngredientId));
        }

        // ---- Nguyên liệu cần nhập thêm ------------------------------------
        var lowStock = await _db.Ingredients
            .Where(i => i.StoreId == storeId && i.IsActive && i.ReorderPoint > 0)
            .Select(i => new { i.Id, i.Name, i.ReorderPoint, i.BaseUnit })
            .ToListAsync(ct);

        foreach (var ing in lowStock)
        {
            var stock = await _inventory.GetAvailableStockAsync(ing.Id, ct);
            if (stock >= ing.ReorderPoint) continue;

            result.Add((
                $"other-{index++}",
                SuggestionType.Restock,
                $"{ing.Name}: còn {stock:0.##} {WasteRiskService.UnitLabel(ing.BaseUnit)}, " +
                $"dưới điểm đặt hàng lại ({ing.ReorderPoint:0.##}).",
                ing.Id));
        }

        return result
            .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4))
            .ToList();
    }

    // ==========================================================================
    //  BẢN DIỄN GIẢI MẪU — dùng khi AI lỗi
    // ==========================================================================

    /// <summary>
    /// Sinh diễn giải bằng khuôn mẫu cố định. Không hay bằng AI viết nhưng
    /// đủ dùng, và quan trọng nhất là các con số vẫn chính xác 100%.
    /// </summary>
    private static AiPlanNarrative BuildFallbackNarrative(
        List<LotRiskDto> risks,
        List<PromotionCandidate> candidates,
        (int Revenue, int OrderCount, int WasteValue) summary)
    {
        var totalRisk = risks.Sum(r => r.ValueAtRisk);
        var critical = risks.Where(r => r.Severity == (int)RiskSeverity.Critical).ToList();

        var headline = critical.Count > 0
            ? $"{critical.Count} nguyên liệu cần xử lý gấp"
            : totalRisk > 0
                ? $"{totalRisk:N0}đ nguyên liệu có nguy cơ hết hạn"
                : "Hôm nay không có việc gì gấp";

        var sb = new StringBuilder();
        sb.Append($"Doanh thu {summary.Revenue:N0}đ với {summary.OrderCount} đơn. ");

        if (totalRisk > 0)
        {
            sb.Append($"Có {risks.Count} lô nguyên liệu sắp hết hạn, ");
            sb.Append($"tổng giá trị có nguy cơ phải bỏ là {totalRisk:N0}đ. ");
            if (candidates.Count > 0)
                sb.Append($"Hệ thống tìm được {candidates.Count} phương án giảm giá có lợi ròng dương.");
            else
                sb.Append("Không có phương án giảm giá nào có lợi ròng dương.");
        }
        else
        {
            sb.Append("Không có nguyên liệu nào sắp hết hạn mà tốc độ bán hiện tại không kịp tiêu thụ.");
        }

        if (summary.WasteValue > 0)
            sb.Append($" Hao hụt ghi nhận trong ngày: {summary.WasteValue:N0}đ.");

        var suggestions = candidates.Select(c => new AiSuggestionText(
            CandidateId: c.CandidateId,
            Priority: c.DaysAvailable <= 2 ? SuggestionPriority.Critical
                    : c.DaysAvailable <= 4 ? SuggestionPriority.High
                    : SuggestionPriority.Medium,
            Title: $"Giảm {c.DiscountPercent}% {c.ProductName} trong {c.DaysAvailable} ngày",
            Reasoning:
                $"{c.IngredientName} có {c.QuantityAtRisk:0.##} đơn vị nguy cơ phải bỏ. " +
                $"{c.ProductName} bán trung bình {c.BaselineUnits / Math.Max(c.DaysAvailable, 1)} ly/ngày. " +
                $"Giảm {c.DiscountPercent}% dự kiến bán thêm {c.ExpectedExtraUnits} ly, " +
                $"{(c.Feasible ? "đủ tiêu hết trước hạn" : "chưa đủ nhưng vẫn giảm được thiệt hại")}. " +
                $"Lợi ích ròng {c.NetBenefit:N0}đ.",
            BannerCopy: $"{c.ProductName} giảm {c.DiscountPercent}% — chỉ {c.DaysAvailable} ngày"
        )).ToList();

        return new AiPlanNarrative(headline, sb.ToString(), suggestions, ModelUsed: "fallback-template");
    }

    // ==========================================================================
    //  ĐỌC KẾ HOẠCH
    // ==========================================================================

    public async Task<DailyPlanDto?> GetAsync(
        Guid storeId, string businessDate, CancellationToken ct = default)
    {
        var plan = await _db.DailyPlans
            .AsNoTracking()
            .Include(p => p.Suggestions)
            .Include(p => p.Decisions)
            .FirstOrDefaultAsync(p => p.StoreId == storeId && p.BusinessDate == businessDate, ct);

        if (plan is null) return null;

        var risks = string.IsNullOrEmpty(plan.RiskAnalysisJson)
            ? new List<LotRiskDto>()
            : JsonSerializer.Deserialize<List<LotRiskDto>>(plan.RiskAnalysisJson) ?? new();

        // Nạp tên nguyên liệu và món cho từng đề xuất
        var ingredientNames = await _db.Ingredients
            .Where(i => i.StoreId == storeId)
            .ToDictionaryAsync(i => i.Id, i => i.Name, ct);

        var products = await _db.Products
            .Where(p => p.StoreId == storeId)
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.ColorPrimaryHex }, ct);

        var suggestions = plan.Suggestions
            .OrderBy(s => s.SortOrder)
            .Select(s =>
            {
                var decision = plan.Decisions.FirstOrDefault(d => d.SuggestionId == s.Id);
                return new PlanSuggestionDto(
                    Id: s.Id,
                    Type: (int)s.Type,
                    TypeLabel: TypeLabel(s.Type),
                    Priority: (int)s.Priority,
                    PriorityLabel: PriorityLabel(s.Priority),
                    IngredientId: s.IngredientId,
                    IngredientName: s.IngredientId is Guid ig && ingredientNames.TryGetValue(ig, out var iname) ? iname : null,
                    ProductId: s.ProductId,
                    ProductName: s.ProductId is Guid pg && products.TryGetValue(pg, out var p) ? p.Name : null,
                    ProductColorHex: s.ProductId is Guid pg2 && products.TryGetValue(pg2, out var p2) ? p2.ColorPrimaryHex : null,
                    Title: s.Title,
                    Reasoning: s.Reasoning,
                    BannerCopy: s.BannerCopy,
                    DiscountPercent: s.DiscountPercent,
                    TargetUnits: s.TargetUnits,
                    ExpectedWasteAvoided: s.ExpectedWasteAvoided,
                    ExpectedNetBenefit: s.ExpectedNetBenefit,
                    DaysUntilExpiry: s.DaysUntilExpiry,
                    QuantityAtRisk: s.QuantityAtRisk,
                    SuggestedFrom: s.SuggestedFrom,
                    SuggestedTo: s.SuggestedTo,
                    DecisionAction: decision is null ? null : (int)decision.Action,
                    DecisionNote: decision?.Note);
            })
            .ToList();

        return new DailyPlanDto(
            Id: plan.Id,
            BusinessDate: plan.BusinessDate,
            GeneratedAt: plan.GeneratedAt,
            TotalValueAtRisk: plan.TotalValueAtRisk,
            CriticalCount: plan.CriticalCount,
            WarningCount: plan.WarningCount,
            LowStockCount: plan.LowStockCount,
            Revenue: plan.Revenue,
            OrderCount: plan.OrderCount,
            WasteValue: plan.WasteValue,
            Headline: plan.Headline,
            Summary: plan.Summary,
            AiModel: plan.AiModel,
            AiError: plan.AiError,
            Status: (int)plan.Status,
            Risks: risks,
            Suggestions: suggestions);
    }

    // ==========================================================================
    //  DUYỆT ĐỀ XUẤT → TẠO KHUYẾN MÃI THẬT
    // ==========================================================================

    public async Task<Promotion?> DecideSuggestionAsync(
        DecideSuggestionRequest req, Guid actorId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var suggestion = await _db.PlanSuggestions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == req.SuggestionId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy đề xuất");

        var already = await _db.PlanDecisions
            .AnyAsync(d => d.SuggestionId == req.SuggestionId, ct);
        if (already)
            throw new BusinessRuleException("Đề xuất này đã được xử lý trước đó");

        Promotion? promotion = null;
        var action = (DecisionAction)req.Action;

        // Duyệt (0) hoặc duyệt-có-sửa (2) đều tạo khuyến mãi thật.
        // Từ chối (1) chỉ ghi nhật ký, không tạo gì.
        if (action != DecisionAction.Rejected && suggestion.ProductId is Guid productId)
        {
            var discount = req.OverrideDiscountPercent ?? suggestion.DiscountPercent ?? 10;
            var startsAt = req.OverrideStartsAt ?? DateTime.UtcNow;
            var endsAt = req.OverrideEndsAt
                ?? DateTime.UtcNow.AddDays(suggestion.DaysUntilExpiry ?? 2);

            promotion = new Promotion
            {
                StoreId     = suggestion.Plan!.StoreId,
                Name        = suggestion.Title,
                Description = suggestion.Reasoning,
                Type        = PromotionType.PercentOff,
                Value       = discount,
                Status      = PromotionStatus.Active,
                StartsAt    = startsAt,
                EndsAt      = endsAt,
                ProductId   = productId,
                BannerText  = suggestion.BannerCopy,
                SourcePlanId = suggestion.DailyPlanId,
                SourceSuggestionId = suggestion.Id
            };

            _db.Promotions.Add(promotion);
        }

        _db.PlanDecisions.Add(new PlanDecision
        {
            DailyPlanId  = suggestion.DailyPlanId,
            SuggestionId = suggestion.Id,
            Action       = action,
            ActorUserId  = actorId,
            Note         = req.Note,
            ModifiedPayloadJson = action == DecisionAction.Modified
                ? JsonSerializer.Serialize(new
                {
                    discountPercent = req.OverrideDiscountPercent,
                    startsAt = req.OverrideStartsAt,
                    endsAt = req.OverrideEndsAt
                })
                : null,
            CreatedPromotionId = promotion?.Id,
            DecidedAt = DateTime.UtcNow
        });

        // Xử lý xong hết thì đánh dấu kế hoạch đã xem
        var plan = suggestion.Plan!;
        var totalSuggestions = await _db.PlanSuggestions
            .CountAsync(s => s.DailyPlanId == plan.Id, ct);
        var decidedCount = await _db.PlanDecisions
            .CountAsync(d => d.DailyPlanId == plan.Id, ct) + 1;

        if (decidedCount >= totalSuggestions)
            plan.Status = PlanStatus.Reviewed;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation("Quyết định {Action} cho đề xuất {Title}", action, suggestion.Title);

        return promotion;
    }

    // ==========================================================================
    //  NHÃN HIỂN THỊ
    // ==========================================================================

    public static string TypeLabel(SuggestionType t) => t switch
    {
        SuggestionType.Discount       => "Giảm giá",
        SuggestionType.Bundle         => "Combo",
        SuggestionType.StaffPush      => "Nhân viên mời khách",
        SuggestionType.ReducePurchase => "Giảm lượng nhập",
        SuggestionType.Dispose        => "Chuẩn bị tiêu hủy",
        SuggestionType.Restock        => "Cần nhập thêm",
        _ => t.ToString()
    };

    public static string PriorityLabel(SuggestionPriority p) => p switch
    {
        SuggestionPriority.Critical => "Khẩn cấp",
        SuggestionPriority.High     => "Cao",
        SuggestionPriority.Medium   => "Trung bình",
        _                           => "Thấp"
    };
}
