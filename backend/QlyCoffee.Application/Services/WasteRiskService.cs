using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  LỚP 2 — PHÂN TÍCH RỦI RO HẾT HẠN & SINH PHƯƠNG ÁN KHUYẾN MÃI
//
//  ⚠️ TOÀN BỘ FILE NÀY LÀ CODE, KHÔNG CÓ AI.
//
//  Mọi con số tiền và số lượng đều được tính bằng công thức tường minh ở đây.
//  Claude chỉ nhận kết quả đã tính xong để viết diễn giải (xem DailyPlanService).
//  Nếu để mô hình ngôn ngữ làm phép tính, báo cáo tài chính sẽ sai mà không ai
//  phát hiện được — vì câu chữ vẫn rất trôi chảy và thuyết phục.
//
//  BỐN BƯỚC:
//    1. Dự báo tiêu thụ mỗi ngày (EWMA trên 28 ngày lịch sử)
//    2. Tính lượng có nguy cơ phải bỏ = còn lại − dự kiến dùng hết trước hạn
//    3. Tìm "món chở hàng" — món nào tiêu thụ nguyên liệu đó nhiều nhất
//    4. Thử từng mức giảm giá, tính lợi ích ròng, chọn phương án tốt nhất
// ==============================================================================

public interface IWasteRiskService
{
    Task<List<LotRiskDto>> AnalyzeAsync(Guid storeId, CancellationToken ct = default);
    Task<double> ForecastDailyUsageAsync(Guid ingredientId, CancellationToken ct = default);
}

/// <summary>Tham số nghiệp vụ, nạp từ file .env — chỉnh được mà không phải sửa code.</summary>
public class PlanningOptions
{
    /// <summary>Chỉ quan tâm lô hết hạn trong bao nhiêu ngày tới.</summary>
    public int WasteRiskHorizonDays { get; set; } = 14;

    /// <summary>Không bao giờ giảm giá xuống dưới mức biên lợi nhuận này.</summary>
    public int MinMarginPercent { get; set; } = 15;

    /// <summary>
    /// Độ co giãn giá của đồ uống. Âm nghĩa là giảm giá thì bán được nhiều hơn.
    /// −1.6 nghĩa là giảm giá 10% thì sản lượng tăng khoảng 16%.
    /// Khi tích lũy đủ dữ liệu khuyến mãi thật thì nên ước lượng lại từ thực tế.
    /// </summary>
    public double DefaultPriceElasticity { get; set; } = -1.6;

    /// <summary>Số đề xuất tối đa trong một bản kế hoạch — nhiều quá thì rối.</summary>
    public int MaxSuggestionsPerPlan { get; set; } = 6;

    /// <summary>Số ngày lịch sử dùng để dự báo.</summary>
    public int ForecastLookbackDays { get; set; } = 28;

    /// <summary>Hệ số EWMA. Càng lớn càng ưu tiên dữ liệu gần đây.</summary>
    public double ForecastEwmaAlpha { get; set; } = 0.25;
}

public class WasteRiskService : IWasteRiskService
{
    private readonly AppDbContext _db;
    private readonly PlanningOptions _opt;

