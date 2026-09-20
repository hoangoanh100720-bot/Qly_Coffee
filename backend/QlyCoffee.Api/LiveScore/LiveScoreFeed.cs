using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.LiveScore;

// ==============================================================================
//  BẢNG TỈ SỐ TRỰC TIẾP
//
//    football-data.org ──(job nền hỏi định kỳ)──► LiveScoreStore
//                                                    │ chỉ khi có thay đổi
//                                                    ▼
//                                LiveScoreHub ──(SignalR)──► mọi trình duyệt
//
//  Vì sao máy chủ hỏi thay cho trình duyệt:
//    - Mười màn hình mở cùng lúc vẫn chỉ tốn MỘT lượt gọi nguồn.
//    - Khóa API không bao giờ ra tới trình duyệt.
//
//  Giống ScheduledJobsService: một quán một máy chủ thì chạy trong ứng dụng là
//  đủ. Lên nhiều máy chủ thì máy nào cũng tự hỏi nguồn (tốn lượt gấp N), và
//  SignalR cần Redis backplane để tin tới được client đang nối vào máy khác.
// ==============================================================================

/// <summary>
/// Hub chỉ phát MỘT CHIỀU từ máy chủ xuống, client không gọi gì lên nên lớp này
/// để trống. Việc gửi làm qua <see cref="IHubContext{THub}"/> trong job nền.
/// <para>
/// Không đòi đăng nhập: tỉ số là thông tin công khai, và sau này muốn đưa lên
/// trang khách hay TV quầy thì không phải cấp tài khoản cho màn hình đó.
/// </para>
/// </summary>
public class LiveScoreHub : Hub
{
}

/// <summary>
/// Giữ bảng tỉ số mới nhất trong bộ nhớ. Endpoint REST đọc từ đây và KHÔNG BAO
/// GIỜ gọi thẳng nguồn — nên người lạ gọi api/live-score liên tục cũng không đốt
/// được lượt gọi của quán.
/// </summary>
public class LiveScoreStore
{
    private readonly object _gate = new();
    private LiveScoreBoardDto _board;

    public LiveScoreStore(IOptions<LiveScoreOptions> opt)
    {
        _board = new LiveScoreBoardDto(opt.Value.Enabled, null, Array.Empty<LiveMatchDto>());
    }

    public LiveScoreBoardDto Current
    {
        get { lock (_gate) return _board; }
    }

    /// <summary>
    /// Thay danh sách trận. Trả về bảng mới nếu CÓ thay đổi, null nếu y như cũ —
    /// người gọi chỉ đẩy xuống client khi khác null.
    /// </summary>
    public LiveScoreBoardDto? Replace(IReadOnlyList<LiveMatchDto> matches)
    {
        lock (_gate)
        {
            // LiveMatchDto là record nên SequenceEqual so theo GIÁ TRỊ từng trường:
            // tỉ số, phút hay trạng thái nhích một chút là khác ngay.
            if (_board.UpdatedAtUtc is not null && _board.Notice is null &&
                _board.Matches.SequenceEqual(matches))
                return null;

            _board = _board with { Matches = matches, UpdatedAtUtc = DateTime.UtcNow, Notice = null };
            return _board;
        }
    }

    /// <summary>
    /// Nguồn lỗi: GIỮ NGUYÊN tỉ số cũ, chỉ gắn thêm lời nhắn. Một lần mất mạng mà
    /// bảng trống trơn thì nhân viên tưởng trận đã hết.
    /// </summary>
    public LiveScoreBoardDto? SetNotice(string notice)
    {
        lock (_gate)
        {
            if (_board.Notice == notice) return null;
            _board = _board with { Notice = notice };
            return _board;
        }
    }
}

/// <summary>
/// Job nền hỏi nguồn tỉ số với nhịp ĐỔI THEO TÌNH HÌNH: có trận đang đá hoặc
/// sắp tới giờ bóng lăn → 30 giây; không có gì → 10 phút. Cả ngày chỉ tốn vài
/// trăm lượt gọi.
/// </summary>
public class LiveScorePoller : BackgroundService
{
    /// <summary>Trận đã kết thúc còn nằm trên bảng bao lâu, tính từ giờ bóng lăn.</summary>
    private static readonly TimeSpan FinishedVisibleFor = TimeSpan.FromHours(4);

    /// <summary>Chỉ hiện trận sắp đá trong chừng này thời gian tới.</summary>
    private static readonly TimeSpan UpcomingHorizon = TimeSpan.FromHours(24);

    /// <summary>Chuyển sang nhịp nhanh trước giờ bóng lăn chừng này.</summary>
    private static readonly TimeSpan WarmUp = TimeSpan.FromMinutes(5);

    private readonly FootballDataClient _api;
    private readonly LiveScoreStore _store;
    private readonly IHubContext<LiveScoreHub> _hub;
    private readonly LiveScoreOptions _opt;
    private readonly ILogger<LiveScorePoller> _logger;

