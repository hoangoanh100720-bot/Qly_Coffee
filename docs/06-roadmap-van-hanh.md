# 06 — Roadmap & Vận hành

---

## 1. Roadmap 7 phase

Giả định đội ngũ khởi đầu: **1 fullstack lead + 1 fullstack + 1 ML engineer + 1 designer (bán thời gian)**. Thời lượng tính theo tuần làm việc thực tế.

---

### Phase 0 — Nền móng (2 tuần)

| Hạng mục | Chi tiết |
|---|---|
| Repo | Monorepo pnpm + Turborepo, ESLint/Prettier/TS strict, Husky + lint-staged |
| CI/CD | GitHub Actions: lint → test → build → preview deploy |
| Hạ tầng dev | `docker-compose.dev.yml`: Postgres+Timescale, Redis, MinIO, Mailhog |
| DB | Prisma schema v1, migration đầu tiên, seed data |
| Auth | JWT + refresh rotation, RBAC, OAuth Google |
| Multi-tenant | RLS policies + Prisma middleware + **tenant-isolation test** |
| Observability | Sentry, OpenTelemetry, structured logging |

**Định nghĩa hoàn thành:** Tạo được tenant → tạo store → đăng nhập → gọi API có RLS chặn đúng. Test isolation pass trong CI.

---

### Phase 1 — POS Core & MVP bán được (6 tuần)

| Tuần | Hạng mục |
|---|---|
| 1–2 | Quản lý menu (danh mục, món, biến thể, modifier), upload ảnh, thư viện công thức chuẩn 120 món VN |
| 2–3 | POS: giao diện bán hàng, giỏ hàng, thanh toán (tiền mặt + VietQR), in bill |
| 3–4 | **Offline-first**: IndexedDB, outbox pattern, sync engine, dải số hóa đơn |
| 4–5 | KDS: màn hình bar, nút Bắt đầu/Xong, realtime Socket.IO |
| 5 | Menu QR cho khách xem tại bàn |
| 6 | Báo cáo cơ bản: doanh thu, món bán chạy, theo ca. Xuất Excel |

**Định nghĩa hoàn thành:** Một quán thật bán hàng được cả ngày, kể cả khi rút mạng 30 phút.

> ⚠️ **Không được bỏ qua:** Đây là phần "table stakes". Nếu POS không tốt bằng KiotViet, không ai thử phần AI của ta.

---

### Phase 2 — Dữ liệu & ETA v1 (4 tuần)

| Hạng mục | Chi tiết |
|---|---|
| Event pipeline | `order_events` hypertable, ghi đầy đủ mốc thời gian |
| Cấu hình trạm | UI cho chủ quán khai báo trạm + gán món vào trạm (có preset theo loại quán) |
| **Queue Simulator** | SimPy engine, API `/v1/eta/predict` |
| ETA hiển thị | Trên POS (cho thu ngân đọc cho khách) + trang theo dõi đơn cho khách |
| Display policy | Làm mượt, ràng buộc đơn điệu, làm tròn |
| Dashboard chính xác | MAE, on-time rate, label coverage — hiển thị cho chính chủ quán |
| **Pilot 2 quán thật** | Đo `label_coverage` — nếu < 80%, dừng lại sửa UX KDS trước khi đi tiếp |

**Cổng quyết định (Go/No-Go):** `label_coverage ≥ 85%` và `simulator MAE < 150s`. Nếu không đạt, **không được sang Phase 3** — build ML trên nhãn rác là lãng phí.

---

### Phase 3 — ETA v2 (ML) & Smart Sequencing (5 tuần)

| Hạng mục | Chi tiết |
|---|---|
| Feature store | Pipeline ETL → Parquet trên S3, feature builder có test chống leakage |
| ML residual model | LightGBM quantile P50/P80/P95, backtest theo thời gian |
| MLOps | MLflow registry, job retrain hằng đêm, shadow mode, canary deploy |
| Global model | Gộp dữ liệu ẩn danh đa tenant, per-store calibration |
| **Smart Sequencing v1** | Heuristic + batch grouping, hiển thị gợi ý trên KDS |
| Nudge món nhanh | Gợi ý thay thế khi ETA > 10 phút |
| Arrival-time ordering | Đặt trước theo giờ đến, tính ngược thời điểm bắt đầu |