    public WasteRiskService(AppDbContext db, IOptions<PlanningOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    // ==========================================================================
    //  BƯỚC 1 — DỰ BÁO TIÊU THỤ
    // ==========================================================================

    /// <summary>
    /// Ước lượng lượng tiêu thụ trung bình mỗi ngày bằng EWMA
    /// (trung bình trượt có trọng số mũ).
    /// <para>
    /// Vì sao EWMA chứ không phải trung bình cộng: quán thay đổi theo mùa và
    /// theo khuyến mãi. Trung bình cộng 28 ngày cho tuần đầu và tuần cuối cùng
    /// trọng số, trong khi tuần gần nhất mới phản ánh đúng tình hình hiện tại.
    /// </para>
    /// <para>
    /// Công thức: EWMA(n) = α × giá_trị(n) + (1−α) × EWMA(n−1)
    /// </para>
    /// </summary>
    public async Task<double> ForecastDailyUsageAsync(Guid ingredientId, CancellationToken ct = default)
    {
        var since = VietnamTime.Now().Date.AddDays(-_opt.ForecastLookbackDays).ToString("yyyy-MM-dd");

        var history = await _db.DailyConsumptions
            .AsNoTracking()
            .Where(c => c.IngredientId == ingredientId
                     && string.Compare(c.BusinessDate, since) >= 0)
            .OrderBy(c => c.BusinessDate)
            .Select(c => new { c.BusinessDate, c.QuantityUsed })
            .ToListAsync(ct);

        // Chưa có lịch sử → không dự báo được. Trả 0 nghĩa là "coi như không
        // tiêu thụ", tức toàn bộ tồn kho bị tính là có nguy cơ. Bảo thủ nhưng
        // an toàn: thà cảnh báo thừa còn hơn để hàng hỏng.
        if (history.Count == 0) return 0;

        // Bù những ngày không bán được gì — nếu bỏ qua thì EWMA sẽ đánh giá
        // tiêu thụ cao hơn thực tế.
        var byDate = history.ToDictionary(h => h.BusinessDate, h => h.QuantityUsed);
        var series = new List<double>();
        var cursor = VietnamTime.Now().Date.AddDays(-_opt.ForecastLookbackDays);
        var today = VietnamTime.Now().Date;

        while (cursor < today)
        {
            var key = cursor.ToString("yyyy-MM-dd");
            series.Add(byDate.TryGetValue(key, out var v) ? v : 0);
            cursor = cursor.AddDays(1);
        }

        if (series.Count == 0) return 0;

        var alpha = _opt.ForecastEwmaAlpha;
        var ewma = series[0];
        for (int i = 1; i < series.Count; i++)
            ewma = alpha * series[i] + (1 - alpha) * ewma;

        return Math.Round(ewma, 4);
    }

    // ==========================================================================
    //  BƯỚC 2 & 3 — QUÉT LÔ, TÍNH RỦI RO, TÌM MÓN CHỞ HÀNG
    // ==========================================================================

    public async Task<List<LotRiskDto>> AnalyzeAsync(Guid storeId, CancellationToken ct = default)
    {
        var today = VietnamTime.Now().Date;
        var horizon = today.AddDays(_opt.WasteRiskHorizonDays);
        // Mốc đưa vào truy vấn phải là Kind=Utc, xem VietnamTime.CalendarDayUtc
        var horizonUtc = VietnamTime.CalendarDayUtc(horizon);

        var lots = await _db.InventoryLots
            .AsNoTracking()
            .Include(l => l.Ingredient)
            .Where(l => l.StoreId == storeId
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0
                     && l.ExpiryDate != null
                     && l.ExpiryDate <= horizonUtc)
            .OrderBy(l => l.ExpiryDate)
            .ToListAsync(ct);

        var risks = new List<LotRiskDto>();

        foreach (var lot in lots)
        {
            if (lot.Ingredient is null || lot.ExpiryDate is null) continue;

            var daysLeft = (int)(lot.ExpiryDate.Value.Date - today).TotalDays;
            var avgDaily = await ForecastDailyUsageAsync(lot.IngredientId, ct);

            // Dự kiến dùng hết bao nhiêu trước khi hết hạn.
            // Ngày âm (đã quá hạn) coi như 0 ngày còn lại.
            var projected = Math.Round(avgDaily * Math.Max(daysLeft, 0), 4);

            // ⭐ CON SỐ TRUNG TÂM CỦA CẢ TÍNH NĂNG
            var atRisk = Math.Max(0, Math.Round(lot.RemainingQuantity - projected, 4));

            // Dùng hết kịp trước hạn → không phải lo, bỏ qua
            if (atRisk <= 0.0001) continue;

            var valueAtRisk = (int)Math.Round(atRisk * lot.UnitCost);
            var severity = ClassifySeverity(daysLeft, valueAtRisk);

            risks.Add(new LotRiskDto(
                LotId:                lot.Id,
                LotCode:              lot.LotCode,
                IngredientId:         lot.IngredientId,
                IngredientName:       lot.Ingredient.Name,
                IngredientColorHex:   lot.Ingredient.ColorHex,
                IngredientIconKey:    lot.Ingredient.IconKey,
                UnitLabel:            UnitLabel(lot.Ingredient.BaseUnit),
                RemainingQuantity:    lot.RemainingQuantity,
                UnitCost:             lot.UnitCost,
                ExpiryDate:           lot.ExpiryDate.Value,
                DaysUntilExpiry:      daysLeft,
                AvgDailyUsage:        avgDaily,
                ProjectedUsage:       projected,
                QuantityAtRisk:       atRisk,
                ValueAtRisk:          valueAtRisk,
                Severity:             (int)severity,
                SeverityLabel:        SeverityLabel(severity),
                Carriers:             await FindCarrierProductsAsync(lot.IngredientId, storeId, ct)
            ));
        }

        // Sắp theo giá trị rủi ro giảm dần — việc tốn tiền nhất lên đầu
        return risks.OrderByDescending(r => r.ValueAtRisk).ToList();
    }

    /// <summary>
    /// Phân loại mức nghiêm trọng theo MA TRẬN hai chiều: thời gian còn lại
    /// và giá trị tiền.
    /// <para>
    /// Chỉ dùng số ngày là chưa đủ: 200g trân châu hết hạn ngày mai (9.000đ)
    /// không đáng lo bằng 5 lít sữa hết hạn sau 3 ngày (160.000đ).
    /// </para>
    /// </summary>
    private static RiskSeverity ClassifySeverity(int daysLeft, int valueAtRisk)
    {
        if (daysLeft <= 1) return RiskSeverity.Critical;
        if (daysLeft <= 3) return valueAtRisk >= 200_000 ? RiskSeverity.Critical : RiskSeverity.High;
        if (daysLeft <= 7) return valueAtRisk >= 500_000 ? RiskSeverity.High : RiskSeverity.Medium;
        return RiskSeverity.Low;
    }

    /// <summary>
    /// Tìm "món chở hàng" — món nào có thể tiêu thụ nguyên liệu đang cận hạn.
    /// <para>
    /// Xếp hạng theo: lượng tiêu thụ mỗi ly × số ly bán trung bình mỗi ngày.
    /// Món dùng nhiều nguyên liệu nhưng không ai mua thì vô dụng; món bán chạy
    /// nhưng chỉ dùng vài gram cũng không giải quyết được vấn đề.
    /// </para>
    /// </summary>
    private async Task<List<CarrierProductDto>> FindCarrierProductsAsync(
        Guid ingredientId, Guid storeId, CancellationToken ct)
    {
        var recipeItems = await _db.RecipeItems
            .AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.IngredientId == ingredientId
                     && r.Product!.IsActive
                     && r.Product.StoreId == storeId)
            .ToListAsync(ct);

        var carriers = new List<CarrierProductDto>();

        foreach (var ri in recipeItems)
        {
            if (ri.Product is null) continue;

            var avgUnits = await GetAvgDailyUnitsSoldAsync(ri.ProductId, ct);
            var margin = ri.Product.BasePrice - ri.Product.ComputedCost;

            carriers.Add(new CarrierProductDto(
                ProductId:          ri.ProductId,
                ProductName:        ri.Product.Name,
                ColorPrimaryHex:    ri.Product.ColorPrimaryHex,
                QuantityPerServing: ri.Quantity,
                Price:              ri.Product.BasePrice,
                Cost:               ri.Product.ComputedCost,
                MarginPercent:      ri.Product.BasePrice > 0
                                        ? (int)Math.Round(margin * 100.0 / ri.Product.BasePrice)
                                        : 0,
                AvgDailyUnits:      avgUnits));
        }

        return carriers
            .OrderByDescending(c => c.QuantityPerServing * Math.Max(c.AvgDailyUnits, 0.5))
            .ToList();
    }

