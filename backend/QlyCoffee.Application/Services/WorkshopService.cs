using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DỊCH VỤ WORKSHOP — LỊCH, GIÁ VÀ GIỮ CHỖ
//
//  BA QUY TẮC KHÔNG ĐƯỢC PHÁ
//
//  1. GIÁ LUÔN TÍNH Ở ĐÂY, KHÔNG BAO GIỜ NHẬN TỪ TRÌNH DUYỆT.
//     Giao diện có tính lại để hiện số tiền ngay khi khách bấm, nhưng đó chỉ là
//     bản xem trước. Nhận số tiền client gửi lên là để bất kỳ ai cũng đặt được
//     buổi học giá 0 đồng bằng công cụ phát triển của trình duyệt.
//
//  2. ĐẾM CHỖ CHỈ CÓ MỘT ĐỊNH NGHĨA: WorkshopBooking.HoldsSeat.
//     Mọi phép đếm đi qua đó. Viết lại điều kiện "status != 3" rải rác trong
//     truy vấn là cách chắc chắn để nơi này coi NoShow là trống còn nơi kia coi
//     là đầy.
//
//  3. KIỂM TRA CHỖ TRỐNG NẰM TRONG GIAO DỊCH, VÀ KIỂM TRA LẠI SAU KHI GHI.
//     Hai khách bấm đặt cùng một giây đều đọc thấy "còn 1 chỗ". Kiểm tra trước
//     khi ghi là chưa đủ — xem CreateBookingAsync để biết vì sao phải đọc lại
//     một lần nữa sau khi đã ghi.
//
//  GIỜ GIẤC
//  Buổi học gắn với ngày giờ ĐỊA PHƯƠNG của quán, không phải mốc UTC. Cả file
//  này dùng NowLocal() thay cho DateTime.Now để múi giờ máy chủ (thường là UTC
//  khi chạy trên hạ tầng thuê) không đẩy buổi sáng thứ Bảy sang thứ Sáu.
// ==============================================================================

public interface IWorkshopService
{
    Task<WorkshopCalendarDto> GetCalendarAsync(
        Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<WorkshopBookingDto> CreateBookingAsync(
        Guid storeId, CreateWorkshopBookingRequest req, CancellationToken ct = default);

    Task<WorkshopBookingDto?> GetBookingAsync(
        Guid storeId, string code, string phone, CancellationToken ct = default);

    Task<WorkshopBookingDto> CancelBookingAsync(
        Guid storeId, string code, string phone, string? reason, CancellationToken ct = default);

    Task<IReadOnlyList<WorkshopBookingDto>> GetBookingsForAdminAsync(
        Guid storeId, DateOnly? from, DateOnly? to, int? status, CancellationToken ct = default);

    Task<WorkshopBookingDto> UpdateBookingStatusAsync(
        Guid storeId, Guid bookingId, int status, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Sửa giá, sức chứa và phần mô tả của một buổi — việc của chủ quán.
    /// Có thể áp cùng lúc cho mọi buổi tương lai trùng thứ và trùng giờ.
    /// </summary>
    Task<WorkshopSessionSavedDto> UpdateSessionAsync(
        Guid storeId, Guid sessionId, UpdateWorkshopSessionRequest req, CancellationToken ct = default);
}

public class WorkshopService : IWorkshopService
{
    private readonly AppDbContext _db;

    public WorkshopService(AppDbContext db) => _db = db;

    /// <summary>
    /// Múi giờ quán. Tên IANA ("Asia/Ho_Chi_Minh") chạy được trên Linux; Windows
    /// dùng tên riêng nên thử cả hai rồi mới chịu thua về UTC+7 cố định.
    /// </summary>
    private static readonly TimeZoneInfo StoreTz = ResolveStoreTimeZone();

    private static TimeZoneInfo ResolveStoreTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Việt Nam không có giờ mùa hè nên một khoảng lệch cố định là đúng,
        // không phải giải pháp tạm.
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "Giờ Việt Nam", "Giờ Việt Nam");
    }

