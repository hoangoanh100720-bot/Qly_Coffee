using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace QlyCoffee.Client.Components.Visual;

// ==============================================================================
//  BỘ ICON GIAO DIỆN
//
//  Vì sao gom hết vào một file C# thay vì mỗi icon một file .razor:
//    - 24 file .razor cho 24 hình vẽ nhỏ làm rối cây thư mục.
//    - Dùng ComponentBase + AddMarkupContent cho ra cùng kết quả, biên dịch nhanh hơn.
//
//  QUY ƯỚC VẼ (giữ nguyên để bộ icon trông đồng bộ):
//    - Khung 24×24, nét 1.75, đầu và góc nét bo tròn.
//    - Dùng `currentColor` cho nét → icon tự đổi màu theo chữ xung quanh,
//      nên hoạt động đúng ở cả chế độ sáng và tối mà không cần viết thêm CSS.
//    - Chỉ vẽ nét, không tô đặc — hợp với phong cách nhẹ của cả giao diện.
//
//  LƯU Ý CÚ PHÁP: raw string literal nhiều dòng bắt buộc phải xuống dòng ngay
//  sau dấu mở và dấu đóng phải nằm riêng một dòng. Viết nội dung sát dấu mở rồi
//  xuống dòng sẽ không biên dịch được.
// ==============================================================================

/// <summary>Lớp cơ sở cho mọi icon giao diện.</summary>
public abstract class IconBase : ComponentBase
{
    /// <summary>Cạnh hình vuông chứa icon, tính bằng pixel.</summary>
    [Parameter] public int Size { get; set; } = 20;

    /// <summary>Lớp CSS bổ sung.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>
    /// Nhãn cho trình đọc màn hình. Để trống nghĩa là icon chỉ mang tính trang trí
    /// và sẽ được đánh dấu <c>aria-hidden</c> — đúng chuẩn khi cạnh nó đã có chữ.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Phần thân SVG của từng icon con.</summary>
    protected abstract string Body { get; }

    /// <summary>Nét đậm — ghi đè ở icon nào cần nét dày hơn.</summary>
    protected virtual string StrokeWidth => "1.75";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var aria = string.IsNullOrEmpty(AriaLabel)
            ? "aria-hidden=\"true\""
            : $"role=\"img\" aria-label=\"{AriaLabel}\"";

        builder.AddMarkupContent(0,
            $"<svg width=\"{Size}\" height=\"{Size}\" viewBox=\"0 0 24 24\" fill=\"none\" " +
            $"stroke=\"currentColor\" stroke-width=\"{StrokeWidth}\" " +
            $"stroke-linecap=\"round\" stroke-linejoin=\"round\" " +
            $"class=\"{CssClass}\" {aria} xmlns=\"http://www.w3.org/2000/svg\">{Body}</svg>");
    }
}

// ------------------------------------------------------------------------------
//  ĐIỀU HƯỚNG & KHUNG TRANG
// ------------------------------------------------------------------------------

/// <summary>Ba gạch ngang — nút mở menu trên màn hình hẹp.</summary>
public class MenuIcon : IconBase
{
    protected override string Body =>
        "<path d='M3 6h18M3 12h18M3 18h18'/>";
}

/// <summary>Lưới ô vuông — bảng điều khiển.</summary>
public class DashboardIcon : IconBase
{
    protected override string Body =>
        "<rect x='3' y='3' width='7' height='8' rx='1.5'/>" +
        "<rect x='14' y='3' width='7' height='5' rx='1.5'/>" +
        "<rect x='14' y='11' width='7' height='10' rx='1.5'/>" +
        "<rect x='3' y='14' width='7' height='7' rx='1.5'/>";
}

/// <summary>Mũi tên chỉ sang phải — đi tới trang chi tiết.</summary>
public class ArrowIcon : IconBase
{
    protected override string Body =>
        "<path d='M5 12h14M13 6l6 6-6 6'/>";
}

