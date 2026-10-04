using System.Text.Json;

namespace QlyCoffee.Client.Services;

// ==============================================================================
//  DỮ LIỆU CÓ CẤU TRÚC (JSON-LD) CHO TRANG MÓN
// ==============================================================================
//
//  VÌ SAO CẦN, KHI ĐÃ CÓ og:image
//  Thẻ og:image chỉ nói "trang này có một tấm ảnh". Nó không nói ảnh đó là MÓN
//  GÌ, GIÁ BAO NHIÊU, CÒN BÁN KHÔNG. Lược đồ Product nói đủ ba điều đó, và đó
//  là điều kiện để ảnh món hiện kèm tên và giá trong Google Images — nơi khách
//  tìm đồ uống bằng mắt chứ không bằng chữ.
//
//  QUY TẮC QUAN TRỌNG NHẤT: JSON-LD PHẢI KHỚP VỚI NHỮNG GÌ KHÁCH THẤY.
//  Khai giá 45.000đ trong JSON-LD trong khi trang hiện 50.000đ là lý do Google
//  gỡ toàn bộ kết quả nâng cao của site, không riêng trang đó. Vì vậy mọi con số
//  ở đây lấy thẳng từ cùng dữ liệu mà giao diện đang hiển thị.
//
//  Ảnh phải là ĐƯỜNG DẪN TUYỆT ĐỐI. Đường dẫn tương đối thì Google bỏ qua.
// ==============================================================================

public static class SeoJson
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        // Không thoát tiếng Việt thành \uXXXX: JSON-LD nằm trong trang HTML
        // UTF-8, để nguyên chữ có dấu vừa nhẹ hơn vừa đọc được khi soi nguồn.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Lược đồ Product cho một món trên thực đơn.
    /// </summary>
    /// <param name="name">Tên món đúng như trên trang.</param>
    /// <param name="description">Mô tả ngắn, cùng câu với thẻ meta description.</param>
    /// <param name="absoluteImageUrl">Ảnh món, ĐƯỜNG DẪN TUYỆT ĐỐI.</param>
    /// <param name="absolutePageUrl">Địa chỉ trang món, đường dẫn tuyệt đối.</param>
    /// <param name="priceVnd">Giá đang bán, đồng. Phải khớp giá hiện trên trang.</param>
    /// <param name="inStock">Còn bán được hay tạm hết.</param>
    /// <param name="categoryName">Nhóm món — giúp Google xếp đúng chủ đề.</param>
    public static string Product(
        string name,
        string? description,
        string absoluteImageUrl,
        string absolutePageUrl,
        int priceVnd,
        bool inStock,
        string? categoryName)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"]    = "https://schema.org",
            ["@type"]       = "Product",
            ["name"]        = name,
            ["image"]       = new[] { absoluteImageUrl },
            ["description"] = description,
            ["category"]    = categoryName,
            ["brand"]       = new Dictionary<string, object?>
            {
                ["@type"] = "Brand",
                ["name"]  = "Một Chút Coffee"
            },
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"]         = "Offer",
                ["url"]           = absolutePageUrl,
                ["price"]         = priceVnd,
                ["priceCurrency"] = "VND",

                // Món hết nguyên liệu là hết THẬT, không phải ngừng bán vĩnh
                // viễn — OutOfStock đúng nghĩa hơn Discontinued, và Google sẽ
                // tự hiện lại khi mình báo còn hàng.
                ["availability"]  = inStock
                    ? "https://schema.org/InStock"
                    : "https://schema.org/OutOfStock",
                ["seller"] = new Dictionary<string, object?>
                {
                    ["@type"] = "CafeOrCoffeeShop",
                    ["name"]  = "Một Chút Coffee"
                }
            }
        };

        // Bỏ các khóa rỗng: JSON-LD có "description": null là lỗi cú pháp lược đồ
        foreach (var key in data.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            data.Remove(key);

        return JsonSerializer.Serialize(data, Opts);
    }
}