    public LiveScorePoller(
        FootballDataClient api,
        LiveScoreStore store,
        IHubContext<LiveScoreHub> hub,
        IOptions<LiveScoreOptions> opt,
        ILogger<LiveScorePoller> logger)
    {
        _api = api;
        _store = store;
        _hub = hub;
        _opt = opt.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _logger.LogInformation("Bảng tỉ số bóng đá đang tắt — chưa đặt FOOTBALL_API_KEY trong .env");
            return;
        }

        _logger.LogInformation("Bảng tỉ số bóng đá đã khởi động");

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var now = DateTime.UtcNow;
                var board = SelectForBoard(await _api.GetMatchesAsync(stoppingToken), now);

                await PublishAsync(_store.Replace(board), stoppingToken);
                wait = NextPollDelay(board, now);
            }
            catch (FootballApiException ex)
            {
                _logger.LogWarning("Nguồn tỉ số: {Reason}. Hỏi lại sau {Wait}", ex.Message, ex.RetryAfter);
                await PublishAsync(_store.SetNotice(ex.Notice), stoppingToken);
                wait = ex.RetryAfter;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Mất mạng, nguồn sập, JSON đổi dạng… Lỗi ở đây KHÔNG được làm sập
                // ứng dụng — quán vẫn phải bán được hàng.
                _logger.LogError(ex, "Không lấy được tỉ số, thử lại sau 1 phút");
                await PublishAsync(_store.SetNotice("Mất kết nối nguồn tỉ số — đang thử lại…"), stoppingToken);
                wait = TimeSpan.FromMinutes(1);
            }

            await Task.Delay(wait, stoppingToken);
        }
    }

    /// <summary>Không có gì mới thì im lặng — không đánh thức client vô cớ.</summary>
    private async Task PublishAsync(LiveScoreBoardDto? board, CancellationToken ct)
    {
        if (board is null) return;
        await _hub.Clients.All.SendAsync(LiveScoreChannel.BoardUpdated, board, ct);
    }

    /// <summary>
    /// Lọc và xếp: đang đá lên đầu, rồi sắp đá (sớm trước), rồi vừa kết thúc
    /// (mới trước). Trận đá xong từ hôm qua hay tuần sau không liên quan tới
    /// khách đang ngồi trong quán.
    /// </summary>
    private static IReadOnlyList<LiveMatchDto> SelectForBoard(IEnumerable<LiveMatchDto> all, DateTime nowUtc)
        => all
            .Where(m => m.Phase switch
            {
                MatchPhase.Live or MatchPhase.Break => true,
                MatchPhase.Finished => m.KickoffUtc >= nowUtc - FinishedVisibleFor,
                // Lùi 3 tiếng để giữ cả trận đã quá giờ bóng lăn mà nguồn chưa kịp
                // báo "đang đá", và trận vừa bị hoãn sát giờ.
                _ => m.KickoffUtc >= nowUtc - TimeSpan.FromHours(3) &&
                     m.KickoffUtc <= nowUtc + UpcomingHorizon
            })
            .OrderBy(m => Rank(m.Phase))
            .ThenBy(m => m.Phase == MatchPhase.Finished ? -m.KickoffUtc.Ticks : m.KickoffUtc.Ticks)
            .ThenBy(m => m.Competition)
            .ToList();

    private static int Rank(string phase) => phase switch
    {
        MatchPhase.Live or MatchPhase.Break => 0,
        MatchPhase.Finished => 2,
        _ => 1
    };

    private TimeSpan NextPollDelay(IReadOnlyList<LiveMatchDto> board, DateTime nowUtc)
    {
        // Sàn 10 giây: gói miễn phí chỉ cho 10 lượt/phút, đặt nhỏ hơn là tự khóa mình
        var fast = TimeSpan.FromSeconds(Math.Max(10, _opt.LivePollSeconds));
        var idle = TimeSpan.FromMinutes(Math.Max(1, _opt.IdlePollMinutes));

        if (board.Any(m => m.Phase is MatchPhase.Live or MatchPhase.Break)) return fast;

        var nextKickoff = board
            .Where(m => m.Phase == MatchPhase.Upcoming)
            .Select(m => (DateTime?)m.KickoffUtc)
            .Min();
        if (nextKickoff is null) return idle;

        // Ngủ tới sát giờ bóng lăn rồi mới chuyển nhịp nhanh, nhưng không ngủ quá
        // chu kỳ nghỉ — lịch thi đấu có thể đổi trong lúc đó.
        var untilWarmUp = nextKickoff.Value - WarmUp - nowUtc;
        if (untilWarmUp <= TimeSpan.Zero) return fast;
        return untilWarmUp < idle ? untilWarmUp : idle;
    }
}
