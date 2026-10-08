using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  NHÂN SỰ & CHẤM CÔNG
//
//  QUẦY CHẤM CÔNG (máy dùng chung, đăng nhập tài khoản nhân viên):
//    chọn tên → gõ mã NV + PIN → "Chấm công vào" → hệ thống báo đúng giờ hay
//    trễ bao nhiêu phút. Ai giữ két thì đếm két ngay lúc này, so với số ca
//    trước bàn giao để bắt âm két. Về thì "Chấm công ra"; người giữ két đếm và
//    giao két luôn trong cùng thao tác.
//
//  QUẢN LÝ: thêm/sửa nhân viên, lương giờ, đặt lại PIN; đặt giờ các khung ca;
//  xem bảng công — ai trễ bao nhiêu, làm bao nhiêu giờ, được bao nhiêu tiền,
//  giữ két thì hụt bao nhiêu.
//
//  CÁCH TÍNH (xem thêm Domain/Entities/Staffing.cs):
//    trễ       = giờ vào − giờ bắt đầu ca (không âm)
//    về sớm    = giờ kết thúc ca − giờ ra (không âm)
//    phút công = phần GIAO giữa [vào, ra] và [bắt đầu ca, kết thúc ca]
//                → tới sớm hay ở lại quá giờ đều không cộng thêm
//    tiền công = phút công × lương giờ / 60, làm tròn tới đồng
// ==============================================================================

public interface IStaffService
{
    // ---- Quầy chấm công ---------------------------------------------------------
    Task<KioskDto> GetKioskAsync(Guid storeId, Guid? deviceUserId, CancellationToken ct = default);
    Task<CheckInResultDto> CheckInAsync(Guid storeId, Guid deviceUserId, CheckInRequest req, CancellationToken ct = default);
    Task<ShiftDto> TakeDrawerAsync(Guid storeId, Guid deviceUserId, TakeDrawerRequest req, CancellationToken ct = default);
    Task<CheckOutResultDto> CheckOutAsync(Guid storeId, Guid deviceUserId, CheckOutRequest req, CancellationToken ct = default);

