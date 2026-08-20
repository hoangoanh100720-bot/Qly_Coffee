using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Api.Ai;

// ==============================================================================
//  LỚP 3 — GỌI CLAUDE VIẾT DIỄN GIẢI CHO BẢN KẾ HOẠCH
//
//  ⚠️ NGUYÊN TẮC SỐ MỘT: AI KHÔNG BAO GIỜ TÍNH TOÁN
//
//  Mọi con số tiền và số lượng đã được backend tính xong ở WasteRiskService và
//  PromotionPlanner. Claude chỉ nhận chúng như dữ kiện và viết thành câu chữ.
//
//  Vì sao nghiêm ngặt như vậy: mô hình ngôn ngữ viết rất trôi chảy và thuyết
//  phục ngay cả khi tính sai. Một báo cáo tài chính sai mà đọc vào thấy hợp lý
//  là thứ nguy hiểm nhất — không ai phát hiện ra cho tới khi kiểm kê.
//
//  BA LỚP BẢO VỆ:
//    1. Prompt hệ thống cấm tuyệt đối việc tính lại số
//    2. Ràng buộc JSON schema — AI chỉ được điền trường chữ, không có trường số
//    3. Lọc sau khi nhận kết quả — bỏ mọi candidateId không tồn tại
// ==============================================================================

public class ClaudeOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5";
    public int MaxTokens { get; set; } = 8000;
    /// <summary>Tắt thì hệ thống dùng bản diễn giải mẫu, vẫn có đủ số liệu.</summary>
    public bool Enabled { get; set; } = true;
}

public class ClaudeNarrator : IAiNarrator
{
    private readonly ClaudeOptions _opt;
    private readonly ILogger<ClaudeNarrator> _logger;

    public ClaudeNarrator(IOptions<ClaudeOptions> opt, ILogger<ClaudeNarrator> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    // ==========================================================================
    //  PROMPT HỆ THỐNG
    // ==========================================================================

    private const string SystemPrompt = """
        Bạn là trợ lý vận hành của một quán cà phê tại Việt Nam. Cuối mỗi ngày bạn
        nhận số liệu phân tích tồn kho ĐÃ ĐƯỢC HỆ THỐNG TÍNH SẴN, và viết một bản
        kế hoạch ngắn gọn cho chủ quán đọc.

        ## Nhiệm vụ
        1. Viết một câu headline nêu điều quan trọng nhất cần xử lý hôm nay.
        2. Viết đoạn tóm tắt 3–5 câu bằng tiếng Việt tự nhiên, giọng như một quản lý
           ca báo cáo cuối ngày: trực tiếp, cụ thể, không màu mè.
        3. Với mỗi phương án được cung cấp, viết tiêu đề ngắn và lý do giải thích
           VÌ SAO nên làm, dựa trên chính các con số được đưa.
        4. Với phương án giảm giá, viết thêm một dòng banner dưới 80 ký tự cho trang
           bán hàng — viết cho khách trẻ, tự nhiên, không sáo rỗng.

        ## Ràng buộc tuyệt đối
        - KHÔNG tự tính toán lại bất kỳ con số nào. Mọi số liệu đã được tính sẵn và
          chính xác. Bạn chỉ trích dẫn lại chúng.
        - KHÔNG đề xuất phương án không có trong danh sách được cung cấp.
        - KHÔNG bịa candidateId. Chỉ dùng đúng những id được đưa.
        - KHÔNG hứa với khách những điều quán không kiểm soát được.
        - Nếu số liệu cho thấy không có việc gì gấp, hãy nói thẳng là hôm nay ổn.
          Không cần bịa ra việc để đề xuất.

        ## Xếp mức ưu tiên
        - critical: hết hạn trong 1–2 ngày, hoặc giá trị rủi ro trên 500.000đ
        - high:     hết hạn trong 3–4 ngày
        - medium:   hết hạn trong 5–7 ngày
        - low:      còn nhiều thời gian

        ## Giọng văn
        Ngắn gọn, cụ thể, không dùng từ hoa mỹ. Nêu con số khi nó giúp người đọc
        ra quyết định. Điều quan trọng nhất viết trước.
        """;

    // ==========================================================================
    //  GỌI API
    // ==========================================================================

    public async Task<AiPlanNarrative> WriteAsync(
        PlanNarrativeInput input, CancellationToken ct = default)
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            _logger.LogInformation("AI đang tắt (AI_ENABLED=false hoặc thiếu khóa) — dùng bản mẫu");
            throw new InvalidOperationException("AI đang tắt trong cấu hình");
        }

        var client = new AnthropicClient { ApiKey = _opt.ApiKey };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model     = _opt.Model,
            MaxTokens = _opt.MaxTokens,

