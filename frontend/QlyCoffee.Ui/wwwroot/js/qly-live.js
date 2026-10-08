// =============================================================================
//  QLY LIVE — ỨNG DỤNG TỰ CẬP NHẬT, TỰ HỒI PHỤC, KHÔNG CẦN BẤM "TẢI LẠI"
// =============================================================================
//  Dùng chung cho trang bán hàng và trang quản lý. Nạp NGAY SAU thẻ
//  <div id="blazor-error-ui"> trong index.html (script cần thẻ đó đã tồn tại).
//
//  Bốn việc:
//
//  1. TỰ HỒI PHỤC KHI LỖI. Trước đây Blazor gặp lỗi chưa xử lý thì bật một dải
//     đỏ "Đã có lỗi xảy ra… Tải lại ×" và người dùng phải tự bấm. Giờ dải đó
//     không bao giờ hiện ra (CSS ẩn hẳn #blazor-error-ui); script canh lúc
//     Blazor bật nó lên và tự tải lại trang. Giới hạn 3 lần mỗi phút — lỗi
//     xảy ra ngay lúc khởi động thì tải lại cũng vô ích, không được để trang
//     nháy liên tục.
//
//  2. TỰ NHẬN BẢN MỚI. Mỗi 15 giây hỏi máy chủ mã phiên bản (ETag) của
//     _framework/blazor.boot.json — file này đổi mỗi lần build/deploy. Đổi
//     thì tải lại trang. Đây là thủ phạm của lỗi "Đã có lỗi xảy ra" khi đang
//     phát triển: build lại trong lúc tab đang mở, trình duyệt chạy lẫn file
//     cũ với file mới. Chỉ hỏi bằng HEAD — vài trăm byte, không tải nội dung.
//
//  3. TỰ THAY CSS. File CSS đổi thì nạp bản mới tại chỗ, KHÔNG tải lại trang —
//     giỏ hàng, ô đang gõ, vị trí cuộn đều giữ nguyên. Nạp xong bản mới mới gỡ
//     bản cũ nên không có khoảnh khắc trang mất kiểu.
//
//  4. BÁO TAB HIỆN LẠI cho component LiveRefresh (QlyCoffee.Ui) để nó lấy số
//     liệu mới ngay khi người dùng quay lại tab.
//
//  KHÔNG BAO GIỜ TẢI LẠI KHI ĐANG GÕ. Ô nhập đang có con trỏ thì chờ tới lúc
//  rời ô mới tải — mất nửa câu ghi chú đơn hàng còn khó chịu hơn thấy bản cũ
//  thêm vài giây. Giỏ hàng nằm trong localStorage nên tải lại không mất.
// =============================================================================
window.qlyLive = (function () {
    var CHECK_MS = 15000;
    var BOOT = '_framework/blazor.boot.json';
    var RELOAD_KEY = 'qly-auto-reload';

    var visibleListeners = [];
    var stamps = {};          // đường dẫn -> ETag / Last-Modified đã thấy
    var reloadQueued = false;

    // --- Tiện ích --------------------------------------------------------------

    function isTyping() {
        var el = document.activeElement;
        if (!el) return false;
        var tag = el.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
    }

    // Chạy fn khi người dùng không gõ; đang gõ thì đợi rời ô.
    function whenIdle(fn) {
        if (!isTyping()) { fn(); return; }
        var el = document.activeElement;
        el.addEventListener('blur', function () { setTimeout(function () { whenIdle(fn); }, 400); }, { once: true });
    }

    function reload(reason) {
        if (reloadQueued) return;
        try {
            var now = Date.now();
            var recent = JSON.parse(sessionStorage.getItem(RELOAD_KEY) || '[]')
                .filter(function (t) { return now - t < 60000; });
            if (recent.length >= 3) {
                console.warn('[qlyLive] Đã tự tải lại 3 lần trong 1 phút — dừng để tránh vòng lặp. Lý do:', reason);
                return;
            }
            recent.push(now);
            sessionStorage.setItem(RELOAD_KEY, JSON.stringify(recent));
        } catch (e) { /* sessionStorage bị chặn — vẫn tải lại */ }

        reloadQueued = true;
        console.info('[qlyLive] Tự tải lại:', reason);

        // Trang quản lý có service worker đệm khung ứng dụng: báo nó kiểm tra
        // bản mới TRƯỚC khi tải lại, nếu không lần tải lại vẫn ra bản cũ.
        var swUpdate = ('serviceWorker' in navigator)
            ? navigator.serviceWorker.getRegistration()
                .then(function (r) { return r && r.update(); })
                .catch(function () { })
            : Promise.resolve();

        swUpdate.then(function () { location.reload(); });
    }

    function stampOf(url) {
        return fetch(url, { method: 'HEAD', cache: 'no-store' })
            .then(function (r) { return r.ok ? (r.headers.get('ETag') || r.headers.get('Last-Modified')) : null; })
            .catch(function () { return null; });   // rớt mạng: bỏ qua lượt này
    }

    // --- 1. Tự hồi phục khi lỗi -----------------------------------------------

    var errorBox = document.getElementById('blazor-error-ui');
    if (errorBox) {
        new MutationObserver(function () {
            if (errorBox.style.display && errorBox.style.display !== 'none') {
                whenIdle(function () { reload('ứng dụng gặp lỗi'); });
            }
        }).observe(errorBox, { attributes: true, attributeFilter: ['style'] });
    }

    // --- 2 & 3. Bản mới, CSS mới ----------------------------------------------

    function localStylesheets() {
        return Array.prototype.filter.call(
            document.querySelectorAll('link[rel="stylesheet"][href]'),
            function (l) { return !/^(https?:)?\/\//.test(l.getAttribute('href')); });
    }

    function swapStylesheet(link, path) {
        var fresh = link.cloneNode();
        fresh.href = path + '?t=' + Date.now();
        fresh.onload = function () { link.remove(); };
        fresh.onerror = function () { fresh.remove(); };
        link.after(fresh);
        console.info('[qlyLive] Đã nạp CSS mới:', path);
    }

    function check() {
        if (document.visibilityState !== 'visible' || reloadQueued) return;

        stampOf(BOOT).then(function (s) {
            if (!s) return;
            if (stamps[BOOT] && stamps[BOOT] !== s) {
                whenIdle(function () { reload('có bản cập nhật mới'); });
                return;
            }
            stamps[BOOT] = s;
        });

        localStylesheets().forEach(function (link) {
            var path = link.getAttribute('href').split('?')[0];
            stampOf(path).then(function (s) {
                if (!s) return;
                if (stamps[path] && stamps[path] !== s && link.isConnected) swapStylesheet(link, path);
                stamps[path] = s;
            });
        });
    }

    // Lượt đầu chỉ ghi nhận mã phiên bản hiện tại, chưa so sánh gì.
    setTimeout(check, 3000);
    setInterval(check, CHECK_MS);

    // --- 4. Tab hiện lại --------------------------------------------------------

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState !== 'visible') return;
        check();
        visibleListeners.forEach(function (ref) {
            ref.invokeMethodAsync('OnTabVisible').catch(function () { /* component đã huỷ */ });
        });
    });

    function sameRef(a, b) { return a === b || (a && b && a._id !== undefined && a._id === b._id); }

    return {
        isHidden: function () { return document.visibilityState === 'hidden'; },
        onVisible: function (ref) { visibleListeners.push(ref); },
        offVisible: function (ref) {
            visibleListeners = visibleListeners.filter(function (r) { return !sameRef(r, ref); });
        }
    };
})();