    private async Task<double> GetAvgDailyUnitsSoldAsync(Guid productId, CancellationToken ct)
    {
        var since = VietnamTime.Now().Date.AddDays(-_opt.ForecastLookbackDays).ToString("yyyy-MM-dd");

        var rows = await _db.DailySales
            .AsNoTracking()
            .Where(s => s.ProductId == productId && string.Compare(s.BusinessDate, since) >= 0)
            .Select(s => s.UnitsSold)
            .ToListAsync(ct);

        return rows.Count == 0
            ? 0
            : Math.Round(rows.Sum() / (double)_opt.ForecastLookbackDays, 2);
    }

    public static string UnitLabel(BaseUnit u) => u switch
    {
        BaseUnit.Gram => "g",
        BaseUnit.Milliliter => "ml",
        BaseUnit.Piece => "cái",
        _ => ""
    };

    public static string SeverityLabel(RiskSeverity s) => s switch
    {
        RiskSeverity.Critical => "Khẩn cấp",
        RiskSeverity.High     => "Cao",
        RiskSeverity.Medium   => "Trung bình",
        _                     => "Thấp"
    };
}

// ==============================================================================
//  SINH PHƯƠNG ÁN KHUYẾN MÃI
// ==============================================================================

public interface IPromotionPlanner
{
    List<PromotionCandidate> Generate(LotRiskDto risk);
}

