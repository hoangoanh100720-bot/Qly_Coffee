using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Infrastructure.Seed;

// ==============================================================================
//  ĐỒNG BỘ LỊCH WORKSHOP
//
//  VÌ SAO KHÔNG ĐỂ TRONG DatabaseSeeder
//
//  Seeder chỉ chạy đúng một lần lúc database còn trống. Lịch workshop thì phải
//  luôn có buổi ở phía trước: seed 8 tuần vào ngày khai trương thì hai tháng sau
//  trang đặt lịch trống trơn, và không ai nhận ra cho tới khi khách phàn nàn.
//
//  File này chạy lại mỗi lần khởi động (khi bật cờ) và CHỈ BÙ phần còn thiếu:
//    · Buổi nào đã có trong khoảng thì để nguyên — kể cả khi chủ quán đã sửa
//      giờ, đổi chủ đề, hạ giá hay hủy nó. Ghi đè là xóa mất quyết định của
//      chủ quán, và tệ hơn: đổi giá của buổi mà khách đã đặt chỗ.
//    · Ưu đãi nào đã có mã thì để nguyên, chỉ thêm mã mới.
//
//  ⚠️ GIÁ VÀ MỨC ƯU ĐÃI BÊN DƯỚI LÀ SỐ GỢI Ý, KHÔNG PHẢI SỐ CỦA QUÁN.
//  Chủ quán phải rà lại trước khi mở bán thật. Sửa ở trang quản trị, KHÔNG sửa
//  file này — sửa ở đây không đổi được những buổi đã sinh ra rồi.
// ==============================================================================

public static class WorkshopSync
{
    public record Result(int NewSessions, int NewDiscounts, DateOnly CoveredTo);

    /// <summary>
    /// Bảo đảm lịch luôn có buổi trong <paramref name="weeksAhead"/> tuần tới, và
    /// bộ ưu đãi chuẩn đã có mặt.
    /// </summary>
    public static async Task<Result> ApplyAsync(
        AppDbContext db, Guid storeId, ILogger logger,
        int weeksAhead = 10, CancellationToken ct = default)
    {
        var newDiscounts = await EnsureDiscountsAsync(db, storeId, ct);
        var (newSessions, coveredTo) = await EnsureSessionsAsync(db, storeId, weeksAhead, ct);

        await db.SaveChangesAsync(ct);

        if (newSessions > 0 || newDiscounts > 0)
        {
            logger.LogInformation(
                "WorkshopSync: thêm {Sessions} buổi học và {Discounts} ưu đãi, lịch đã phủ tới {To:yyyy-MM-dd}",
                newSessions, newDiscounts, coveredTo);
        }

        return new Result(newSessions, newDiscounts, coveredTo);
    }

    // ==========================================================================
    //  ƯU ĐÃI
    // ==========================================================================

