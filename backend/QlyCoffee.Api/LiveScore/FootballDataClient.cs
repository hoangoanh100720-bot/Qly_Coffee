using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.LiveScore;

// ==============================================================================
//  NGUỒN TỈ SỐ BÓNG ĐÁ — football-data.org (API v4)
//
//  Vì sao chọn nguồn này:
//    - Gói miễn phí KHÔNG hết hạn, có đủ các giải khách hay xem: Ngoại hạng Anh,
//      C1, La Liga, Serie A, Bundesliga, Ligue 1, Euro, World Cup… Đăng ký chỉ
//      cần email.
//    - Giới hạn 10 lượt/phút. Máy chủ hỏi 30 giây một lần khi có trận là
//      2 lượt/phút, còn dư rất nhiều.
//    - Muốn đổi nguồn (API-Football…) chỉ phải viết lại lớp này: phần còn lại
//      của hệ thống chỉ biết tới LiveMatchDto.
// ==============================================================================

/// <summary>Cấu hình đọc từ nhóm biến FOOTBALL_* trong .env.</summary>
public class LiveScoreOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Mã giải cách nhau dấu phẩy, VD "PL,CL". Trống = mọi giải trong gói.</summary>
    public string Competitions { get; set; } = "";

    /// <summary>Chu kỳ hỏi khi có trận đang đá, tính bằng giây.</summary>
    public int LivePollSeconds { get; set; } = 30;

    /// <summary>Chu kỳ hỏi khi không có trận nào đang đá, tính bằng phút.</summary>
    public int IdlePollMinutes { get; set; } = 10;

    /// <summary>Chưa có khóa thì tắt hẳn — không gọi ra ngoài lần nào.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Nguồn từ chối có kiểm soát (hết lượt, sai khóa). Mang theo thời gian nên chờ
/// trước khi hỏi lại, và câu nhắn hiện cho người xem.
/// </summary>
public class FootballApiException : Exception
{
    public TimeSpan RetryAfter { get; }
    public string Notice { get; }

    public FootballApiException(string message, string notice, TimeSpan retryAfter) : base(message)
    {
        Notice = notice;
        RetryAfter = retryAfter;
    }
}

public class FootballDataClient
{
    public const string HttpClientName = "football-data";
    private const string BaseUrl = "https://api.football-data.org/v4/";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _factory;
    private readonly LiveScoreOptions _opt;

    public FootballDataClient(IHttpClientFactory factory, IOptions<LiveScoreOptions> opt)
    {
        _factory = factory;
        _opt = opt.Value;
    }

    /// <summary>
    /// Mọi trận từ hôm qua tới ngày kia, tính theo ngày UTC.
    /// <para>
    /// Lấy rộng hơn "hôm nay" vì Việt Nam lệch UTC 7 tiếng: trận 2 giờ sáng ở
    /// quán là 19 giờ hôm trước theo UTC. Lọc lại theo giờ bóng lăn là việc của
    /// <see cref="LiveScorePoller"/>.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<LiveMatchDto>> GetMatchesAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var url = $"{BaseUrl}matches" +
                  $"?dateFrom={today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                  $"&dateTo={today.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        var competitions = _opt.Competitions.Replace(" ", "");
        if (competitions.Length > 0)
            url += "&competitions=" + Uri.EscapeDataString(competitions);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Auth-Token", _opt.ApiKey);

        using var res = await _factory.CreateClient(HttpClientName).SendAsync(req, ct);

        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new FootballApiException(
                "Hết lượt gọi trong phút này",
                "Nguồn tỉ số tạm hết lượt — sẽ tự cập nhật lại sau ít phút.",
                RetryAfter(res));

        // Sai khóa, hoặc FOOTBALL_COMPETITIONS có giải nằm ngoài gói. Hỏi lại liên
        // tục cũng không khá hơn — chờ lâu để khỏi xả đầy log.
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            throw new FootballApiException(
                $"Bị từ chối ({(int)res.StatusCode}) — kiểm tra FOOTBALL_API_KEY và FOOTBALL_COMPETITIONS",
                "Nguồn tỉ số từ chối yêu cầu. Kiểm tra FOOTBALL_API_KEY và FOOTBALL_COMPETITIONS trong .env.",
                TimeSpan.FromMinutes(30));

        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<MatchesResponse>(JsonOpts, ct);
        return body?.Matches?.Select(Map).ToList() ?? new List<LiveMatchDto>();
    }

