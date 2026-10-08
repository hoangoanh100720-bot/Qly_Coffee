using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  CA LÀM VIỆC & ĐỐI SOÁT KÉT
//
//  Nhận ca → bán hàng → đếm két → giao ca. Xem chú thích đầu file
//  Domain/Entities/Shifts.cs cho lý do tính doanh thu theo khoảng thời gian.
//
//  CÁCH TÍNH TIỀN THU TRONG CA
//    Một đơn thuộc ca nếu thời điểm THU TIỀN rơi vào [mở ca, đóng ca):
//      · thời điểm thu = PaidAt; đơn tiền mặt cũ (trước khi OrderService ghi
//        PaidAt) thì lấy CompletedAt — lúc khách nhận ly và trả tiền ở quầy;
//      · số tiền = PaidAmount; đơn cũ chưa có thì lấy GrandTotal.
//    Chỉ tiền mặt đi vào két. Chuyển khoản vẫn là doanh thu của ca nhưng không
//    nằm trong két, nên không cộng vào "tiền lẽ ra có trong két".
// ==============================================================================

public interface IShiftService
{
    Task<CurrentShiftDto> GetCurrentAsync(Guid storeId, Guid? userId, CancellationToken ct = default);

    Task<ShiftDto> OpenAsync(Guid storeId, Guid userId, OpenShiftRequest req, CancellationToken ct = default);

    Task<ShiftDto> CloseAsync(Guid storeId, Guid userId, CloseShiftRequest req, CancellationToken ct = default);

    /// <summary>Nhân viên nhận két ở quầy chấm công (đã xác minh mã NV + PIN).</summary>
    Task<ShiftDto> OpenForEmployeeAsync(
        Guid storeId, Guid deviceUserId, Employee emp, int countedCash, string? note, CancellationToken ct = default);

    /// <summary>Nhân viên đếm két và giao lúc chấm công ra.</summary>
    Task<ShiftDto> CloseForEmployeeAsync(
        Guid storeId, Guid deviceUserId, Employee emp, CloseShiftRequest req, CancellationToken ct = default);

    Task<ShiftReportDto> GetReportAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public class ShiftService : IShiftService
{
    private readonly AppDbContext _db;

    public ShiftService(AppDbContext db) => _db = db;

    /// <summary>
    /// Giờ Việt Nam là UTC+7 cố định quanh năm (không có giờ mùa hè), nên đổi
    /// ngày báo cáo ra mốc UTC chỉ cần trừ 7 tiếng — không cần tra múi giờ.
    /// </summary>
    private static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);

    /// <summary>Lệch két tới mức này (đồng) thì bắt buộc ghi chú lý do.</summary>
    private const int NoteRequiredAbove = 0;

    // ==========================================================================
    //  ĐỌC
    // ==========================================================================

    public async Task<CurrentShiftDto> GetCurrentAsync(Guid storeId, Guid? userId, CancellationToken ct = default)
    {
        var open = await _db.CashShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId && s.Status == ShiftStatus.Open, ct);

        var last = await LastClosedAsync(storeId, ct);

        ShiftDto? openDto = null;
        if (open is not null)
        {
            var sales = await SumSalesAsync(storeId, open.OpenedAt, DateTime.UtcNow, ct);
            openDto = ToDto(open, sales, userId);
        }

