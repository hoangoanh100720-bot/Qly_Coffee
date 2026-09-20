// =============================================================================
//  SERVICE WORKER — BẢN PHÁT HÀNH
// =============================================================================
//  Giữ sẵn KHUNG ỨNG DỤNG trong máy để app mở được cả khi mạng chập chờn.
//
//  Quán dùng 4G hoặc wifi chia sẻ thì mất mạng vài giây là chuyện mỗi ngày.
//  Không có file này, đúng lúc đó nhân viên bấm mở app sẽ nhận trang khủng long
//  của trình duyệt. Có file này, app vẫn mở ra và tự báo mất kết nối tử tế.
//
//  RANH GIỚI QUAN TRỌNG NHẤT Ở ĐÂY: chỉ đệm KHUNG (HTML, CSS, WebAssembly,
//  icon), TUYỆT ĐỐI không đệm DỮ LIỆU. Đệm nhầm một lần gọi API thì màn hình
//  bán hàng sẽ hiện tồn kho của hôm qua, nhân viên bán món đã hết, và không ai
//  hiểu vì sao. Dữ liệu sai còn tệ hơn không có dữ liệu.
// =============================================================================

self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

// Tên đệm mang theo mã băm của toàn bộ tài nguyên (do MSBuild sinh ra trong
// service-worker-assets.js). Đẩy bản mới lên là mã băm đổi, tên đệm đổi theo,
// nên bản cũ bị dọn sạch — không bao giờ có chuyện lẫn lộn hai phiên bản.
const cacheNamePrefix = 'qly-admin-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;

// Mọi đường dẫn đều trả về index.html vì đây là ứng dụng một trang: /ban-hang
// không phải một file trên đĩa, nó là một route do Blazor xử lý sau khi khung
// ứng dụng đã chạy.
const offlineAssetsInclude = [
    /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/,
    /\.woff$/, /\.woff2$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/,
    /\.blat$/, /\.dat$/, /\.webmanifest$/
];
const offlineAssetsExclude = [
    /^service-worker\.js$/,
    // Cấu hình phải luôn lấy mới: đổi địa chỉ API trên máy chủ mà máy nhân viên
    // còn giữ bản cũ thì app gọi nhầm máy chủ và không ai đoán ra nguyên nhân.
    /^appsettings\.json$/
];

async function onInstall() {
    // skipWaiting: bản mới nhận việc ngay thay vì đợi người dùng đóng hết tab.
    // Với app nội bộ đây là lựa chọn đúng — nhân viên hiếm khi đóng hẳn app,
    // không có dòng này thì bản vá có thể nằm chờ nhiều ngày.
    self.skipWaiting();

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        // integrity + cache 'no-cache' để chắc chắn tải đúng bản vừa publish,
        // không nhận nhầm bản cũ còn nằm trong đệm HTTP của trình duyệt.
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate() {
    await self.clients.claim();

    // Dọn đệm của các bản trước.
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    // --- Những gì KHÔNG bao giờ đụng tới đệm --------------------------------

    // Chỉ GET mới có thể đệm. POST/PUT/DELETE là hành động thay đổi dữ liệu —
    // trả lại một bản lưu cho chúng thì nhân viên tưởng đã lưu mà thực ra chưa.
    if (event.request.method !== 'GET') {
        return fetch(event.request);
    }

    const url = new URL(event.request.url);

    // Khác origin = gọi sang backend (API, ảnh món, SignalR). Luôn đi thẳng ra
    // mạng. Đây là ranh giới giữ cho dữ liệu không bao giờ cũ.
    if (url.origin !== self.location.origin) {
        return fetch(event.request);
    }

    // Cùng origin nhưng là đường dẫn API — trường hợp backend đứng chung máy
    // chủ với app. Cũng đi thẳng ra mạng, vì lý do y hệt.
    if (url.pathname.startsWith('/api/')) {
        return fetch(event.request);
    }

    // --- Khung ứng dụng: đệm trước, mạng sau --------------------------------

    // Điều hướng trang (gõ địa chỉ, F5, mở từ icon trên màn hình chính) đều
    // được trả về index.html — khung ứng dụng. Blazor đọc đường dẫn thật rồi
    // tự dựng đúng màn hình.
    const shouldServeIndexHtml = event.request.mode === 'navigate';
    const cacheKey = shouldServeIndexHtml ? 'index.html' : event.request;

    const cache = await caches.open(cacheName);
    const cachedResponse = await cache.match(cacheKey);
    if (cachedResponse) {
        return cachedResponse;
    }

    // Không có trong đệm thì ra mạng. Mất mạng luôn thì để lỗi nổi lên đúng bản
    // chất của nó — app đã chạy sẽ hiện thông báo mất kết nối của riêng nó,
    // rõ ràng hơn bất cứ trang thay thế nào dựng ở tầng này.
    return fetch(event.request);
}