**Định nghĩa hoàn thành:** `MAE < 90s`, `on_time_rate ≥ 85%`, `eta_padding ≤ 90s` trên tập test của ít nhất 3 quán pilot.

---

### Phase 4 — Kho & Dự báo (4 tuần)

| Hạng mục | Chi tiết |
|---|---|
| Nguyên liệu & định mức | CRUD, BOM, tính giá vốn tự động |
| Tồn kho | Nhập/xuất/kiểm kê/điều chuyển, cảnh báo tồn tối thiểu |
| Dự báo nhu cầu | LightGBM multi-horizon 14 ngày, có lễ tết VN + thời tiết |
| Tự động đặt hàng | Sinh đơn nhập đề xuất theo lead time NCC |
| Phát hiện thất thoát | Variance lý thuyết vs thực tế, cảnh báo có ngữ cảnh |
| ClickHouse | CDC từ Postgres, dashboard analytics tốc độ cao |

---

### Phase 5 — Trí tuệ vận hành & Khách hàng (5 tuần)

| Hạng mục | Chi tiết |
|---|---|
| **Digital Twin** | UI kịch bản what-if, tính ROI, xuất báo cáo PDF |
| Xếp ca AI | OR-Tools CP-SAT, ràng buộc luật lao động VN, nguyện vọng nhân viên |
| Menu Engineering | Ma trận 4 ô + chiều tài nguyên, ước lượng độ co giãn giá |
| Customer 360 | RFM, churn prediction, gợi ý cá nhân hóa |
| Loyalty & Voucher | Tích điểm, hạng thành viên, chiến dịch tự động |
| **AI Ordering Agent** | Claude API + tool calling, kênh Zalo OA + Messenger + web chat |

---

### Phase 6 — SaaS hóa & Tăng trưởng (4 tuần)

| Hạng mục | Chi tiết |
|---|---|
| Billing | Đăng ký gói, thanh toán định kỳ, hóa đơn, quản lý hạn mức |
| Onboarding tự phục vụ | Wizard 5 bước, import từ KiotViet/iPOS/Excel, dữ liệu mẫu |
| **Website SEO** | Toàn bộ [tài liệu 05](05-seo-va-tang-truong.md) Phần A + C |
| Trang quán công khai | Mặt trận 2 SEO — programmatic pages + GMB sync |
| Tích hợp sàn | GrabFood, ShopeeFood, beFood — 2 chiều |
| Hóa đơn điện tử | Viettel/VNPT/MISA |
| API công khai + Webhook | Cho tenant Enterprise tích hợp |

---

### Phase 7+ — Mở rộng (liên tục)

- Quản lý chuỗi tập trung (so sánh chi nhánh, chuyển hàng nội bộ, báo cáo hợp nhất)
- Ứng dụng khách hàng (React Native) với đặt trước + ví
- Computer Vision
- Marketplace tích hợp bên thứ 3
- Mở rộng loại hình: trà sữa, bánh, nhà hàng nhỏ (mô hình trạm tổng quát hóa tốt)

---

## 2. Nguyên tắc ưu tiên khi phải cắt phạm vi

Khi trễ tiến độ, cắt theo thứ tự này (cắt từ dưới lên):

```
🔒 KHÔNG BAO GIỜ CẮT
   1. Offline-first POS
   2. Chất lượng thu thập nhãn (KDS UX)
   3. Tenant isolation & bảo mật
   4. Hóa đơn điện tử

⚠️  CẮT SAU CÙNG
   5. ETA prediction (lý do tồn tại của sản phẩm)
   6. Dashboard đo lường tác động

✂️  CẮT ĐƯỢC
   7. Smart sequencing → lùi phase
   8. Digital twin → lùi phase
   9. AI ordering agent → lùi phase
   10. Computer vision → bỏ
```