/// <summary>
/// Một phương án giảm giá đã tính đầy đủ lãi lỗ.
/// </summary>
public record PromotionCandidate(
    string CandidateId,
    Guid ProductId,
    string ProductName,
    string ProductColorHex,
    Guid IngredientId,
    string IngredientName,

    int DiscountPercent,
    /// <summary>Số ly cần bán THÊM để tiêu hết phần nguyên liệu có nguy cơ.</summary>
    int TargetUnits,
    /// <summary>Số ly vốn dĩ sẽ bán được nếu KHÔNG giảm giá.</summary>
    int BaselineUnits,
    /// <summary>Số ly tăng thêm nhờ giảm giá, theo mô hình co giãn.</summary>
    int ExpectedExtraUnits,

    /// <summary>Giá trị nguyên liệu cứu được (đồng).</summary>
    int WasteAvoided,
    /// <summary>Lãi mất trên số ly vốn dĩ đã bán được với giá gốc (đồng).</summary>
    int MarginGivenUp,
    /// <summary>Lãi thu thêm từ số ly bán thêm (đồng).</summary>
    int ExtraMargin,
    /// <summary>⭐ Lợi ích ròng = cứu được + lãi thêm − lãi mất.</summary>
    int NetBenefit,

    /// <summary>Có bán kịp hết phần nguy cơ trước hạn không.</summary>
    bool Feasible,
    int DaysAvailable,
    double QuantityAtRisk);

public class PromotionPlanner : IPromotionPlanner
{
    private readonly PlanningOptions _opt;

    /// <summary>Các mức giảm được thử. Bội số 5% cho dễ hiểu với khách.</summary>
    private static readonly int[] DiscountLevels = { 10, 15, 20, 25, 30 };

    /// <summary>Chỉ xét 3 món chở hàng tốt nhất — giảm giá nhiều món cùng lúc làm khách rối.</summary>
    private const int MaxCarriersPerIngredient = 3;

    public PromotionPlanner(IOptions<PlanningOptions> opt) => _opt = opt.Value;