        return new CurrentShiftDto(
            openDto,
            last is null ? null : new ShiftHandoverDto(
                last.ClosedByName ?? last.OpenedByName,
                last.ClosedAt!.Value,
                last.HandoverCash ?? 0,
                last.HandedOverToName));
    }

    public async Task<ShiftReportDto> GetReportAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        // SpecifyKind(Utc) là BẮT BUỘC: DateOnly.ToDateTime trả về Kind=Unspecified,
        // và Npgsql từ chối ghi Unspecified vào cột timestamptz.
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue) - VnOffset, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue) - VnOffset, DateTimeKind.Utc);

        // Ca BẮT ĐẦU trong khoảng ngày được chọn. Ca mở 22h tối hôm trước đóng
        // 6h sáng nay thuộc về hôm trước — đúng cách quán vẫn ghi sổ.
        var shifts = await _db.CashShifts.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.OpenedAt >= fromUtc && s.OpenedAt < toUtc)
            .OrderByDescending(s => s.OpenedAt)
            .ToListAsync(ct);

        var dtos = new List<ShiftDto>(shifts.Count);
        foreach (var s in shifts)
        {
            // Ca đã đóng dùng số chụp lại; ca đang mở tính tới hiện tại.
            var sales = s.Status == ShiftStatus.Closed
                ? new Sales(s.CashSales ?? 0, s.TransferSales ?? 0, s.PaidOrderCount ?? 0)
                : await SumSalesAsync(storeId, s.OpenedAt, DateTime.UtcNow, ct);
            dtos.Add(ToDto(s, sales, userId: null));
        }

        // ---- Tiền thu NGOÀI CA ------------------------------------------------
        // Mọi khoản thu trong khoảng ngày, trừ đi phần đã rơi vào một ca nào đó.
        // Tính trên cả các ca bắt đầu trước khoảng ngày nhưng kéo dài sang (ca
        // đêm), để không báo nhầm tiền của ca đêm là "ngoài ca".
        var windows = await _db.CashShifts.AsNoTracking()
            .Where(s => s.StoreId == storeId
                        && s.OpenedAt < toUtc
                        && (s.ClosedAt == null || s.ClosedAt > fromUtc))
            .Select(s => new { s.OpenedAt, s.ClosedAt })
            .ToListAsync(ct);

        var payments = await PaymentsQuery(storeId, fromUtc, toUtc)
            .Select(p => new { p.At, p.Amount, p.IsCash })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var outside = payments
            .Where(p => !windows.Any(w => p.At >= w.OpenedAt && p.At < (w.ClosedAt ?? now)))
            .ToList();

        var closed = dtos.Where(d => d.Status == (int)ShiftStatus.Closed).ToList();

        return new ShiftReportDto(
            dtos,
            TotalCashSales:       dtos.Sum(d => d.CashSales),
            TotalTransferSales:   dtos.Sum(d => d.TransferSales),
            TotalDeposited:       closed.Sum(d => d.DepositedCash ?? 0),
            TotalDifference:      closed.Sum(d => d.CashDifference ?? 0),
            ShiftsWithDifference: closed.Count(d => (d.CashDifference ?? 0) != 0),
            OutsideShiftCash:     outside.Where(p => p.IsCash).Sum(p => p.Amount),
            OutsideShiftTransfer: outside.Where(p => !p.IsCash).Sum(p => p.Amount),
            OutsideShiftOrders:   outside.Count);
    }

    // ==========================================================================
    //  GHI
    //
    //  Hai lối vào cùng đi qua một lõi (OpenCoreAsync / CloseCoreAsync):
    //    · OpenAsync / CloseAsync  — quản lý thao tác bằng tài khoản đăng nhập
    //      (VD đóng ca hộ khi nhân viên về quên giao két);
    //    · *ForEmployeeAsync        — nhân viên ở quầy chấm công, đã xác minh
    //      bằng mã NV + PIN (AttendanceService gọi).
    // ==========================================================================

    /// <summary>Ai đang thao tác: tài khoản của máy, tên hiển thị, nhân viên (nếu có).</summary>
    public sealed record ShiftActor(Guid DeviceUserId, string Name, Guid? EmployeeId, bool IsManager);

    public async Task<ShiftDto> OpenAsync(Guid storeId, Guid userId, OpenShiftRequest req, CancellationToken ct = default)
    {
        var actor = await ActorFromUserAsync(userId, ct);
        var shift = await OpenCoreAsync(storeId, actor, req.OpeningCash, req.Note, ct);
        return ToDto(shift, new Sales(0, 0, 0), userId);
    }

    public async Task<ShiftDto> CloseAsync(Guid storeId, Guid userId, CloseShiftRequest req, CancellationToken ct = default)
    {
        var actor = await ActorFromUserAsync(userId, ct);
        var (shift, sales) = await CloseCoreAsync(storeId, actor, req, ct);
        return ToDto(shift, sales, userId);
    }

    public async Task<ShiftDto> OpenForEmployeeAsync(
        Guid storeId, Guid deviceUserId, Employee emp, int countedCash, string? note, CancellationToken ct = default)
    {
        var shift = await OpenCoreAsync(storeId, new ShiftActor(deviceUserId, emp.FullName, emp.Id, false), countedCash, note, ct);
        return ToDto(shift, new Sales(0, 0, 0), userId: null);
    }

    public async Task<ShiftDto> CloseForEmployeeAsync(
        Guid storeId, Guid deviceUserId, Employee emp, CloseShiftRequest req, CancellationToken ct = default)
    {
        var (shift, sales) = await CloseCoreAsync(storeId, new ShiftActor(deviceUserId, emp.FullName, emp.Id, false), req, ct);
        return ToDto(shift, sales, userId: null);
    }

    private async Task<ShiftActor> ActorFromUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new BusinessRuleException("Không tìm thấy tài khoản đang đăng nhập.");
        return new ShiftActor(userId, user.FullName, null, user.Role is UserRole.Manager or UserRole.Owner);
    }

    private async Task<CashShift> OpenCoreAsync(
        Guid storeId, ShiftActor actor, int openingCash, string? note, CancellationToken ct)
    {
        if (openingCash < 0)
            throw new BusinessRuleException("Tiền két đầu ca không được âm.");

        var current = await _db.CashShifts
            .FirstOrDefaultAsync(s => s.StoreId == storeId && s.Status == ShiftStatus.Open, ct);
        if (current is not null)
            throw new BusinessRuleException(
                $"{current.OpenedByName} đang giữ két từ {ToVn(current.OpenedAt):HH:mm}. " +
                "Người đó phải đếm két và giao trước.");

        var last = await LastClosedAsync(storeId, ct);
        var expected = last?.HandoverCash;

        // Nhận két lệch với số ca trước giao thì phải ghi rõ — đây là chỗ duy
        // nhất phát hiện được tiền mất GIỮA hai ca (lúc két không ai giữ).
        if (expected is int exp && exp != openingCash && string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException(
                $"Ca trước giao {exp:N0}đ nhưng bạn đếm được {openingCash:N0}đ " +
                $"({(openingCash < exp ? "âm két" : "dư")} {Math.Abs(openingCash - exp):N0}đ). " +
                "Ghi chú lý do trước khi nhận két.");

        var shift = new CashShift
        {
            StoreId             = storeId,
            Status              = ShiftStatus.Open,
            OpenedById          = actor.DeviceUserId,
            OpenedByEmployeeId  = actor.EmployeeId,
            OpenedByName        = actor.Name,
            OpenedAt            = DateTime.UtcNow,
            OpeningCash         = openingCash,
            ExpectedOpeningCash = expected,
            ReceivedFromName    = last?.ClosedByName,
            OpeningNote         = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };

        _db.CashShifts.Add(shift);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Hai người bấm "Nhận két" cùng lúc: unique index chỉ cho một ca mở.
            _db.Entry(shift).State = EntityState.Detached;
            throw new BusinessRuleException("Vừa có người khác nhận két. Tải lại để xem ai đang giữ két.");
        }

        return shift;
    }

    private async Task<(CashShift Shift, Sales Sales)> CloseCoreAsync(
        Guid storeId, ShiftActor actor, CloseShiftRequest req, CancellationToken ct)
    {
        if (req.CountedCash < 0 || req.HandoverCash < 0)
            throw new BusinessRuleException("Số tiền không được âm.");
        if (req.HandoverCash > req.CountedCash)
            throw new BusinessRuleException("Tiền để lại két không thể nhiều hơn tiền đếm được.");

        var shift = await _db.CashShifts
            .FirstOrDefaultAsync(s => s.StoreId == storeId && s.Status == ShiftStatus.Open, ct)
            ?? throw new BusinessRuleException("Không có ai đang giữ két.");

        // Chỉ người giữ két hoặc quản lý được giao két. Người khác giao hộ là
        // nhận trách nhiệm cho số tiền mình không giữ.
        var isHolder = actor.EmployeeId is Guid eid
            ? shift.OpenedByEmployeeId == eid
            : shift.OpenedByEmployeeId is null && shift.OpenedById == actor.DeviceUserId;
        if (!isHolder && !actor.IsManager)
            throw new BusinessRuleException(
                $"Két đang do {shift.OpenedByName} giữ. Chỉ người đó hoặc quản lý được đếm và giao két.");

        Employee? next = null;
        if (req.HandOverToEmployeeId is Guid nextId)
        {
            next = await _db.Employees.AsNoTracking()
                       .FirstOrDefaultAsync(e => e.Id == nextId && e.StoreId == storeId && e.IsActive, ct)
                   ?? throw new BusinessRuleException("Người nhận ca không hợp lệ.");
        }

        var closedAt = DateTime.UtcNow;
        var sales = await SumSalesAsync(storeId, shift.OpenedAt, closedAt, ct);
        var expectedCash = shift.OpeningCash + sales.Cash;
        var difference = req.CountedCash - expectedCash;

        if (Math.Abs(difference) > NoteRequiredAbove && string.IsNullOrWhiteSpace(req.Note))
            throw new BusinessRuleException(
                $"Két {(difference < 0 ? "hụt" : "dư")} {Math.Abs(difference):N0}đ so với sổ. " +
                "Ghi chú lý do trước khi giao két.");

        shift.Status                 = ShiftStatus.Closed;
        shift.ClosedById             = actor.DeviceUserId;
        shift.ClosedByEmployeeId     = actor.EmployeeId;
        shift.ClosedByName           = actor.Name;
        shift.ClosedAt               = closedAt;
        shift.HandedOverToEmployeeId = next?.Id;
        shift.HandedOverToName       = next?.FullName;
        shift.CountedCash            = req.CountedCash;
        shift.HandoverCash           = req.HandoverCash;
        shift.DepositedCash          = req.CountedCash - req.HandoverCash;
        shift.ClosingNote            = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        shift.CashSales              = sales.Cash;
        shift.TransferSales          = sales.Transfer;
        shift.PaidOrderCount         = sales.Orders;
        shift.ExpectedCash           = expectedCash;
        shift.CashDifference         = difference;
        shift.UpdatedAt              = closedAt;

        await _db.SaveChangesAsync(ct);
        return (shift, sales);
    }

    // ==========================================================================
    //  NỘI BỘ
    // ==========================================================================

    private sealed record Sales(int Cash, int Transfer, int Orders);

    private sealed class Payment
    {
        public DateTime At { get; init; }
        public int Amount { get; init; }
        public bool IsCash { get; init; }
    }

    /// <summary>Các khoản đã thu tiền trong [from, to). Xem quy tắc ở đầu file.</summary>
    private IQueryable<Payment> PaymentsQuery(Guid storeId, DateTime fromUtc, DateTime toUtc)
        => _db.Orders.AsNoTracking()
            .Where(o => o.StoreId == storeId && o.PaymentStatus == PaymentStatus.Paid)
            .Select(o => new Payment
            {
                At     = o.PaidAt ?? o.CompletedAt ?? o.PlacedAt,
                Amount = o.PaidAmount > 0 ? o.PaidAmount : o.GrandTotal,
                IsCash = o.PaymentMethod == PaymentMethod.Cash,
            })
            .Where(p => p.At >= fromUtc && p.At < toUtc);

    private async Task<Sales> SumSalesAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Cộng ở bộ nhớ: một ca chỉ vài trăm đơn, còn GroupBy sau một phép
        // chiếu có toán tử ?? thì không phải bản EF nào cũng dịch ra SQL được.
        var rows = await PaymentsQuery(storeId, fromUtc, toUtc)
            .Select(p => new { p.Amount, p.IsCash })
            .ToListAsync(ct);

        return new Sales(
            Cash:     rows.Where(r => r.IsCash).Sum(r => r.Amount),
            Transfer: rows.Where(r => !r.IsCash).Sum(r => r.Amount),
            Orders:   rows.Count);
    }

    private Task<CashShift?> LastClosedAsync(Guid storeId, CancellationToken ct)
        => _db.CashShifts.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.Status == ShiftStatus.Closed)
            .OrderByDescending(s => s.ClosedAt)
            .FirstOrDefaultAsync(ct);

    private static ShiftDto ToDto(CashShift s, Sales sales, Guid? userId) => new(
        s.Id,
        (int)s.Status,
        s.OpenedByName,
        s.OpenedAt,
        s.OpeningCash,
        s.ExpectedOpeningCash,
        s.ReceivedFromName,
        s.OpeningNote,
        s.ClosedByName,
        s.ClosedAt,
        s.HandedOverToName,
        s.CountedCash,
        s.HandoverCash,
        s.DepositedCash,
        s.ClosingNote,
        sales.Cash,
        sales.Transfer,
        sales.Orders,
        s.ExpectedCash ?? s.OpeningCash + sales.Cash,
        s.CashDifference,
        IsMine: userId is not null && s.OpenedById == userId);

    private static DateTime ToVn(DateTime utc) => utc + VnOffset;
}