---

## 3. Ước tính chi phí hạ tầng

### Giai đoạn 1 — 0–50 quán

| Hạng mục | Nhà cung cấp | Chi phí/tháng |
|---|---|---|
| App servers (API + web) | Hetzner CPX41 ×2 | ~1.100.000đ |
| PostgreSQL managed | Neon / Supabase Pro | ~600.000đ |
| Redis | Upstash / self-host | ~250.000đ |
| Object storage | Cloudflare R2 (100GB) | ~130.000đ |
| CDN + WAF | Cloudflare Pro | ~600.000đ |
| AI service | Hetzner CPX31 | ~450.000đ |
| Monitoring | Grafana Cloud free + Sentry team | ~700.000đ |
| Claude API (ordering agent) | ~2.000 hội thoại | ~500.000đ |
| **Tổng** | | **~4.3 triệu/tháng** |

### Giai đoạn 2 — 500 quán

| Hạng mục | Chi phí/tháng |
|---|---|
| App servers (K8s, 6 node) | ~6.000.000đ |
| PostgreSQL (16GB RAM, replica) | ~4.500.000đ |
| ClickHouse | ~2.500.000đ |
| Redis cluster | ~1.200.000đ |
| Storage + CDN | ~2.000.000đ |
| AI service (2 node + GPU cho training) | ~4.000.000đ |
| Monitoring & tooling | ~3.000.000đ |
| Claude API | ~5.000.000đ |
| **Tổng** | **~28 triệu/tháng** |

Với 500 quán × ARPU 550k = **275 triệu doanh thu** → chi phí hạ tầng ~10%. Gross margin lành mạnh.

---

## 4. Sổ đăng ký rủi ro

| # | Rủi ro | Xác suất | Tác động | Giảm thiểu |
|---|---|:---:|:---:|---|
| R1 | **Barista không bấm nút KDS** → không có nhãn | Cao | Nghiêm trọng | Pilot sớm Phase 2, cổng Go/No-Go, gamification, suy luận nhãn, có thể dùng nút vật lý (Bluetooth button) nếu cần |
| R2 | Cấu hình trạm quá phức tạp, chủ quán không làm | Cao | Nghiêm trọng | Thư viện preset 120 món, wizard 3 bước, hệ thống tự học lại tham số, cho phép bỏ qua và dùng mặc định |
| R3 | ETA không chính xác đủ để dùng | Trung bình | Nghiêm trọng | Simulator-only làm sàn (đã tốt hơn baseline), hiển thị khoảng thay vì con số |
| R4 | Khách hàng không quan tâm ETA | Trung bình | Nghiêm trọng | A/B test sớm, đo tỷ lệ bỏ đơn; nếu sai, xoay trục sang giá trị nội bộ (throughput cho chủ quán) |
| R5 | Đối thủ lớn (KiotViet) copy | Trung bình | Trung bình | Flywheel dữ liệu là hào cạnh tranh; họ cần 2 năm thu thập; tăng tốc chiếm thị phần |
| R6 | Mất dữ liệu / sự cố tenant leak | Thấp | Thảm họa | RLS 3 lớp, test bắt buộc, backup PITR, diễn tập khôi phục hằng quý |
| R7 | Chi phí Claude API vượt dự toán | Trung bình | Nhẹ | Cache prompt, dùng Haiku cho tác vụ đơn giản, giới hạn hạn mức theo gói |
| R8 | Thay đổi quy định HĐĐT | Trung bình | Trung bình | Trừu tượng hóa provider, theo dõi thông tư Bộ Tài chính |
| R9 | Không tuyển được ML engineer | Trung bình | Trung bình | Phase 2 chỉ cần simulator (không cần ML); có thể thuê tư vấn ngắn hạn cho Phase 3 |
| R10 | Churn cao do onboarding khó | Cao | Nghiêm trọng | Onboarding có người hỗ trợ cho 100 khách đầu, import dữ liệu tự động, đo time-to-first-value |