    /// <summary>
    /// football-data.org báo số giây tới lúc được gọi tiếp trong header riêng
    /// X-RequestCounter-Reset. Thiếu header thì chờ trọn một phút cho chắc.
    /// </summary>
    private static TimeSpan RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.TryGetValues("X-RequestCounter-Reset", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds + 1);

        return res.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
    }

    private static LiveMatchDto Map(ApiMatch m)
    {
        var (phase, label) = Classify(m.Status);

        var s = m.Score;
        var goals = s?.FullTime;
        var hadShootout = s?.Penalties is { Home: not null, Away: not null };

        // Trận phân định bằng luân lưu: fullTime có thể đã cộng cả bàn luân lưu
        // (5–4 thay vì 1–1). Dựng lại tỉ số thật từ hiệp chính + hiệp phụ khi có.
        if (hadShootout && s!.RegularTime is { } rt)
            goals = new ApiGoals(rt.Home + (s.ExtraTime?.Home ?? 0), rt.Away + (s.ExtraTime?.Away ?? 0));

        if (hadShootout && phase == MatchPhase.Finished)
            label = $"Kết thúc · luân lưu {s!.Penalties!.Home}–{s.Penalties.Away}";

        return new LiveMatchDto(
            Id:                m.Id,
            Competition:       m.Competition?.Name ?? "",
            CompetitionEmblem: m.Competition?.Emblem,
            HomeTeam:          TeamName(m.HomeTeam),
            HomeCrest:         m.HomeTeam?.Crest,
            AwayTeam:          TeamName(m.AwayTeam),
            AwayCrest:         m.AwayTeam?.Crest,
            HomeScore:         goals?.Home,
            AwayScore:         goals?.Away,
            Phase:             phase,
            StatusLabel:       label,
            Minute:            ReadInt(m.Minute),
            InjuryTime:        ReadInt(m.InjuryTime),
            KickoffUtc:        m.UtcDate.ToUniversalTime());
    }

    private static (string Phase, string Label) Classify(string? status) => status switch
    {
        "IN_PLAY"          => (MatchPhase.Live,     "Đang đá"),
        "EXTRA_TIME"       => (MatchPhase.Live,     "Hiệp phụ"),
        "PENALTY_SHOOTOUT" => (MatchPhase.Live,     "Luân lưu"),
        "PAUSED"           => (MatchPhase.Break,    "Nghỉ giữa hiệp"),
        "FINISHED"         => (MatchPhase.Finished, "Kết thúc"),
        "AWARDED"          => (MatchPhase.Finished, "Xử thắng"),
        "POSTPONED"        => (MatchPhase.Off,      "Hoãn"),
        "SUSPENDED"        => (MatchPhase.Off,      "Tạm dừng"),
        "CANCELLED"        => (MatchPhase.Off,      "Hủy"),
        _                  => (MatchPhase.Upcoming, "Sắp đá")   // SCHEDULED, TIMED
    };

    /// <summary>Tên ngắn cho vừa một dòng: "Man United" thay vì "Manchester United FC".</summary>
    private static string TeamName(ApiTeam? t)
        => new[] { t?.ShortName, t?.Name, t?.Tla }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
           ?? "Chưa xác định";   // vòng loại trực tiếp chưa rõ đối thủ

    /// <summary>
    /// Đọc số nguyên từ một trường có thể thiếu, null hoặc mang kiểu lạ. Trường
    /// phút không có ở mọi gói dịch vụ, mà dữ liệu từ NGOÀI vào thì không được
    /// làm hỏng cả lượt đọc chỉ vì một trường phụ.
    /// </summary>
    private static int? ReadInt(JsonElement? value)
    {
        if (value is not { } el) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null
        };
    }

    // ---- Hình dạng JSON của football-data.org v4 — chỉ khai báo trường cần dùng ----

    private sealed record MatchesResponse(List<ApiMatch>? Matches);

    private sealed record ApiMatch(
        long Id,
        DateTime UtcDate,
        string? Status,
        ApiCompetition? Competition,
        ApiTeam? HomeTeam,
        ApiTeam? AwayTeam,
        ApiScore? Score,
        JsonElement? Minute,
        JsonElement? InjuryTime);

    private sealed record ApiCompetition(string? Name, string? Emblem);

    private sealed record ApiTeam(string? Name, string? ShortName, string? Tla, string? Crest);

    private sealed record ApiScore(
        ApiGoals? FullTime,
        ApiGoals? RegularTime,
        ApiGoals? ExtraTime,
        ApiGoals? Penalties);

    private sealed record ApiGoals(int? Home, int? Away);
}
