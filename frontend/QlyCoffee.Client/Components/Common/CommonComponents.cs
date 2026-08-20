using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace QlyCoffee.Client.Components.Common;

// ==============================================================================
//  COMPONENT NHỎ DÙNG CHUNG
//
//  Gom vào một file C# vì mỗi component chỉ vài dòng markup — tách ra thành
//  nhiều file .razor riêng chỉ làm rối cây thư mục mà không được lợi gì.
// ==============================================================================

/// <summary>
/// Logo của quán: ly cà phê có hơi bốc lên.
/// Dùng hai màu cố định (accent teal + amber) thay vì currentColor để logo
/// giữ nguyên nhận diện thương hiệu ở mọi vị trí, kể cả trên nền tối.
/// </summary>
public class LogoMark : ComponentBase
{
    [Parameter] public int Size { get; set; } = 32;
    [Parameter] public string? CssClass { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, $"""
            <svg width="{Size}" height="{Size}" viewBox="0 0 48 48" fill="none"
                 class="{CssClass}" role="img" aria-label="Qly Coffee"
                 xmlns="http://www.w3.org/2000/svg">
              <path d="M12 16h24l-3 22a4 4 0 0 1-4 3.5H19a4 4 0 0 1-4-3.5L12 16Z"
                    stroke="var(--accent)" stroke-width="2.5" stroke-linejoin="round"/>
              <path d="M36 20h4a5 5 0 0 1 0 10h-3"
                    stroke="var(--accent)" stroke-width="2.5" stroke-linecap="round"/>
              <path d="M20 10c2-3-2-5 0-8M28 10c2-3-2-5 0-8"
                    stroke="var(--warn)" stroke-width="2.5" stroke-linecap="round"/>
            </svg>
            """);
    }
}

/// <summary>
/// Dấu hỏi tròn nhỏ có tooltip. Dùng để giải thích các chỉ số khó hiểu
/// (VD: "lãi mất trên phần vốn đã bán được") mà không làm dài giao diện.
/// <para>
/// Dùng thuộc tính title gốc của trình duyệt thay vì tự dựng popup —
/// hoạt động sẵn với bàn phím và trình đọc màn hình, không cần thêm mã.
/// </para>
/// </summary>
public class InfoTip : ComponentBase
{
    [Parameter, EditorRequired] public string Text { get; set; } = "";
    [Parameter] public int Size { get; set; } = 14;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var safe = System.Net.WebUtility.HtmlEncode(Text);
        builder.AddMarkupContent(0, $"""
            <span class="info-tip" tabindex="0" role="note" title="{safe}" aria-label="{safe}">
              <svg width="{Size}" height="{Size}" viewBox="0 0 24 24" fill="none"
                   stroke="currentColor" stroke-width="1.9" aria-hidden="true">
                <circle cx="12" cy="12" r="9"/>
                <path d="M12 16v-5M12 8h.01" stroke-linecap="round"/>
              </svg>
            </span>
            """);
    }
}

/// <summary>
/// Khung xương cho trang kế hoạch AI.
/// <para>
/// Dùng khung xương thay vì vòng xoay vì đã biết trước hình dạng nội dung:
/// bốn thẻ số liệu, một khối tóm tắt, ba thẻ đề xuất. Trang không bị nhảy
/// khi dữ liệu thật về.
/// </para>
/// </summary>
public class PlanSkeleton : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, """
            <div class="stack stack-8" aria-busy="true" aria-label="Đang tải kế hoạch">
              <div class="stack stack-2">
                <div class="skeleton skeleton-title" style="width: 280px;"></div>
                <div class="skeleton skeleton-text" style="width: 180px;"></div>
              </div>

              <div class="grid grid-stats">
                <div class="skeleton" style="height: 92px; border-radius: var(--radius);"></div>
                <div class="skeleton" style="height: 92px; border-radius: var(--radius);"></div>
                <div class="skeleton" style="height: 92px; border-radius: var(--radius);"></div>
                <div class="skeleton" style="height: 92px; border-radius: var(--radius);"></div>
              </div>

              <div class="skeleton" style="height: 140px; border-radius: var(--radius-lg);"></div>
              <div class="skeleton" style="height: 180px; border-radius: var(--radius-lg);"></div>
              <div class="skeleton" style="height: 220px; border-radius: var(--radius-lg);"></div>
            </div>
            """);
    }
}

/// <summary>
/// Chuyển hướng tới trang đăng nhập, mang theo đường dẫn hiện tại để sau khi
/// đăng nhập xong quay lại đúng chỗ đang xem dở.
/// </summary>
public class RedirectToLogin : ComponentBase
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(
            Nav.ToBaseRelativePath(Nav.Uri));
        Nav.NavigateTo($"/dang-nhap?returnUrl={returnUrl}", forceLoad: false);
    }
}
