using System.Globalization;
using System.Text;

namespace QlyCoffee.Client.Services;

/// <summary>
/// Ảnh chụp thật của từng topping, tra theo TÊN topping.
/// <para>
/// Trước đây topping chỉ có một chấm màu cạnh tên. Khách lần đầu gặp "Củ năng"
/// hay "Sương sáo" không hình dung được nó là gì — nhìn ảnh thì biết ngay, và
/// topping có ảnh được chọn nhiều hơn hẳn topping chỉ có chữ.
/// </para>
/// <para>
/// TRA THEO TÊN, KHÔNG THEO ID: topping là dữ liệu trong database, quán thêm/sửa
/// được. Tên được bỏ dấu và viết thường trước khi so, nên "Trân châu đen",
/// "tran chau den" hay "Trân Châu Đen (size L)" đều ra cùng một ảnh. Topping
/// mới không khớp từ khoá nào thì <see cref="For"/> trả null và giao diện quay
/// về chấm màu như cũ — không bao giờ hiện ảnh sai món.
/// </para>
/// <para>
/// Nguồn ảnh và giấy phép: <c>QlyCoffee.Client/wwwroot/img/NGUON-ANH.md</c>.
/// Thay ảnh: ghi đè file cùng tên rồi tăng <see cref="Version"/> — không cần sửa gì khác.
/// </para>
/// </summary>
public static class ToppingPhoto
{
    private const string Dir = "_content/QlyCoffee.Ui/img/";

    /// <summary>
    /// Đổi mỗi khi THAY ẢNH mà giữ nguyên tên file — không đổi thì trình duyệt
    /// đã lưu ảnh cũ sẽ tiếp tục hiện ảnh cũ (đã xảy ra với trân châu trắng và
    /// củ năng lần đầu chọn sai ảnh).
    /// </summary>
    private const string Version = "?v=3";

    // Thứ tự QUAN TRỌNG: từ khoá dài/cụ thể đứng trước. "thach ca phe" phải được
    // xét trước "ca phe" (nếu sau này có), "tran chau trang" trước "tran chau".
    private static readonly (string Keyword, string File)[] Map =
    {
        ("tran chau trang", "topping-tran-chau-trang.webp"),
        ("tran chau",       "topping-tran-chau-den.webp"),
        ("thach dua",       "topping-thach-dua.webp"),
        ("nha dam",         "topping-thach-nha-dam.webp"),
        ("pudding",         "topping-pudding-trung.webp"),
        ("flan",            "topping-pudding-trung.webp"),
        ("kem cheese",      "topping-kem-cheese.webp"),
        ("kem pho mai",     "topping-kem-cheese.webp"),
        ("banh quy",        "topping-banh-quy-nghien.webp"),
        ("oreo",            "topping-banh-quy-nghien.webp"),
        ("hat no",          "topping-hat-no.webp"),
        ("cu nang",         "topping-cu-nang.webp"),
        ("suong sao",       "topping-suong-sao.webp"),
        ("thach ca phe",    "topping-thach-ca-phe.webp"),
        ("thach trai cay",  "topping-thach-trai-cay.webp"),
    };

    /// <summary>Đường dẫn ảnh của topping, hoặc null nếu chưa có ảnh cho tên này.</summary>
    public static string? For(string? toppingName)
    {
        if (string.IsNullOrWhiteSpace(toppingName)) return null;
        var key = Normalize(toppingName);
        foreach (var (keyword, file) in Map)
            if (key.Contains(keyword, StringComparison.Ordinal)) return Dir + file + Version;
        return null;
    }

    /// <summary>Bỏ dấu tiếng Việt, đổi đ → d, viết thường, gộp khoảng trắng.</summary>
    private static string Normalize(string s)
    {
        var decomposed = s.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC)
                                  .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