    /// <summary>
    /// Bộ ưu đãi chuẩn.
    /// <para>
    /// Hai nhóm tách bạch: <c>PerSeat</c> là thuộc tính của một NGƯỜI (khách tự
    /// khai, trình thẻ khi tới), <c>Booking</c> là thuộc tính của cả LƯỢT ĐẶT
    /// (hệ thống tự kiểm tra được, không cần giấy tờ gì).
    /// </para>
    /// </summary>
    private static readonly WorkshopDiscount[] StandardDiscounts =
    {
        new()
        {
            Code = "HSSV", Name = "Học sinh – sinh viên",
            Description = "Dành cho học sinh, sinh viên đang đi học. Giảm 30% giá một chỗ.",
            Scope = WorkshopDiscountScope.PerSeat, Percent = 30,
            RequiresProof = true,
            ProofNote = "Mang theo thẻ học sinh / sinh viên còn hạn khi tới quán.",
            SortOrder = 10
        },
        new()
        {
            Code = "U22", Name = "Dưới 22 tuổi",
            Description = "Dành cho bạn sinh từ năm thứ 22 trở lại đây, kể cả khi không còn đi học. Giảm 20%.",
            Scope = WorkshopDiscountScope.PerSeat, Percent = 20,
            RequiresProof = true,
            ProofNote = "Mang theo giấy tờ có ngày sinh (CCCD hoặc bằng lái) khi tới quán.",
            SortOrder = 20
        },
        new()
        {
            Code = "SINHNHAT", Name = "Sinh nhật trong tháng",
            Description = "Tháng sinh nhật của bạn trùng tháng diễn ra buổi học. Giảm 25%.",
            Scope = WorkshopDiscountScope.PerSeat, Percent = 25,
            RequiresProof = true,
            ProofNote = "Mang theo giấy tờ có ngày sinh khi tới quán.",
            SortOrder = 30
        },

        // --- Tự áp, không cần giấy tờ ----------------------------------------
        // Hai ưu đãi này hệ thống tự kiểm tra được từ chính lượt đặt, nên không
        // đặt RequiresProof: bắt khách mang giấy tờ chứng minh "tôi đi bốn
        // người" là vô nghĩa.
        new()
        {
            Code = "NHOM4", Name = "Nhóm từ 4 người",
            Description = "Đặt từ 4 chỗ trở lên trong cùng một lượt, giảm thêm 10% trên tổng.",
            Scope = WorkshopDiscountScope.Booking, Percent = 10, MinSeats = 4,
            SortOrder = 40
        },
        new()
        {
            Code = "DATSOM", Name = "Đặt sớm trước 7 ngày",
            Description = "Đặt trước ngày học ít nhất 7 ngày, giảm thêm 15% trên tổng.",
            Scope = WorkshopDiscountScope.Booking, Percent = 15, MinDaysAhead = 7,
            SortOrder = 50
        }
    };

    private static async Task<int> EnsureDiscountsAsync(
        AppDbContext db, Guid storeId, CancellationToken ct)
    {
        var existing = await db.WorkshopDiscounts
            .Where(d => d.StoreId == storeId)
            .Select(d => d.Code)
            .ToListAsync(ct);

        var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var src in StandardDiscounts)
        {
            if (have.Contains(src.Code)) continue;

            db.WorkshopDiscounts.Add(new WorkshopDiscount
            {
                StoreId = storeId,
                Code = src.Code,
                Name = src.Name,
                Description = src.Description,
                Scope = src.Scope,
                Percent = src.Percent,
                MinSeats = src.MinSeats,
                MinDaysAhead = src.MinDaysAhead,
                RequiresProof = src.RequiresProof,
                ProofNote = src.ProofNote,
                IsActive = true,
                SortOrder = src.SortOrder
            });
            added++;
        }