/// <summary>Mũi tên ra ngoài — mở liên kết ở trang khác.</summary>
public class ExternalIcon : IconBase
{
    protected override string Body =>
        "<path d='M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6'/>" +
        "<path d='M15 3h6v6'/><path d='M10 14 21 3'/>";
}

// ------------------------------------------------------------------------------
//  HÀNH ĐỘNG
// ------------------------------------------------------------------------------

public class PlusIcon : IconBase
{
    protected override string Body => "<path d='M12 5v14M5 12h14'/>";
}

public class CheckIcon : IconBase
{
    protected override string Body => "<path d='m4 12.5 5 5L20 6.5'/>";
    protected override string StrokeWidth => "2.25";
}

public class EditIcon : IconBase
{
    protected override string Body =>
        "<path d='M11 4H5a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h13a2 2 0 0 0 2-2v-6'/>" +
        "<path d='M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4Z'/>";
}

public class TrashIcon : IconBase
{
    protected override string Body =>
        "<path d='M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2'/>" +
        "<path d='M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6'/>" +
        "<path d='M10 11v6M14 11v6'/>";
}

public class RefreshIcon : IconBase
{
    protected override string Body =>
        "<path d='M21 12a9 9 0 1 1-3-6.7L21 8'/><path d='M21 3v5h-5'/>";
}

public class SearchIcon : IconBase
{
    protected override string Body =>
        "<circle cx='11' cy='11' r='7'/><path d='m20 20-3.5-3.5'/>";
}

/// <summary>Vòng xoay chờ. Animation nằm ở lớp .spin-path trong pages.css.</summary>
public class SpinnerIcon : IconBase
{
    protected override string Body =>
        "<path d='M12 3a9 9 0 1 0 9 9' class='spin-path'/>";
    protected override string StrokeWidth => "2.25";
}

// ------------------------------------------------------------------------------
//  NGHIỆP VỤ — ĐỒ UỐNG & KHO
// ------------------------------------------------------------------------------

/// <summary>Ly nước có ống hút — biểu tượng cho món và thực đơn.</summary>
public class CupIcon : IconBase
{
    protected override string Body =>
        "<path d='M5 8h14l-1.4 12.2A2 2 0 0 1 15.6 22H8.4a2 2 0 0 1-2-1.8L5 8Z'/>" +
        "<path d='M4 8c0-1.7 3.6-3 8-3s8 1.3 8 3'/><path d='M15 5V2'/>";
}

/// <summary>Hũ đựng — nguyên liệu.</summary>
public class JarIcon : IconBase
{
    protected override string Body =>
        "<rect x='5' y='8' width='14' height='13' rx='2'/>" +
        "<rect x='4' y='4' width='16' height='4' rx='1.5'/><path d='M9 13h6'/>";
}

/// <summary>Đồng hồ — hạn sử dụng, thời gian còn lại.</summary>
public class ClockIcon : IconBase
{
    protected override string Body =>
        "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3.5 2'/>";
}

/// <summary>Xe tải — nhập kho.</summary>
public class TruckIcon : IconBase
{
    protected override string Body =>
        "<path d='M2 7h11v10H2z'/><path d='M13 10h4.5l3.5 3.5V17h-8'/>" +
        "<circle cx='6' cy='18.5' r='1.8'/><circle cx='17' cy='18.5' r='1.8'/>";
}

/// <summary>Hóa đơn — đơn hàng.</summary>
public class ReceiptIcon : IconBase
{
    protected override string Body =>
        "<path d='M5 3h14v18l-2.5-1.5L14 21l-2-1.5L10 21l-2.5-1.5L5 21V3Z'/>" +
        "<path d='M9 8h6M9 12h6M9 16h3'/>";
}