    private static DateTime NowLocal() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTz);

    private static DateOnly TodayLocal() => DateOnly.FromDateTime(NowLocal());

    /// <summary>
    /// Khách phải đặt trước buổi học ít nhất chừng này. Đặt lúc 7h55 cho buổi
    /// 8h00 thì quán không kịp chuẩn bị thêm một bộ dụng cụ.
    /// </summary>
    private static readonly TimeSpan BookingCutoff = TimeSpan.FromHours(2);

    // ==========================================================================
    //  LỊCH
    // ==========================================================================

    public async Task<WorkshopCalendarDto> GetCalendarAsync(
        Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        // Chặn khoảng quá rộng: trang lịch chỉ vẽ một hai tháng, còn một yêu cầu
        // "từ 1900 tới 2100" sẽ kéo cả bảng về bộ nhớ.
        if (to > from.AddDays(370)) to = from.AddDays(370);

        var sessions = await _db.WorkshopSessions
            .AsNoTracking()
            .Where(s => s.StoreId == storeId
                     && s.SessionDate >= from
                     && s.SessionDate <= to
                     && s.Status != WorkshopSessionStatus.Cancelled)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
            .ToListAsync(ct);

        var discounts = await GetActiveDiscountsAsync(storeId, ct);
        var seatDiscounts = discounts.Where(d => d.Scope == WorkshopDiscountScope.PerSeat).ToList();
        var bookingDiscounts = discounts.Where(d => d.Scope == WorkshopDiscountScope.Booking).ToList();

        var seatsTaken = await GetSeatsTakenAsync(sessions.Select(s => s.Id).ToList(), ct);

        // Mức giảm theo chỗ TỐT NHẤT — dùng cho dòng "chỉ từ ...đ" trên ô ngày.
        // Ưu đãi cả lượt không tính vào đây: nó phụ thuộc số người đi cùng, mà
        // lúc xem lịch khách chưa nói mình đi mấy người.
        var bestSeatPercent = seatDiscounts.Count == 0 ? 0 : seatDiscounts.Max(d => d.Percent);

        var now = NowLocal();

        var days = sessions
            .GroupBy(s => s.SessionDate)
            .Select(g => new WorkshopDayDto(
                Date: g.Key.ToString("yyyy-MM-dd"),
                Slots: g.Select(s =>
                {
                    var taken = seatsTaken.GetValueOrDefault(s.Id, 0);
                    var left = Math.Max(0, s.Capacity - taken);
                    return new WorkshopSlotDto(
                        Id: s.Id,
                        Topic: s.Topic,
                        Summary: s.Summary,
                        Date: s.SessionDate.ToString("yyyy-MM-dd"),
                        StartTime: s.StartTime.ToString("HH\\:mm"),
                        EndTime: s.EndTime.ToString("HH\\:mm"),
                        Capacity: s.Capacity,
                        SeatsLeft: left,
                        BasePrice: s.BasePrice,
                        BestPrice: ApplyPercent(s.BasePrice, bestSeatPercent),
                        UnavailableReason: DescribeUnavailable(s, left, now));
                }).ToList()))
            .ToList();

        return new WorkshopCalendarDto(
            From: from.ToString("yyyy-MM-dd"),
            To: to.ToString("yyyy-MM-dd"),
            Today: TodayLocal().ToString("yyyy-MM-dd"),
            Days: days,
            SeatDiscounts: seatDiscounts.Select(ToDto).ToList(),
            BookingDiscounts: bookingDiscounts.Select(ToDto).ToList());
    }

    /// <summary>
    /// Vì sao khung giờ này không đặt được — viết sẵn thành câu cho khách đọc.
    /// <para>
    /// Trả về câu chữ thay vì mã lỗi để giao diện không phải tự dịch, và để lý do
    /// hiện ra giống hệt nhau ở mọi chỗ. Null = đặt được.
    /// </para>
    /// </summary>
    private static string? DescribeUnavailable(WorkshopSession s, int seatsLeft, DateTime now)
    {
        if (s.Status == WorkshopSessionStatus.Cancelled) return "Buổi này đã hủy";
        if (s.Status == WorkshopSessionStatus.Completed) return "Buổi này đã diễn ra";
        if (s.Status == WorkshopSessionStatus.Full)      return "Quán đã khóa buổi này";
        if (s.StartsAtLocal - now < BookingCutoff)
            return s.StartsAtLocal <= now ? "Buổi này đã diễn ra" : "Đã quá hạn đặt trước";
        if (seatsLeft <= 0) return "Đã hết chỗ";
        return null;
    }

    // ==========================================================================
    //  ĐẶT CHỖ
    // ==========================================================================

    public async Task<WorkshopBookingDto> CreateBookingAsync(
        Guid storeId, CreateWorkshopBookingRequest req, CancellationToken ct = default)
    {
        var name = (req.CustomerName ?? "").Trim();
        var phone = NormalizePhone(req.Phone);
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();

        if (name.Length < 2)
            throw new BusinessRuleException("Cần tên người đặt để quán gọi đúng tên khi bạn tới.");
        if (!IsValidPhone(phone))
            throw new BusinessRuleException("Số điện thoại chưa đúng. Nhập 10 số, bắt đầu bằng 0.");

        var lines = (req.Lines ?? new()).Where(l => l.Quantity > 0).ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Chưa chọn chỗ nào. Chọn ít nhất một chỗ rồi đặt lại.");

        var seats = lines.Sum(l => l.Quantity);
        if (seats > 20)
            throw new BusinessRuleException(
                "Một lượt đặt tối đa 20 chỗ. Nhóm đông hơn thì gọi quán để xếp buổi riêng.");

        // Gộp các dòng cùng một diện ưu đãi. Giao diện có thể gửi lên hai dòng
        // "sinh viên 1 chỗ"; gộp lại để hóa đơn không có hai dòng giống hệt nhau.
        var merged = lines
            .GroupBy(l => l.DiscountId)
            .Select(g => (DiscountId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        // Một giao dịch bao trọn: đọc buổi học, đếm chỗ, ghi đặt chỗ, đếm lại.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var session = await _db.WorkshopSessions
                .FirstOrDefaultAsync(s => s.Id == req.SessionId && s.StoreId == storeId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy buổi học này. Có thể lịch đã đổi, tải lại trang giúp.");

            var now = NowLocal();
            var taken = (await GetSeatsTakenAsync(new[] { session.Id }, ct)).GetValueOrDefault(session.Id, 0);
            var left = Math.Max(0, session.Capacity - taken);

            var reason = DescribeUnavailable(session, left, now);
            if (reason is not null)
                throw new BusinessRuleException($"{reason}. Chọn giúp một khung giờ khác.");

            if (seats > left)
                throw new BusinessRuleException(
                    left == 1
                        ? "Buổi này chỉ còn 1 chỗ."
                        : $"Buổi này chỉ còn {left} chỗ.");

            var discounts = await GetActiveDiscountsAsync(storeId, ct);
            var byId = discounts.ToDictionary(d => d.Id);

            // --- Dựng các dòng, GIÁ TÍNH TỪ DỮ LIỆU TRONG DATABASE -----------
            var booking = new WorkshopBooking
            {
                StoreId = storeId,
                SessionId = session.Id,
                Code = await GenerateBookingCodeAsync(ct),
                CustomerName = name,
                Phone = phone,
                Email = email,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                Seats = seats,
                Status = WorkshopBookingStatus.Pending
            };

            foreach (var (discountId, qty) in merged)
            {
                WorkshopDiscount? d = null;
                if (discountId is { } id)
                {
                    if (!byId.TryGetValue(id, out d) || d.Scope != WorkshopDiscountScope.PerSeat)
                        throw new BusinessRuleException(
                            "Một ưu đãi bạn chọn không còn áp dụng. Tải lại trang rồi chọn lại giúp.");
                }

                var unit = ApplyPercent(session.BasePrice, d?.Percent ?? 0);
                booking.Lines.Add(new WorkshopBookingLine
                {
                    BookingId = booking.Id,
                    DiscountId = d?.Id,
                    TierName = d?.Name ?? "Giá thường",
                    Percent = d?.Percent ?? 0,
                    Quantity = qty,
                    UnitPrice = unit,
                    LineTotal = unit * qty
                });
            }

            booking.Subtotal = booking.Lines.Sum(l => l.LineTotal);

            // --- Ưu đãi cả lượt: tự áp, lấy MỘT cái lợi nhất ------------------
            // Không cộng dồn nhiều ưu đãi: cộng dồn làm khách không đoán được
            // mình trả bao nhiêu, và mở đường cho tổng tiền âm khi ai đó lỡ tay
            // đặt hai ưu đãi 60%.
            var best = PickBookingDiscount(discounts, seats, session, now);
            if (best is not null)
            {
                booking.BookingDiscountId = best.Id;
                booking.BookingDiscountName = best.Name;
                booking.BookingDiscountAmount = booking.Subtotal - ApplyPercent(booking.Subtotal, best.Percent);
            }

            booking.GrandTotal = booking.Subtotal - booking.BookingDiscountAmount;

            _db.WorkshopBookings.Add(booking);
            await _db.SaveChangesAsync(ct);

            // --- Đọc lại sau khi ghi -----------------------------------------
            // Hai khách bấm cùng lúc đều đọc thấy "còn 1 chỗ" và cùng đi qua
            // được bước kiểm tra bên trên. Sau khi ghi, đếm lại: nếu tổng đã
            // vượt sức chứa thì người ghi sau tự rút lui. Đây là chỗ RẺ NHẤT để
            // xử lý — PostgreSQL không có ràng buộc nào diễn tả được "tổng một
            // cột của các dòng liên quan không vượt quá N".
            var takenAfter = (await GetSeatsTakenAsync(new[] { session.Id }, ct))
                .GetValueOrDefault(session.Id, 0);
            if (takenAfter > session.Capacity)
            {
                await tx.RollbackAsync(ct);
                throw new BusinessRuleException(
                    "Vừa có người đặt trước bạn vài giây nên buổi này hết chỗ. Chọn giúp khung giờ khác.");
            }

            await tx.CommitAsync(ct);

            return ToDto(booking, session, discounts);
        });
    }

    /// <summary>
    /// Chọn ưu đãi cả lượt có lợi nhất cho khách trong số những cái đủ điều kiện.
    /// </summary>
    private static WorkshopDiscount? PickBookingDiscount(
        IReadOnlyList<WorkshopDiscount> all, int seats, WorkshopSession session, DateTime now)
    {
        var daysAhead = (session.SessionDate.ToDateTime(TimeOnly.MinValue)
                       - now.Date).TotalDays;

        return all
            .Where(d => d.Scope == WorkshopDiscountScope.Booking)
            .Where(d => d.MinSeats == 0 || seats >= d.MinSeats)
            .Where(d => d.MinDaysAhead == 0 || daysAhead >= d.MinDaysAhead)
            .OrderByDescending(d => d.Percent)
            .FirstOrDefault();
    }

    // ==========================================================================
    //  TRA CỨU & HỦY
    // ==========================================================================

    public async Task<WorkshopBookingDto?> GetBookingAsync(
        Guid storeId, string code, string phone, CancellationToken ct = default)
    {
        var booking = await LoadBookingAsync(storeId, code, ct);
        if (booking is null) return null;

        // Số điện thoại là mật khẩu: không có nó thì bất kỳ ai đoán được mã đặt
        // chỗ cũng xem được tên, email và số điện thoại của khách khác.
        if (booking.Phone != NormalizePhone(phone)) return null;

        var discounts = await GetActiveDiscountsAsync(storeId, ct);
        return ToDto(booking, booking.Session!, discounts);
    }

    public async Task<WorkshopBookingDto> CancelBookingAsync(
        Guid storeId, string code, string phone, string? reason, CancellationToken ct = default)
    {
        var booking = await LoadBookingAsync(storeId, code, ct)
            ?? throw new BusinessRuleException("Không tìm thấy mã đặt chỗ này.");

        if (booking.Phone != NormalizePhone(phone))
            throw new BusinessRuleException("Số điện thoại không khớp với lượt đặt này.");

        if (booking.Status == WorkshopBookingStatus.Cancelled)
            throw new BusinessRuleException("Lượt đặt này đã hủy từ trước rồi.");

        if (booking.Status == WorkshopBookingStatus.CheckedIn)
            throw new BusinessRuleException("Bạn đã điểm danh buổi này, không hủy được nữa.");

        // Hủy sát giờ vẫn để quán xử lý tay: chỗ đã bị giữ, nguyên liệu đã chuẩn
        // bị. Cho khách tự hủy lúc này là bật đèn xanh cho việc đặt rồi bỏ.
        if (booking.Session!.StartsAtLocal - NowLocal() < BookingCutoff)
            throw new BusinessRuleException(
                "Đã quá hạn tự hủy. Gọi quán giúp để được sắp xếp.");

        booking.Status = WorkshopBookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = string.IsNullOrWhiteSpace(reason) ? "Khách tự hủy" : reason.Trim();

        await _db.SaveChangesAsync(ct);

        var discounts = await GetActiveDiscountsAsync(storeId, ct);
        return ToDto(booking, booking.Session!, discounts);
    }

    // ==========================================================================
    //  PHÍA QUÁN
    // ==========================================================================

    public async Task<IReadOnlyList<WorkshopBookingDto>> GetBookingsForAdminAsync(
        Guid storeId, DateOnly? from, DateOnly? to, int? status, CancellationToken ct = default)
    {
        var q = _db.WorkshopBookings
            .AsNoTracking()
            .Include(bk => bk.Lines)
            .Include(bk => bk.Session)
            .Where(bk => bk.StoreId == storeId);

        if (from is { } f) q = q.Where(bk => bk.Session!.SessionDate >= f);
        if (to is { } t)   q = q.Where(bk => bk.Session!.SessionDate <= t);
        if (status is { } s) q = q.Where(bk => (int)bk.Status == s);

        var list = await q
            .OrderBy(bk => bk.Session!.SessionDate)
            .ThenBy(bk => bk.Session!.StartTime)
            .ThenBy(bk => bk.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var discounts = await GetActiveDiscountsAsync(storeId, ct);
        return list.Select(bk => ToDto(bk, bk.Session!, discounts)).ToList();
    }

    public async Task<WorkshopBookingDto> UpdateBookingStatusAsync(
        Guid storeId, Guid bookingId, int status, string? reason, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(WorkshopBookingStatus), status))
            throw new BusinessRuleException("Trạng thái không hợp lệ.");

        var booking = await _db.WorkshopBookings
            .Include(bk => bk.Lines)
            .Include(bk => bk.Session)
            .FirstOrDefaultAsync(bk => bk.Id == bookingId && bk.StoreId == storeId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy lượt đặt này.");

        var next = (WorkshopBookingStatus)status;

        if (next == WorkshopBookingStatus.Cancelled && string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Hủy chỗ phải ghi lý do — khách sẽ đọc được câu này.");

        booking.Status = next;
        booking.CancelReason = next == WorkshopBookingStatus.Cancelled ? reason!.Trim() : booking.CancelReason;
        booking.CancelledAt = next == WorkshopBookingStatus.Cancelled ? DateTime.UtcNow : booking.CancelledAt;
        booking.CheckedInAt = next == WorkshopBookingStatus.CheckedIn ? DateTime.UtcNow : booking.CheckedInAt;

        await _db.SaveChangesAsync(ct);

        var discounts = await GetActiveDiscountsAsync(storeId, ct);
        return ToDto(booking, booking.Session!, discounts);
    }

    // ==========================================================================
    //  TIỆN ÍCH
    // ==========================================================================

    private Task<List<WorkshopDiscount>> GetActiveDiscountsAsync(Guid storeId, CancellationToken ct) =>
        _db.WorkshopDiscounts
            .AsNoTracking()
            .Where(d => d.StoreId == storeId && d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync(ct);

    private Task<WorkshopBooking?> LoadBookingAsync(Guid storeId, string code, CancellationToken ct) =>
        _db.WorkshopBookings
            .Include(bk => bk.Lines)
            .Include(bk => bk.Session)
            .FirstOrDefaultAsync(bk => bk.StoreId == storeId
                                    && bk.Code == (code ?? "").Trim().ToUpperInvariant(), ct);

    /// <summary>
    /// Số chỗ ĐANG BỊ GIỮ của từng buổi.
    /// <para>
    /// Điều kiện giữ chỗ viết thẳng ở đây thay vì gọi <c>HoldsSeat</c> vì EF phải
    /// dịch được sang SQL — thuộc tính tính toán trên entity thì không. Hai chỗ
    /// PHẢI khớp nhau; sửa một bên thì sửa cả bên kia.
    /// </para>
    /// </summary>
    // ==========================================================================
    //  SỬA GIÁ VÀ SỨC CHỨA MỘT BUỔI
    // ==========================================================================
    //
    //  BA LUẬT, CẢ BA ĐỀU ĐỂ TRÁNH HỨA VỚI KHÁCH ĐIỀU KHÔNG GIỮ ĐƯỢC:
    //
    //  1. KHÔNG SỬA BUỔI ĐÃ BẮT ĐẦU. Giá của buổi đã qua chẳng còn tác dụng gì
    //     (lượt đặt đã chụp giá lúc đặt), nên cho sửa chỉ tạo ảo giác là sửa
    //     được điều gì đó.
    //
    //  2. KHÔNG HẠ SỨC CHỨA XUỐNG DƯỚI SỐ CHỖ ĐÃ BÁN. Hạ được thì hệ thống tự
    //     tạo ra một buổi bán quá chỗ, và người phát hiện ra sẽ là nhân viên
    //     đứng trước tám khách với sáu bộ dụng cụ.
    //
    //  3. ĐỔI GIÁ KHÔNG ĐỘNG TỚI LƯỢT ĐÃ ĐẶT. Đây là hệ quả của thiết kế sẵn
    //     có — WorkshopBookingLine.UnitPrice chụp lại giá lúc đặt — chứ không
    //     phải việc hàm này phải làm thêm. Ghi ra đây để lần sau không ai
    //     "sửa cho đồng bộ".
    // ==========================================================================

    /// <summary>Trần giá một chỗ. Không phải luật kinh doanh, là lưới chắn lỗi gõ thừa số 0.</summary>
    private const int MaxSeatPrice = 10_000_000;

    /// <summary>Trần sức chứa. Quán không có chỗ cho lớp đông hơn thế, và 600 thường là gõ nhầm 60.</summary>
    private const int MaxCapacity = 60;

    public async Task<WorkshopSessionSavedDto> UpdateSessionAsync(
        Guid storeId, Guid sessionId, UpdateWorkshopSessionRequest req, CancellationToken ct = default)
    {
        if (req.BasePrice < 0 || req.BasePrice > MaxSeatPrice)
            throw new BusinessRuleException(
                $"Giá một chỗ phải trong khoảng 0 – {MaxSeatPrice:N0} đ.");

        if (req.Capacity < 1 || req.Capacity > MaxCapacity)
            throw new BusinessRuleException($"Số chỗ phải trong khoảng 1 – {MaxCapacity}.");

        var session = await _db.WorkshopSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StoreId == storeId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy buổi này.");

        var now = NowLocal();

        if (session.StartsAtLocal <= now)
            throw new BusinessRuleException(
                "Buổi này đã bắt đầu nên không sửa được nữa. " +
                "Các lượt đã đặt vẫn giữ đúng số tiền báo cho khách lúc đặt.");

        // --- Các buổi sẽ đụng tới ------------------------------------------
        var targets = new List<WorkshopSession> { session };

        if (req.ApplyToSameSlot)
        {
            // "Cùng khung giờ" = trùng THỨ trong tuần và trùng giờ bắt đầu.
            // Lọc thứ ở phía C# chứ không phải trong câu truy vấn: DayOfWeek của
            // DateOnly không dịch được sang SQL, để EF tự xoay sẽ thành quét bảng.
            var later = await _db.WorkshopSessions
                .Where(s => s.StoreId == storeId
                         && s.Id != session.Id
                         && s.StartTime == session.StartTime
                         && s.SessionDate >= DateOnly.FromDateTime(now)
                         && s.Status != WorkshopSessionStatus.Cancelled)
                .ToListAsync(ct);

            targets.AddRange(later.Where(s =>
                s.SessionDate.DayOfWeek == session.SessionDate.DayOfWeek
                && s.StartsAtLocal > now));
        }

        // --- Chỗ đã bán của từng buổi --------------------------------------
        var taken = await GetSeatsTakenAsync(targets.Select(s => s.Id).ToList(), ct);

        var mySeats = taken.GetValueOrDefault(session.Id, 0);
        if (req.Capacity < mySeats)
            throw new BusinessRuleException(
                $"Buổi này đã có {mySeats} chỗ được đặt, không hạ sức chứa xuống {req.Capacity} được. " +
                "Muốn thu nhỏ lớp thì phải hủy bớt lượt đặt trước, và nhớ gọi báo khách.");

        var topic   = string.IsNullOrWhiteSpace(req.Topic)   ? null : req.Topic.Trim();
        var summary = string.IsNullOrWhiteSpace(req.Summary) ? null : req.Summary.Trim();

        var updated = 0;
        var skipped = 0;

        foreach (var s in targets)
        {
            // Buổi nào đã bán nhiều hơn sức chứa mới thì BỎ QUA chứ không làm
            // hỏng cả lượt sửa. Chủ quán đổi giá cho bốn mươi buổi mà bị chặn
            // vì đúng một buổi đông khách thì lần sau họ sẽ thôi dùng nút này.
            if (req.Capacity < taken.GetValueOrDefault(s.Id, 0)) { skipped++; continue; }

            s.BasePrice = req.BasePrice;
            s.Capacity  = req.Capacity;
            if (topic   is not null) s.Topic   = topic;
            if (summary is not null) s.Summary = summary;
            s.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        // Ghi chú nội bộ CHỈ gắn cho buổi đang mở, không rải ra cả khung giờ:
        // "nhớ mượn thêm ấm" là chuyện của đúng buổi đó.
        if (req.Note is not null) session.Note = req.Note.Trim();

        await _db.SaveChangesAsync(ct);

        var message = updated switch
        {
            0 => "Không buổi nào được đổi.",
            1 => $"Đã đổi buổi {session.SessionDate:dd/MM} {session.StartTime:HH\\:mm}.",
            _ => $"Đã đổi {updated} buổi cùng khung giờ."
        };

        if (skipped > 0)
            message += $" Bỏ qua {skipped} buổi vì đã bán nhiều hơn {req.Capacity} chỗ.";

        return new WorkshopSessionSavedDto(
            Id:           session.Id,
            BasePrice:    session.BasePrice,
            Capacity:     session.Capacity,
            SeatsTaken:   mySeats,
            UpdatedCount: updated,
            SkippedCount: skipped,
            Message:      message);
    }

    private async Task<Dictionary<Guid, int>> GetSeatsTakenAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return new Dictionary<Guid, int>();

        var rows = await _db.WorkshopBookings
            .AsNoTracking()
            .Where(bk => sessionIds.Contains(bk.SessionId)
                      && bk.Status != WorkshopBookingStatus.Cancelled)
            .GroupBy(bk => bk.SessionId)
            .Select(g => new { SessionId = g.Key, Seats = g.Sum(x => x.Seats) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.SessionId, r => r.Seats);
    }

    /// <summary>
    /// Giá sau khi giảm <paramref name="percent"/>%, làm tròn XUỐNG tới nghìn đồng.
    /// <para>
    /// Làm tròn xuống chứ không tròn gần nhất: sai số luôn nghiêng về phía khách,
    /// và không bao giờ có chuyện quán báo giá thấp hơn rồi thu cao hơn. Tròn tới
    /// nghìn vì quán không có tiền lẻ dưới nghìn để thối.
    /// </para>
    /// </summary>
    private static int ApplyPercent(int amount, int percent)
    {
        if (percent <= 0) return amount;
        if (percent >= 100) return 0;
        var after = (long)amount * (100 - percent) / 100;
        return (int)(after / 1000 * 1000);
    }

    /// <summary>
    /// Sinh mã đặt chỗ dạng "WS-XXXXXX".
    /// <para>
    /// Bỏ các ký tự dễ đọc nhầm khi khách đọc mã qua điện thoại: 0/O, 1/I/L, 2/Z,
    /// 5/S, 8/B. Thử lại khi trùng — chỉ mục duy nhất trên cột này mới là chốt
    /// chặn cuối, vòng lặp ở đây chỉ để khách không gặp lỗi.
    /// </para>
    /// </summary>
    private async Task<string> GenerateBookingCodeAsync(CancellationToken ct)
    {
        const string alphabet = "34679ACDEFGHJKMNPQRTUVWXY";

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];

            var code = "WS-" + new string(chars);
            var exists = await _db.WorkshopBookings.AnyAsync(bk => bk.Code == code, ct);
            if (!exists) return code;
        }

        // 25^6 ≈ 244 triệu tổ hợp: tám lần trùng liên tiếp nghĩa là có gì đó sai
        // chứ không phải xui. Lấy mã dài hơn để khách vẫn đặt được, và để lại
        // dấu vết dễ nhận ra trong dữ liệu.
        return "WS-" + GuidV7.New().ToString("N")[..10].ToUpperInvariant();
    }

    /// <summary>Bỏ dấu cách, dấu chấm và +84 ở đầu để so sánh số điện thoại nhất quán.</summary>
    private static string NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length == 11) digits = "0" + digits[2..];
        return digits;
    }

    private static bool IsValidPhone(string normalized) =>
        normalized.Length == 10 && normalized[0] == '0';

    private static WorkshopDiscountDto ToDto(WorkshopDiscount d) => new(
        d.Id, d.Code, d.Name, d.Description, (int)d.Scope,
        d.Percent, d.MinSeats, d.MinDaysAhead, d.RequiresProof, d.ProofNote);

    private static WorkshopBookingDto ToDto(
        WorkshopBooking bk, WorkshopSession session, IReadOnlyList<WorkshopDiscount> discounts)
    {
        // Nhắc mang giấy tờ: gom từ đúng những diện ưu đãi lượt đặt này đã dùng.
        // Liệt kê hết mọi ưu đãi cần thẻ sẽ bắt người trả giá thường cũng mang
        // thẻ sinh viên đi.
        var usedIds = bk.Lines.Where(l => l.DiscountId is not null)
                              .Select(l => l.DiscountId!.Value).ToHashSet();
        var reminders = discounts
            .Where(d => usedIds.Contains(d.Id) && d.RequiresProof)
            .Select(d => d.ProofNote ?? $"{d.Name}: cần trình giấy tờ khi tới quán")
            .ToList();

        return new WorkshopBookingDto(
            Id: bk.Id,
            Code: bk.Code,
            CustomerName: bk.CustomerName,
            Phone: bk.Phone,
            Email: bk.Email,
            Note: bk.Note,
            Status: (int)bk.Status,
            StatusLabel: StatusLabel(bk.Status),
            SessionId: session.Id,
            Topic: session.Topic,
            Date: session.SessionDate.ToString("yyyy-MM-dd"),
            StartTime: session.StartTime.ToString("HH\\:mm"),
            EndTime: session.EndTime.ToString("HH\\:mm"),
            Seats: bk.Seats,
            Lines: bk.Lines
                .OrderByDescending(l => l.UnitPrice)
                .Select(l => new WorkshopBookingLineDto(
                    l.TierName, l.Percent, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList(),
            Subtotal: bk.Subtotal,
            BookingDiscountName: bk.BookingDiscountName,
            BookingDiscountAmount: bk.BookingDiscountAmount,
            GrandTotal: bk.GrandTotal,
            ProofReminders: reminders,
            CancelReason: bk.CancelReason);
    }

    private static string StatusLabel(WorkshopBookingStatus s) => s switch
    {
        WorkshopBookingStatus.Pending   => "Chờ quán xác nhận",
        WorkshopBookingStatus.Confirmed => "Đã xác nhận",
        WorkshopBookingStatus.CheckedIn => "Đã tới quán",
        WorkshopBookingStatus.Cancelled => "Đã hủy",
        WorkshopBookingStatus.NoShow    => "Không tới",
        _ => "Không rõ"
    };
}