        return added;
    }

    // ==========================================================================
    //  LỊCH BUỔI HỌC
    // ==========================================================================

    /// <summary>
    /// Khuôn một khung giờ lặp hằng tuần.
    /// <para>
    /// Chủ đề xoay vòng theo tuần để người ghé lại tuần sau có cái mới để học —
    /// đúng như bài giới thiệu đã hứa "mỗi cuối tuần một chủ đề, đổi theo mùa".
    /// </para>
    /// </summary>
    private record SlotTemplate(
        DayOfWeek Day, TimeOnly Start, TimeOnly End, int Capacity, int Price, string[] Topics, string Summary);

    /// <summary>
    /// Khung giờ cố định hằng tuần.
    /// <para>
    /// GIÁ KHÁC NHAU THEO KHUNG GIỜ là cố ý: sáng thứ Bảy là giờ vàng nên giá
    /// nguyên; chiều thứ Sáu vắng khách nên hạ giá để lấp chỗ thay vì để trống
    /// cả buổi. Đây chính là thứ mà một mức giá chung không làm được.
    /// </para>
    /// </summary>
    private static readonly SlotTemplate[] WeeklySlots =
    {
        new(DayOfWeek.Friday, new TimeOnly(17, 30), new TimeOnly(19, 30), 8, 290_000,
            new[] { "Latte art căn bản", "Pha phin đúng cách", "Cold brew ủ lạnh" },
            "Buổi tan tầm, nhịp chậm. Học xong là vừa giờ ăn tối."),

        new(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(11, 30), 10, 390_000,
            new[] { "Matcha đánh tay", "Latte art căn bản", "Espresso và sữa", "Trà trái cây theo mùa" },
            "Buổi đông vui nhất tuần, nhóm đủ mười người và đủ mười bộ dụng cụ."),

        new(DayOfWeek.Saturday, new TimeOnly(14, 30), new TimeOnly(17, 0), 10, 350_000,
            new[] { "Cold brew ủ lạnh", "Trà trái cây theo mùa", "Matcha đánh tay", "Pha phin đúng cách" },
            "Buổi chiều thong thả, hợp với ai muốn hỏi nhiều."),

        new(DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(11, 30), 10, 390_000,
            new[] { "Espresso và sữa", "Matcha đánh tay", "Latte art căn bản", "Cold brew ủ lạnh" },
            "Sáng chủ nhật, buổi được đặt kín sớm nhất."),

        new(DayOfWeek.Sunday, new TimeOnly(15, 0), new TimeOnly(17, 0), 6, 320_000,
            new[] { "Pha phin đúng cách", "Trà trái cây theo mùa", "Latte art căn bản" },
            "Nhóm nhỏ sáu người, người pha có thời gian sửa tay cho từng bạn.")
    };

    private static async Task<(int Added, DateOnly CoveredTo)> EnsureSessionsAsync(
        AppDbContext db, Guid storeId, int weeksAhead, CancellationToken ct)
    {
        // Ngày theo giờ quán. Máy chủ chạy UTC thì DateTime.Today ở Việt Nam
        // buổi tối sẽ ra ngày hôm trước, và lịch thiếu mất một ngày.
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, StoreTimeZone()));

        var from = today;
        var to = today.AddDays(Math.Clamp(weeksAhead, 1, 52) * 7);

        // Lấy các buổi ĐÃ CÓ trong khoảng, kể cả buổi đã hủy: buổi chủ quán chủ
        // động hủy mà bị sinh lại tuần sau thì việc hủy thành vô nghĩa.
        var existing = await db.WorkshopSessions
            .IgnoreQueryFilters()
            .Where(s => s.StoreId == storeId && s.SessionDate >= from && s.SessionDate <= to)
            .Select(s => new { s.SessionDate, s.StartTime })
            .ToListAsync(ct);

        var have = existing.Select(e => (e.SessionDate, e.StartTime)).ToHashSet();
        var added = 0;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var tpl in WeeklySlots)
            {
                if (day.DayOfWeek != tpl.Day) continue;
                if (have.Contains((day, tpl.Start))) continue;

                db.WorkshopSessions.Add(new WorkshopSession
                {
                    StoreId = storeId,
                    Topic = PickTopic(tpl, day),
                    Summary = tpl.Summary,
                    SessionDate = day,
                    StartTime = tpl.Start,
                    EndTime = tpl.End,
                    Capacity = tpl.Capacity,
                    BasePrice = tpl.Price,
                    Status = WorkshopSessionStatus.Open
                });
                added++;
            }
        }

        return (added, to);
    }

    /// <summary>
    /// Chủ đề của một buổi, xoay vòng theo SỐ TUẦN TRONG NĂM.
    /// <para>
    /// Xoay theo tuần chứ không random: sinh lại lịch lần sau phải ra đúng chủ đề
    /// cũ cho cùng một ngày, nếu không hai máy chủ sẽ quảng cáo hai chủ đề khác
    /// nhau cho cùng một buổi.
    /// </para>
    /// </summary>
    private static string PickTopic(SlotTemplate tpl, DateOnly day)
    {
        var week = System.Globalization.ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));
        return tpl.Topics[week % tpl.Topics.Length];
    }

    private static TimeZoneInfo StoreTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "Giờ Việt Nam", "Giờ Việt Nam");
    }
}