    // ---- Quản lý nhân sự ---------------------------------------------------------
    Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid storeId, CancellationToken ct = default);
    Task<EmployeeDto> CreateEmployeeAsync(Guid storeId, SaveEmployeeRequest req, CancellationToken ct = default);
    Task<EmployeeDto> UpdateEmployeeAsync(Guid storeId, Guid id, SaveEmployeeRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<WorkSlotDto>> GetSlotsAsync(Guid storeId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkSlotDto>> SaveSlotsAsync(Guid storeId, IReadOnlyList<SaveWorkSlotRequest> slots, CancellationToken ct = default);
    Task<TimesheetDto> GetTimesheetAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public class StaffService : IStaffService
{
    private readonly AppDbContext _db;
    private readonly IShiftService _shifts;

    public StaffService(AppDbContext db, IShiftService shifts)
    {
        _db = db;
        _shifts = shifts;
    }

    /// <summary>Giờ Việt Nam: UTC+7 cố định quanh năm.</summary>
    private static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);

    /// <summary>Được chấm công vào sớm nhất trước giờ bắt đầu ca bao nhiêu phút.</summary>
    private const int EarliestCheckInMinutes = 60;

    /// <summary>Sai PIN bao nhiêu lần thì khoá, khoá bao lâu.</summary>
    private const int MaxPinAttempts = 5;
    private static readonly TimeSpan PinLockDuration = TimeSpan.FromMinutes(5);

    /// <summary>Khung ca mặc định khi quán chưa đặt: bốn ca 4 tiếng, phủ 06:00–22:00.</summary>
    private static readonly (string Name, TimeOnly Start)[] DefaultSlots =
    {
        ("Ca sáng",  new TimeOnly(6, 0)),
        ("Ca trưa",  new TimeOnly(10, 0)),
        ("Ca chiều", new TimeOnly(14, 0)),
        ("Ca tối",   new TimeOnly(18, 0)),
    };

    // ==========================================================================
    //  QUẦY CHẤM CÔNG
    // ==========================================================================

    public async Task<KioskDto> GetKioskAsync(Guid storeId, Guid? deviceUserId, CancellationToken ct = default)
    {
        var slots = await ActiveSlotsAsync(storeId, ct);
        var now = DateTime.UtcNow;

        var onDuty = await _db.Attendances.AsNoTracking()
            .Where(a => a.StoreId == storeId && a.CheckOutAt == null)
            .OrderBy(a => a.CheckInAt)
            .ToListAsync(ct);

        var drawer = await _shifts.GetCurrentAsync(storeId, deviceUserId, ct);
        var drawerHolder = await _db.CashShifts.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.Status == ShiftStatus.Open)
            .Select(s => s.OpenedByEmployeeId)
            .FirstOrDefaultAsync(ct);

        var onDutyIds = onDuty.Select(a => a.EmployeeId).ToHashSet();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.StoreId == storeId && e.IsActive)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

        return new KioskDto(
            employees.Select(e => new KioskEmployeeDto(
                e.Id, e.FullName, e.Position, onDutyIds.Contains(e.Id),
                IsLocked: e.PinLockedUntil > now)).ToList(),
            slots.Select(ToDto).ToList(),
            SuggestSlot(slots, now)?.Id,
            onDuty.Select(a => new OnDutyDto(
                a.Id, a.EmployeeId, a.EmployeeName, a.SlotName, a.SlotEndAt, a.CheckInAt, a.LateMinutes,
                HoldsDrawer: drawerHolder == a.EmployeeId)).ToList(),
            drawer);
    }

    public async Task<CheckInResultDto> CheckInAsync(Guid storeId, Guid deviceUserId, CheckInRequest req, CancellationToken ct = default)
    {
        var emp = await VerifyAsync(storeId, req, ct);

        var open = await _db.Attendances.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.CheckOutAt == null, ct);
        if (open is not null)
            throw new BusinessRuleException(
                $"{emp.FullName} đã chấm công vào {open.SlotName} lúc {ToVn(open.CheckInAt):HH:mm}. " +
                "Chấm công ra ca đó trước.");

        var slot = await _db.WorkSlots.AsNoTracking()
                       .FirstOrDefaultAsync(s => s.Id == req.SlotId && s.StoreId == storeId && s.IsActive, ct)
                   ?? throw new BusinessRuleException("Chọn ca làm việc.");

        var now = DateTime.UtcNow;
        var (start, end) = SlotWindow(slot, now);

        if (now < start.AddMinutes(-EarliestCheckInMinutes))
            throw new BusinessRuleException(
                $"Chưa tới giờ vào {slot.Name} ({ToVn(start):HH:mm}). Chỉ chấm công sớm tối đa {EarliestCheckInMinutes} phút.");
        if (now >= end)
            throw new BusinessRuleException($"{slot.Name} đã kết thúc lúc {ToVn(end):HH:mm}. Chọn ca đang diễn ra.");

        // Nhận két TRƯỚC khi ghi công: két lệch mà thiếu ghi chú thì cả lần
        // chấm công bị từ chối, nhân viên sửa rồi bấm lại — không để lại một
        // bản ghi công "đã vào" mà két thì chưa ai nhận.
        ShiftDto? drawer = null;
        if (req.TakeDrawer)
        {
            if (req.CountedCash is not int counted)
                throw new BusinessRuleException("Nhập số tiền đếm được trong két.");
            drawer = await _shifts.OpenForEmployeeAsync(storeId, deviceUserId, emp, counted, req.Note, ct);
        }

        var minutesFromStart = (int)Math.Floor((now - start).TotalMinutes);

        var attendance = new Attendance
        {
            StoreId      = storeId,
            EmployeeId   = emp.Id,
            EmployeeCode = emp.Code,
            EmployeeName = emp.FullName,
            WorkDate     = DateOnly.FromDateTime(ToVn(start)),
            SlotId       = slot.Id,
            SlotName     = slot.Name,
            SlotStartAt  = start,
            SlotEndAt    = end,
            CheckInAt    = now,
            LateMinutes  = Math.Max(0, minutesFromStart),
            HourlyWage   = emp.HourlyWage,
            CashShiftId  = drawer?.Id,
        };
        _db.Attendances.Add(attendance);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Bấm "Chấm công" hai lần liền: unique index chỉ cho một lần đang mở.
            throw new BusinessRuleException($"{emp.FullName} vừa chấm công vào rồi.");
        }

        return new CheckInResultDto(
            emp.FullName, slot.Name, start, end, now, minutesFromStart, attendance.LateMinutes, drawer);
    }

    public async Task<ShiftDto> TakeDrawerAsync(Guid storeId, Guid deviceUserId, TakeDrawerRequest req, CancellationToken ct = default)
    {
        var emp = await VerifyAsync(storeId, req, ct);

        var attendance = await _db.Attendances
            .FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.CheckOutAt == null, ct)
            ?? throw new BusinessRuleException("Chấm công vào ca trước rồi mới nhận két.");

        var drawer = await _shifts.OpenForEmployeeAsync(storeId, deviceUserId, emp, req.CountedCash, req.Note, ct);

        attendance.CashShiftId = drawer.Id;
        await _db.SaveChangesAsync(ct);
        return drawer;
    }

    public async Task<CheckOutResultDto> CheckOutAsync(Guid storeId, Guid deviceUserId, CheckOutRequest req, CancellationToken ct = default)
    {
        var emp = await VerifyAsync(storeId, req, ct);

        var a = await _db.Attendances
            .FirstOrDefaultAsync(x => x.EmployeeId == emp.Id && x.CheckOutAt == null, ct)
            ?? throw new BusinessRuleException($"{emp.FullName} chưa chấm công vào ca nào.");

        // Đang giữ két thì phải đếm và giao két trước khi về — không ai được
        // rời quán mà để két "không chủ".
        var holdsDrawer = await _db.CashShifts.AnyAsync(
            s => s.StoreId == storeId && s.Status == ShiftStatus.Open && s.OpenedByEmployeeId == emp.Id, ct);

        ShiftDto? drawer = null;
        if (holdsDrawer)
        {
            if (req.CountedCash is not int counted || req.HandoverCash is not int handover)
                throw new BusinessRuleException("Bạn đang giữ két — đếm két và nhập số tiền để lại cho ca sau.");

            drawer = await _shifts.CloseForEmployeeAsync(storeId, deviceUserId, emp, new CloseShiftRequest
            {
                CountedCash = counted,
                HandoverCash = handover,
                HandOverToEmployeeId = req.HandOverToEmployeeId,
                Note = req.Note,
            }, ct);
        }

        var now = DateTime.UtcNow;
        a.CheckOutAt        = now;
        a.EarlyLeaveMinutes = Math.Max(0, (int)Math.Floor((a.SlotEndAt - now).TotalMinutes));
        a.WorkedMinutes     = WorkedMinutes(a.CheckInAt, now, a.SlotStartAt, a.SlotEndAt);
        a.Pay               = PayFor(a.WorkedMinutes, a.HourlyWage);
        a.UpdatedAt         = now;

        await _db.SaveChangesAsync(ct);

        return new CheckOutResultDto(
            emp.FullName, a.SlotName, a.CheckInAt, now, a.LateMinutes, a.EarlyLeaveMinutes,
            a.WorkedMinutes, a.Pay, drawer);
    }

    // ==========================================================================
    //  QUẢN LÝ NHÂN SỰ
    // ==========================================================================

    public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid storeId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var list = await _db.Employees.AsNoTracking()
            .Where(e => e.StoreId == storeId)
            .OrderByDescending(e => e.IsActive).ThenBy(e => e.Code)
            .ToListAsync(ct);
        return list.Select(e => ToDto(e, now)).ToList();
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(Guid storeId, SaveEmployeeRequest req, CancellationToken ct = default)
    {
        Validate(req, isNew: true);

        var emp = new Employee
        {
            StoreId = storeId,
            Code    = await NextCodeAsync(storeId, ct),
            PinHash = BCrypt.Net.BCrypt.HashPassword(req.Pin!, workFactor: 10),
        };
        Apply(emp, req);

        _db.Employees.Add(emp);
        await _db.SaveChangesAsync(ct);
        return ToDto(emp, DateTime.UtcNow);
    }

    public async Task<EmployeeDto> UpdateEmployeeAsync(Guid storeId, Guid id, SaveEmployeeRequest req, CancellationToken ct = default)
    {
        Validate(req, isNew: false);

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.StoreId == storeId, ct)
                  ?? throw new BusinessRuleException("Không tìm thấy nhân viên.");

        Apply(emp, req);

        // Đặt PIN mới cũng là cách MỞ KHOÁ cho người lỡ gõ sai 5 lần.
        if (!string.IsNullOrWhiteSpace(req.Pin))
        {
            emp.PinHash = BCrypt.Net.BCrypt.HashPassword(req.Pin, workFactor: 10);
            emp.FailedPinAttempts = 0;
            emp.PinLockedUntil = null;
        }

        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(emp, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<WorkSlotDto>> GetSlotsAsync(Guid storeId, CancellationToken ct = default)
    {
        await EnsureDefaultSlotsAsync(storeId, ct);
        var slots = await _db.WorkSlots.AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.StartTime)
            .ToListAsync(ct);
        return slots.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<WorkSlotDto>> SaveSlotsAsync(
        Guid storeId, IReadOnlyList<SaveWorkSlotRequest> slots, CancellationToken ct = default)
    {
        if (slots.Count == 0) throw new BusinessRuleException("Cần ít nhất một khung ca.");

        var parsed = slots.Select(s =>
        {
            if (string.IsNullOrWhiteSpace(s.Name)) throw new BusinessRuleException("Khung ca phải có tên.");
            if (!TimeOnly.TryParseExact(s.Start, "HH:mm", out var start))
                throw new BusinessRuleException($"Giờ bắt đầu \"{s.Start}\" không hợp lệ (dạng HH:mm).");
            return (Req: s, Start: start);
        }).OrderBy(x => x.Start).ToList();

        var existing = await _db.WorkSlots.Where(s => s.StoreId == storeId).ToListAsync(ct);
        var keep = new HashSet<Guid>();

        for (var i = 0; i < parsed.Count; i++)
        {
            var (req, start) = parsed[i];
            var slot = req.Id is Guid id ? existing.FirstOrDefault(s => s.Id == id) : null;
            if (slot is null)
            {
                slot = new WorkSlot { StoreId = storeId };
                _db.WorkSlots.Add(slot);
            }
            slot.Name      = req.Name.Trim();
            slot.StartTime = start;
            slot.IsActive  = req.IsActive;
            slot.SortOrder = i;
            slot.UpdatedAt = DateTime.UtcNow;
            keep.Add(slot.Id);
        }

        // Khung ca bị bỏ khỏi danh sách: xoá mềm. Bảng công cũ đã chụp tên và
        // giờ ca nên không hỏng gì.
        foreach (var gone in existing.Where(s => !keep.Contains(s.Id)))
            gone.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return await GetSlotsAsync(storeId, ct);
    }

    public async Task<TimesheetDto> GetTimesheetAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        var rows = await _db.Attendances.AsNoTracking()
            .Where(a => a.StoreId == storeId && a.WorkDate >= from && a.WorkDate <= to)
            .OrderByDescending(a => a.CheckInAt)
            .ToListAsync(ct);

        var drawerIds = rows.Where(r => r.CashShiftId != null).Select(r => r.CashShiftId!.Value).ToList();
        var drawers = await _db.CashShifts.AsNoTracking()
            .Where(s => drawerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CashDifference, s.OpeningCash, s.ExpectedOpeningCash })
            .ToDictionaryAsync(s => s.Id, ct);

        var now = DateTime.UtcNow;
        var dtos = rows.Select(a =>
        {
            // Chưa chấm ra: còn trong ca thì tính tới hiện tại; hết ca rồi thì
            // tạm tính tới giờ kết thúc ca và gắn cờ để quản lý xem lại.
            var isOpen = a.CheckOutAt is null && now < a.SlotEndAt;
            var missing = a.CheckOutAt is null && now >= a.SlotEndAt;
            var worked = a.CheckOutAt is null
                ? WorkedMinutes(a.CheckInAt, missing ? a.SlotEndAt : now, a.SlotStartAt, a.SlotEndAt)
                : a.WorkedMinutes;
            var pay = a.CheckOutAt is null ? PayFor(worked, a.HourlyWage) : a.Pay;
            var drawer = a.CashShiftId is Guid sid && drawers.TryGetValue(sid, out var d) ? d : null;
            int? diff = drawer?.CashDifference;
            int? receive = drawer?.ExpectedOpeningCash is int exp ? drawer.OpeningCash - exp : null;

            return new AttendanceDto(
                a.Id, a.EmployeeId, a.EmployeeCode, a.EmployeeName, a.WorkDate.ToString("yyyy-MM-dd"),
                a.SlotName, a.SlotStartAt, a.SlotEndAt, a.CheckInAt, a.LateMinutes, a.CheckOutAt,
                a.EarlyLeaveMinutes, worked, a.HourlyWage, pay,
                HeldDrawer: a.CashShiftId is not null, DrawerDifference: diff, ReceiveDifference: receive,
                IsOpen: isOpen, MissingCheckout: missing);
        }).ToList();

        var summary = dtos.GroupBy(r => r.EmployeeId)
            .Select(g =>
            {
                var first = g.First();
                return new EmployeeTimesheetDto(
                    g.Key, first.EmployeeCode, first.EmployeeName, first.HourlyWage,
                    Shifts:            g.Count(),
                    LateCount:         g.Count(r => r.LateMinutes > 0),
                    LateMinutes:       g.Sum(r => r.LateMinutes),
                    EarlyLeaveMinutes: g.Sum(r => r.EarlyLeaveMinutes),
                    WorkedMinutes:     g.Sum(r => r.WorkedMinutes),
                    Pay:               g.Sum(r => r.Pay),
                    MissingCheckouts:  g.Count(r => r.MissingCheckout),
                    DrawerShifts:      g.Count(r => r.HeldDrawer),
                    DrawerShortage:    g.Where(r => r.DrawerDifference < 0).Sum(r => r.DrawerDifference ?? 0)
                                     + g.Where(r => r.ReceiveDifference < 0).Sum(r => r.ReceiveDifference ?? 0),
                    DrawerSurplus:     g.Where(r => r.DrawerDifference > 0).Sum(r => r.DrawerDifference ?? 0)
                                     + g.Where(r => r.ReceiveDifference > 0).Sum(r => r.ReceiveDifference ?? 0),
                    ReceiveShortage:   g.Where(r => r.ReceiveDifference < 0).Sum(r => r.ReceiveDifference ?? 0));
            })
            .OrderBy(s => s.Code)
            .ToList();

        return new TimesheetDto(
            summary, dtos,
            TotalPay:            dtos.Sum(r => r.Pay),
            TotalWorkedMinutes:  dtos.Sum(r => r.WorkedMinutes),
            TotalLateMinutes:    dtos.Sum(r => r.LateMinutes),
            TotalDrawerShortage: summary.Sum(s => s.DrawerShortage));
    }

    // ==========================================================================
    //  NỘI BỘ
    // ==========================================================================

    /// <summary>
    /// Xác minh "chọn tên + mã NV + PIN". Sai thì đếm, đủ 5 lần thì khoá 5 phút.
    /// Câu báo lỗi KHÔNG nói sai mã hay sai PIN — nói rõ thì người dò biết đã
    /// đúng được một nửa.
    /// </summary>
    private async Task<Employee> VerifyAsync(Guid storeId, EmployeeCredential cred, CancellationToken ct)
    {
        var emp = await _db.Employees
                      .FirstOrDefaultAsync(e => e.Id == cred.EmployeeId && e.StoreId == storeId && e.IsActive, ct)
                  ?? throw new BusinessRuleException("Chọn tên nhân viên.");

        var now = DateTime.UtcNow;
        if (emp.PinLockedUntil > now)
            throw new BusinessRuleException(
                $"Đã nhập sai quá {MaxPinAttempts} lần. Thử lại sau {ToVn(emp.PinLockedUntil.Value):HH:mm} hoặc nhờ quản lý đặt lại PIN.");

        var codeOk = string.Equals(cred.Code?.Trim(), emp.Code, StringComparison.OrdinalIgnoreCase);
        var pinOk = !string.IsNullOrEmpty(cred.Pin) && BCrypt.Net.BCrypt.Verify(cred.Pin, emp.PinHash);

        if (!codeOk || !pinOk)
        {
            emp.FailedPinAttempts++;
            var left = MaxPinAttempts - emp.FailedPinAttempts;
            if (left <= 0)
            {
                emp.PinLockedUntil = now + PinLockDuration;
                emp.FailedPinAttempts = 0;
            }
            await _db.SaveChangesAsync(ct);

            throw new BusinessRuleException(left > 0
                ? $"Mã nhân viên hoặc PIN không đúng. Còn {left} lần thử."
                : $"Sai {MaxPinAttempts} lần — tạm khoá {PinLockDuration.TotalMinutes:0} phút.");
        }

        if (emp.FailedPinAttempts != 0 || emp.PinLockedUntil != null)
        {
            emp.FailedPinAttempts = 0;
            emp.PinLockedUntil = null;
            await _db.SaveChangesAsync(ct);
        }
        return emp;
    }

    private async Task<List<WorkSlot>> ActiveSlotsAsync(Guid storeId, CancellationToken ct)
    {
        await EnsureDefaultSlotsAsync(storeId, ct);
        return await _db.WorkSlots.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.IsActive)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);
    }

    /// <summary>Quán chưa đặt khung ca thì tạo bốn ca mặc định — chấm công dùng được ngay.</summary>
    private async Task EnsureDefaultSlotsAsync(Guid storeId, CancellationToken ct)
    {
        if (await _db.WorkSlots.IgnoreQueryFilters().AnyAsync(s => s.StoreId == storeId, ct)) return;

        for (var i = 0; i < DefaultSlots.Length; i++)
            _db.WorkSlots.Add(new WorkSlot
            {
                StoreId = storeId, Name = DefaultSlots[i].Name, StartTime = DefaultSlots[i].Start, SortOrder = i,
            });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mốc bắt đầu/kết thúc (UTC) của khung ca TRONG NGÀY hiện tại theo giờ VN.
    /// Ca tối 22:00 mà quá nửa đêm mới chấm công vẫn tính về ca của hôm trước.
    /// </summary>
    private static (DateTime Start, DateTime End) SlotWindow(WorkSlot slot, DateTime nowUtc)
    {
        var vnNow = nowUtc + VnOffset;
        var startVn = vnNow.Date + slot.StartTime.ToTimeSpan();

        // Đang sau nửa đêm mà ca bắt đầu từ tối qua và chưa hết: lùi về hôm trước.
        if (startVn - vnNow > TimeSpan.FromHours(12)) startVn = startVn.AddDays(-1);

        var startUtc = DateTime.SpecifyKind(startVn - VnOffset, DateTimeKind.Utc);
        return (startUtc, startUtc.AddMinutes(WorkSlot.DurationMinutes));
    }

    /// <summary>
    /// Khung ca hợp nhất với giờ hiện tại: ca đang diễn ra, hoặc ca SẮP bắt đầu
    /// trong vòng 60 phút — 9:50 chấm công thì gợi ý ca 10:00, không phải ca 6:00
    /// sắp hết.
    /// </summary>
    private static WorkSlot? SuggestSlot(IReadOnlyList<WorkSlot> slots, DateTime nowUtc)
        => slots
            .Select(s => (Slot: s, Window: SlotWindow(s, nowUtc)))
            .Where(x => nowUtc >= x.Window.Start.AddMinutes(-EarliestCheckInMinutes) && nowUtc < x.Window.End)
            .OrderByDescending(x => x.Window.Start)
            .Select(x => x.Slot)
            .FirstOrDefault();

    private static int WorkedMinutes(DateTime checkIn, DateTime checkOut, DateTime slotStart, DateTime slotEnd)
    {
        var from = checkIn > slotStart ? checkIn : slotStart;
        var to = checkOut < slotEnd ? checkOut : slotEnd;
        return Math.Max(0, (int)Math.Floor((to - from).TotalMinutes));
    }

    private static int PayFor(int minutes, int hourlyWage)
        => (int)Math.Round(minutes * (decimal)hourlyWage / 60m, MidpointRounding.AwayFromZero);

    private async Task<string> NextCodeAsync(Guid storeId, CancellationToken ct)
    {
        // Tính cả nhân viên đã xoá: mã cũ không bao giờ được cấp lại cho người
        // khác, nếu không bảng công cũ sẽ chỉ nhầm người.
        var codes = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.StoreId == storeId)
            .Select(e => e.Code)
            .ToListAsync(ct);

        var max = codes
            .Select(c => int.TryParse(c.TrimStart('N', 'V', 'n', 'v'), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"NV{max + 1:000}";
    }

    private static void Validate(SaveEmployeeRequest req, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(req.FullName)) throw new BusinessRuleException("Nhập họ tên nhân viên.");
        if (req.HourlyWage < 0) throw new BusinessRuleException("Lương giờ không được âm.");
        if (isNew && string.IsNullOrWhiteSpace(req.Pin)) throw new BusinessRuleException("Đặt mã PIN chấm công cho nhân viên.");
        if (!string.IsNullOrWhiteSpace(req.Pin)
            && (req.Pin.Length is < 4 or > 6 || !req.Pin.All(char.IsDigit)))
            throw new BusinessRuleException("PIN gồm 4–6 chữ số.");
        if (!string.IsNullOrWhiteSpace(req.HiredOn) && !DateOnly.TryParse(req.HiredOn, out _))
            throw new BusinessRuleException("Ngày vào làm không hợp lệ.");
    }

    private static void Apply(Employee emp, SaveEmployeeRequest req)
    {
        emp.FullName   = req.FullName.Trim();
        emp.Phone      = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        emp.Position   = string.IsNullOrWhiteSpace(req.Position) ? "Nhân viên" : req.Position.Trim();
        emp.HourlyWage = req.HourlyWage;
        emp.IsActive   = req.IsActive;
        emp.HiredOn    = DateOnly.TryParse(req.HiredOn, out var d) ? d : null;
        emp.Note       = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
    }

    private static EmployeeDto ToDto(Employee e, DateTime now) => new(
        e.Id, e.Code, e.FullName, e.Phone, e.Position, e.HourlyWage, e.IsActive,
        e.HiredOn?.ToString("yyyy-MM-dd"), e.Note, IsLocked: e.PinLockedUntil > now);

    private static WorkSlotDto ToDto(WorkSlot s) => new(
        s.Id, s.Name, s.StartTime.ToString("HH:mm"),
        s.StartTime.AddMinutes(WorkSlot.DurationMinutes).ToString("HH:mm"), s.IsActive);

    private static DateTime ToVn(DateTime utc) => utc + VnOffset;
}