    /// <summary>
    /// Sinh và chấm điểm các phương án giảm giá cho một lô cận hạn.
    /// </summary>
    public List<PromotionCandidate> Generate(LotRiskDto risk)
    {
        var candidates = new List<PromotionCandidate>();
        var days = Math.Max(risk.DaysUntilExpiry, 1);

        foreach (var carrier in risk.Carriers.Take(MaxCarriersPerIngredient))
        {
            if (carrier.QuantityPerServing <= 0) continue;

            // Cần bán thêm bao nhiêu ly để tiêu hết phần có nguy cơ
            var targetUnits = (int)Math.Ceiling(risk.QuantityAtRisk / carrier.QuantityPerServing);
            var baselineUnits = (int)Math.Round(carrier.AvgDailyUnits * days);

            foreach (var discount in DiscountLevels)
            {
                var newPrice = (int)Math.Round(carrier.Price * (100 - discount) / 100.0);
                var newMargin = newPrice - carrier.Cost;

                // ---- Chặn cứng: không bao giờ bán dưới ngưỡng lãi tối thiểu ----
                // Cứu nguyên liệu mà bán lỗ thì thà bỏ nguyên liệu còn hơn.
                if (newPrice <= 0) continue;
                var newMarginPercent = newMargin * 100.0 / newPrice;
                if (newMarginPercent < _opt.MinMarginPercent) continue;

                // ---- Mô hình co giãn giá ---------------------------------------
                //   Q₂/Q₁ = (P₂/P₁)^ε   với ε là độ co giãn (âm)
                var priceRatio = newPrice / (double)carrier.Price;
                var demandMultiplier = Math.Pow(priceRatio, _opt.DefaultPriceElasticity);
                var expectedTotal = (int)Math.Round(baselineUnits * demandMultiplier);
                var extraUnits = Math.Max(0, expectedTotal - baselineUnits);

                // ---- Ba thành phần của lợi ích ròng ----------------------------

                // (1) Nguyên liệu cứu được — không vượt quá lượng thực sự có nguy cơ
                var qtySaved = Math.Min(
                    risk.QuantityAtRisk,
                    extraUnits * carrier.QuantityPerServing);
                var wasteAvoided = (int)Math.Round(qtySaved * risk.UnitCost);

                // (2) Lãi MẤT trên số ly vốn dĩ đã bán được với giá gốc.
                //     ⚠️ Đây là chỗ hay bị quên nhất. Giảm giá món đang bán chạy
                //     có thể LỖ RÒNG dù vẫn cứu được nguyên liệu.
                var marginGivenUp = baselineUnits * (carrier.Price - newPrice);

                // (3) Lãi thu THÊM từ số ly bán thêm
                var extraMargin = extraUnits * newMargin;

                var netBenefit = wasteAvoided + extraMargin - marginGivenUp;

                candidates.Add(new PromotionCandidate(
                    CandidateId:        "", // gán sau khi đã chọn lọc
                    ProductId:          carrier.ProductId,
                    ProductName:        carrier.ProductName,
                    ProductColorHex:    carrier.ColorPrimaryHex,
                    IngredientId:       risk.IngredientId,
                    IngredientName:     risk.IngredientName,
                    DiscountPercent:    discount,
                    TargetUnits:        targetUnits,
                    BaselineUnits:      baselineUnits,
                    ExpectedExtraUnits: extraUnits,
                    WasteAvoided:       wasteAvoided,
                    MarginGivenUp:      marginGivenUp,
                    ExtraMargin:        extraMargin,
                    NetBenefit:         netBenefit,
                    Feasible:           extraUnits >= targetUnits,
                    DaysAvailable:      days,
                    QuantityAtRisk:     risk.QuantityAtRisk));
            }
        }

        // ---- Lọc: chỉ giữ phương án CÓ LỢI, mỗi món lấy phương án tốt nhất ----
        return candidates
            .Where(c => c.NetBenefit > 0)
            .GroupBy(c => c.ProductId)
            .Select(g => g.OrderByDescending(c => c.NetBenefit).First())
            .OrderByDescending(c => c.NetBenefit)
            .ToList();
    }
}
