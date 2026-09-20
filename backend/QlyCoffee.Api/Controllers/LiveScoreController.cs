using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QlyCoffee.Api.LiveScore;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  BÓNG ĐÁ TRỰC TIẾP
//
//  Chỉ có MỘT endpoint đọc, vì phần "trực tiếp" đi qua SignalR (xem
//  LiveScore/LiveScoreFeed.cs). Endpoint này để trang vừa mở — hoặc vừa nối lại
//  sau khi rớt mạng — lấy trạng thái hiện tại, vì SignalR chỉ đẩy khi có THAY ĐỔI.
//
//  Công khai vì tỉ số là thông tin công khai, và nó chỉ đọc bộ nhớ đệm chứ không
//  gọi ra nguồn — gọi dồn dập cũng không tốn lượt gọi của quán.
// ==============================================================================

[Route("api/live-score")]
[AllowAnonymous]
public class LiveScoreController : BaseApiController
{
    private readonly LiveScoreStore _store;

    public LiveScoreController(LiveScoreStore store) => _store = store;

    [HttpGet]
    public IActionResult Get() => Ok(_store.Current);
}