---

## 5. Checklist tuân thủ Việt Nam

| Hạng mục | Văn bản | Trạng thái cần đạt |
|---|---|---|
| Hóa đơn điện tử | NĐ 123/2020/NĐ-CP, TT 78/2021/TT-BTC | Tích hợp ≥1 nhà cung cấp được cấp phép |
| Bảo vệ dữ liệu cá nhân | NĐ 13/2023/NĐ-CP | Cơ chế consent, quyền truy cập/xóa, nhật ký xử lý, hồ sơ đánh giá tác động |
| An toàn thông tin mạng | Luật ATTTM 2015 | Lưu trữ dữ liệu người dùng VN tại VN (khuyến nghị), có phương án ứng cứu sự cố |
| Thương mại điện tử | NĐ 52/2013 + NĐ 85/2021 | Thông báo website TMĐT với Bộ Công Thương |
| Thanh toán | Thông tư NHNN | Chỉ dùng cổng thanh toán được cấp phép, không lưu số thẻ |
| Điều khoản dịch vụ | — | Nêu rõ quyền sử dụng dữ liệu ẩn danh để cải thiện dịch vụ (nền tảng pháp lý cho flywheel) |
| Sở hữu trí tuệ | — | Đăng ký nhãn hiệu "Qly Coffee" nhóm 9 & 42 |

---

## 6. Chỉ số vận hành nội bộ (theo dõi hằng tuần)

| Nhóm | Chỉ số | Mục tiêu |
|---|---|---|
| **Sản phẩm** | Time-to-first-order (từ đăng ký đến đơn đầu tiên) | < 45 phút |
| | % quán cấu hình xong trạm trong 7 ngày | > 70% |
| | Label coverage trung bình | > 85% |
| | ETA MAE toàn hệ thống | < 90s |
| **Kỹ thuật** | API p95 latency | < 200ms |
| | ETA predict p95 latency | < 50ms |
| | Uptime | > 99.9% |
| | Sync lag p95 (offline) | < 30s |
| | Error rate | < 0.1% |
| **Kinh doanh** | MRR, NRR, churn | Xem [tài liệu 00](00-tong-quan-dinh-vi.md#33-đơn-vị-kinh-tế-mục-tiêu-year-2) |
| | NPS | > 40 |
| **SEO** | Xem [tài liệu 05 Phần D](05-seo-va-tang-truong.md#d1-bộ-chỉ-số-theo-dõi) | — |

---

## 7. Việc cần làm ngay (2 tuần tới)

Trước khi viết dòng code đầu tiên:

1. **Xác minh giả định R1 và R4** — ra 3 quán cà phê thật, quan sát 2 giờ giờ cao điểm, đếm: bao nhiêu khách hỏi "bao lâu nữa?", barista có sẵn sàng bấm nút không, những trạm nào thực sự nghẽn.
2. **Đo baseline thủ công** — bấm giờ 100 đơn ở 1 quán, tính phân phối thời gian pha chế thực tế. Đây là dữ liệu để hiệu chỉnh simulator ban đầu.
3. **Phỏng vấn 10 chủ quán** — hỏi họ đang dùng gì, đau ở đâu, sẵn sàng trả bao nhiêu, điều gì khiến họ đổi phần mềm.
4. **Xây thư viện công thức chuẩn** — 120 món cà phê/trà VN phổ biến với tham số trạm ước lượng ban đầu. Đây là tài sản, làm càng sớm càng tốt.
5. **Chốt tên miền + đăng ký nhãn hiệu.**
6. **Thiết lập Phase 0** — repo, CI, docker-compose.

> **Nguyên tắc:** Hai tuần điều tra thực địa tiết kiệm sáu tháng xây sai sản phẩm. Toàn bộ kế hoạch này dựa trên giả định barista sẽ bấm nút — hãy kiểm chứng điều đó trước.

---

**Quay lại:** [README](../README.md) · [00 — Tổng quan](00-tong-quan-dinh-vi.md)