            // Adaptive thinking: Claude tự quyết định cần suy nghĩ bao nhiêu.
            // Đây là chế độ khuyến nghị cho các model 4.6 trở lên.
            Thinking = new ThinkingConfigAdaptive(),

            OutputConfig = new OutputConfig
            {
                // medium là điểm cân bằng tốt cho tác vụ viết có cấu trúc.
                // Tăng lên high nếu thấy chất lượng diễn giải chưa đạt.
                Effort = Effort.Medium,

                // Ràng buộc đầu ra theo schema — mô hình KHÔNG THỂ trả về
                // trường số nào vì schema không định nghĩa trường số nào cả.
                Format = new JsonOutputFormat { Schema = BuildResponseSchema() }
            },

            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = SystemPrompt,
                    // Prompt hệ thống không đổi giữa các ngày → cache lại,
                    // tiết kiệm khoảng 90% chi phí phần này.
                    CacheControl = new CacheControlEphemeral()
                }
            },

            Messages = [new() { Role = Role.User, Content = BuildUserPrompt(input) }]
        });

        // Đọc khối văn bản đầu tiên. Với structured output thì nội dung là JSON hợp lệ.
        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Claude không trả về nội dung");

        var parsed = JsonSerializer.Deserialize<AiResponseDto>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Không đọc được JSON từ Claude");

        var tokensUsed = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        _logger.LogInformation(
            "Claude đã viết kế hoạch: {N} đề xuất, {Tokens} token",
            parsed.Suggestions?.Count ?? 0, tokensUsed);

        return new AiPlanNarrative(
            Headline: parsed.Headline ?? "",
            Summary: parsed.Summary ?? "",
            Suggestions: (parsed.Suggestions ?? new()).Select(s => new AiSuggestionText(
                CandidateId: s.CandidateId ?? "",
                Priority: ParsePriority(s.Priority),
                Title: s.Title ?? "",
                Reasoning: s.Reasoning ?? "",
                BannerCopy: s.BannerCopy)).ToList(),
            ModelUsed: _opt.Model,
            TokensUsed: tokensUsed);
    }

    // ==========================================================================
    //  DỰNG PROMPT NGƯỜI DÙNG
    // ==========================================================================

    /// <summary>
    /// Trình bày số liệu dưới dạng Markdown có cấu trúc rõ ràng.
    /// Mỗi con số đều ghi kèm đơn vị và ý nghĩa để mô hình không hiểu nhầm.
    /// </summary>
    private static string BuildUserPrompt(PlanNarrativeInput input)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Ngày kinh doanh: {input.BusinessDate}");
        sb.AppendLine();
        sb.AppendLine("## Kết quả hôm nay");
        sb.AppendLine($"- Doanh thu: {input.Revenue:N0}đ");
        sb.AppendLine($"- Số đơn: {input.OrderCount}");
        sb.AppendLine($"- Hao hụt đã ghi nhận: {input.WasteValue:N0}đ");
        sb.AppendLine();

        // ---- Rủi ro hết hạn -------------------------------------------------
        if (input.Risks.Count == 0)
        {
            sb.AppendLine("## Nguyên liệu có nguy cơ bỏ đi");
            sb.AppendLine("Không có. Mọi lô hàng đều sẽ dùng hết trước hạn với tốc độ bán hiện tại.");
        }
        else
        {
            sb.AppendLine("## Nguyên liệu có nguy cơ bỏ đi");
            foreach (var r in input.Risks)
            {
                sb.AppendLine();
                sb.AppendLine($"### {r.IngredientName} — lô {r.LotCode}");
                sb.AppendLine($"- Còn lại: {r.RemainingQuantity:0.##} {r.UnitLabel}");
                sb.AppendLine($"- Hạn dùng: {r.ExpiryDate:dd/MM/yyyy} (còn {r.DaysUntilExpiry} ngày)");
                sb.AppendLine($"- Tiêu thụ trung bình: {r.AvgDailyUsage:0.##} {r.UnitLabel}/ngày");
                sb.AppendLine($"- Dự kiến dùng được trước hạn: {r.ProjectedUsage:0.##} {r.UnitLabel}");
                sb.AppendLine($"- **Nguy cơ phải bỏ: {r.QuantityAtRisk:0.##} {r.UnitLabel} ≈ {r.ValueAtRisk:N0}đ**");
                sb.AppendLine($"- Mức độ: {r.SeverityLabel}");
            }
        }
        sb.AppendLine();

        // ---- Phương án giảm giá ---------------------------------------------
        if (input.Candidates.Count > 0)
        {
            sb.AppendLine("## Phương án khuyến mãi hệ thống đã tính");
            sb.AppendLine();
            sb.AppendLine("Các con số dưới đây đã được tính chính xác. Chỉ trích dẫn, không tính lại.");

            foreach (var c in input.Candidates)
            {
                sb.AppendLine();
                sb.AppendLine($"### candidateId: {c.CandidateId}");
                sb.AppendLine($"- Món đem giảm giá: {c.ProductName}");
                sb.AppendLine($"- Nguyên liệu cần cứu: {c.IngredientName}");
                sb.AppendLine($"- Mức giảm đề xuất: {c.DiscountPercent}%");
                sb.AppendLine($"- Thời gian áp dụng: {c.DaysAvailable} ngày");
                sb.AppendLine($"- Bán trung bình hiện tại: {c.BaselineUnits} ly trong {c.DaysAvailable} ngày");
                sb.AppendLine($"- Cần bán THÊM: {c.TargetUnits} ly để tiêu hết phần nguy cơ");
                sb.AppendLine($"- Dự kiến bán thêm được: {c.ExpectedExtraUnits} ly");
                sb.AppendLine($"- Khả thi: {(c.Feasible ? "CÓ — đủ tiêu hết trước hạn" : "KHÔNG — không kịp bán hết, nhưng vẫn giảm được thiệt hại")}");
                sb.AppendLine($"- Nguyên liệu cứu được: {c.WasteAvoided:N0}đ");
                sb.AppendLine($"- Lãi mất trên phần vốn đã bán được: {c.MarginGivenUp:N0}đ");
                sb.AppendLine($"- Lãi thêm từ ly bán thêm: {c.ExtraMargin:N0}đ");
                sb.AppendLine($"- **Lợi ích ròng: {c.NetBenefit:N0}đ**");
            }
        }
        sb.AppendLine();

        // ---- Đề xuất khác ----------------------------------------------------
        if (input.OtherSuggestions.Count > 0)
        {
            sb.AppendLine("## Đề xuất khác (không phải giảm giá)");
            foreach (var (id, type, detail) in input.OtherSuggestions)
            {
                sb.AppendLine();
                sb.AppendLine($"### candidateId: {id}");
                sb.AppendLine($"- Loại: {DailyPlanService.TypeLabel(type)}");
                sb.AppendLine($"- Chi tiết: {detail}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("Hãy trả về JSON đúng schema đã quy định. Viết cho MỌI candidateId ở trên.");

        return sb.ToString();
    }

    // ==========================================================================
    //  SCHEMA ĐẦU RA
    // ==========================================================================

    /// <summary>
    /// Schema chỉ có TRƯỜNG CHỮ. Không có trường số nào — đây là cách chặn ở
    /// tầng giao thức việc AI sinh ra con số của riêng nó.
    /// </summary>
    private static Dictionary<string, JsonElement> BuildResponseSchema()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "headline": {
              "type": "string",
              "description": "Một câu nêu điều quan trọng nhất, tối đa 120 ký tự"
            },
            "summary": {
              "type": "string",
              "description": "Đoạn tóm tắt 3-5 câu, tối đa 1200 ký tự"
            },
            "suggestions": {
              "type": "array",
              "maxItems": 8,
              "items": {
                "type": "object",
                "properties": {
                  "candidateId": {
                    "type": "string",
                    "description": "PHẢI khớp chính xác một candidateId được cung cấp"
                  },
                  "priority": {
                    "type": "string",
                    "enum": ["critical", "high", "medium", "low"]
                  },
                  "title": {
                    "type": "string",
                    "description": "Tiêu đề ngắn gọn, tối đa 120 ký tự"
                  },
                  "reasoning": {
                    "type": "string",
                    "description": "Lý do nên làm, trích dẫn các con số đã cho, tối đa 600 ký tự"
                  },
                  "bannerCopy": {
                    "type": ["string", "null"],
                    "description": "Dòng banner cho khách, dưới 80 ký tự. null nếu không phải giảm giá"
                  }
                },
                "required": ["candidateId", "priority", "title", "reasoning", "bannerCopy"],
                "additionalProperties": false
              }
            }
          },
          "required": ["headline", "summary", "suggestions"],
          "additionalProperties": false
        }
        """;

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private static SuggestionPriority ParsePriority(string? value) => value?.ToLowerInvariant() switch
    {
        "critical" => SuggestionPriority.Critical,
        "high"     => SuggestionPriority.High,
        "medium"   => SuggestionPriority.Medium,
        _          => SuggestionPriority.Low
    };

    // Kiểu trung gian để đọc JSON từ Claude
    private class AiResponseDto
    {
        public string? Headline { get; set; }
        public string? Summary { get; set; }
        public List<AiSuggestionDto>? Suggestions { get; set; }
    }

    private class AiSuggestionDto
    {
        public string? CandidateId { get; set; }
        public string? Priority { get; set; }
        public string? Title { get; set; }
        public string? Reasoning { get; set; }
        public string? BannerCopy { get; set; }
    }
}
