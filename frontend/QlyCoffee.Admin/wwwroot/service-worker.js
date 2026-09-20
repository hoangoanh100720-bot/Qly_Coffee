// =============================================================================
//  SERVICE WORKER — BẢN DÙNG LÚC PHÁT TRIỂN
// =============================================================================
//  Cố tình để RỖNG.
//
//  Service worker thật giữ tài nguyên trong bộ nhớ đệm và ưu tiên dùng bản đã
//  lưu. Lúc đang sửa code thì đó là cơn ác mộng: sửa xong, tải lại, vẫn thấy
//  bản cũ, và phải vào DevTools xóa đệm thủ công sau mỗi thay đổi.
//
//  File này được đăng ký khi chạy `dotnet run`. Lúc `dotnet publish`, MSBuild
//  thay nó bằng service-worker.published.js — xem thẻ <ServiceWorker> trong
//  QlyCoffee.Admin.csproj. Nghĩa là đệm offline chỉ bật ở bản phát hành, đúng
//  nơi nó có ích.
// =============================================================================

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', () => self.clients.claim());