/// <summary>Nhãn giá — khuyến mãi.</summary>
public class TagIcon : IconBase
{
    protected override string Body =>
        "<path d='M20.6 13.4 12 22l-9-9V4a1 1 0 0 1 1-1h9l7.6 7.6a2 2 0 0 1 0 2.8Z'/>" +
        "<circle cx='7.5' cy='7.5' r='1.4'/>";
}

public class FolderIcon : IconBase
{
    protected override string Body =>
        "<path d='M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z'/>";
}

public class CartIcon : IconBase
{
    protected override string Body =>
        "<circle cx='9' cy='20' r='1.6'/><circle cx='18' cy='20' r='1.6'/>" +
        "<path d='M2 3h3l2.6 12.4a2 2 0 0 0 2 1.6h7.9a2 2 0 0 0 2-1.5L21 8H6'/>";
}

// ------------------------------------------------------------------------------
//  DỮ LIỆU & AI
// ------------------------------------------------------------------------------

public class ChartIcon : IconBase
{
    protected override string Body =>
        "<path d='M3 3v18h18'/><path d='M7 15v3M12 10v8M17 6v12'/>";
}

/// <summary>Máy tính bỏ túi — nhấn mạnh "số này do hệ thống tính, không phải AI đoán".</summary>
public class CalcIcon : IconBase
{
    protected override string Body =>
        "<rect x='5' y='2' width='14' height='20' rx='2'/><path d='M9 6h6'/>" +
        "<path d='M9 11h.01M12 11h.01M15 11h.01M9 15h.01M12 15h.01M15 15h.01M9 19h6'/>";
}

/// <summary>Tia sáng — biểu tượng của AI Engine và bản kế hoạch.</summary>
public class SparkIcon : IconBase
{
    protected override string Body =>
        "<path d='M12 2.5 14 9l6.5 2-6.5 2-2 6.5-2-6.5L3.5 11 10 9l2-6.5Z'/>" +
        "<path d='M19 3v3M20.5 4.5h-3'/>";
}

// ------------------------------------------------------------------------------
//  TRẠNG THÁI
// ------------------------------------------------------------------------------

/// <summary>Tam giác cảnh báo.</summary>
public class WarnIcon : IconBase
{
    protected override string Body =>
        "<path d='M10.3 3.9 1.9 18a2 2 0 0 0 1.7 3h16.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z'/>" +
        "<path d='M12 9v4M12 17h.01'/>";
}

/// <summary>Chữ i trong vòng tròn — chú thích thêm.</summary>
public class InfoIcon : IconBase
{
    protected override string Body =>
        "<circle cx='12' cy='12' r='9'/><path d='M12 16v-5M12 8h.01'/>";
}

/// <summary>Mặt trời có tia — nút đổi giao diện sáng tối.</summary>
public class ThemeIcon : IconBase
{
    protected override string Body =>
        "<circle cx='12' cy='12' r='4.2'/>" +
        "<path d='M12 2v2M12 20v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M2 12h2M20 12h2" +
        "M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4'/>";
}

/// <summary>Máy ảnh — nút thêm/đổi ảnh chụp thật cho món.</summary>
public class CameraIcon : IconBase
{
    protected override string Body =>
        "<path d='M3 8.5A2 2 0 0 1 5 6.5h2.2l1.1-1.8a1.4 1.4 0 0 1 1.2-.7h4.9a1.4 1.4 0 0 1 1.2.7" +
        "l1.1 1.8H19a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z'/>" +
        "<circle cx='12' cy='13' r='3.4'/>";
}

/// <summary>Quả bóng đá — thẻ tỉ số trực tiếp.</summary>
public class BallIcon : IconBase
{
    protected override string Body =>
        "<circle cx='12' cy='12' r='9'/>" +
        "<path d='M12 8.2l3.6 2.6-1.4 4.2H9.8l-1.4-4.2Z'/>" +
        "<path d='M12 3v5.2M15.6 10.8l5-1.6M14.2 15l3.1 4.3M9.8 15l-3.1 4.3M8.4 10.8l-5-1.6'/>";
}
