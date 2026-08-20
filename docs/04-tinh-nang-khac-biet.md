# 04 — Tính năng khác biệt

Mỗi tính năng được chấm theo 3 trục: **Giá trị** (cho khách hàng) · **Độ khó** (kỹ thuật) · **Khả năng sao chép** (đối thủ bắt chước dễ không).

---

## Bảng tổng hợp

| # | Tính năng | Giá trị | Độ khó | Khó sao chép | Phase |
|---|---|:---:|:---:|:---:|:---:|
| 1 | Prep-Time Intelligence (ETA) | ★★★★★ | ★★★★☆ | ★★★★★ | 2–3 |
| 2 | Smart Queue Sequencing | ★★★★★ | ★★★★★ | ★★★★★ | 3 |
| 3 | Nudge món nhanh hơn | ★★★★☆ | ★★☆☆☆ | ★★★☆☆ | 3 |
| 4 | Arrival-time ordering (đặt trước theo giờ đến) | ★★★★★ | ★★★☆☆ | ★★★☆☆ | 3 |
| 5 | Digital Twin — mô phỏng what-if | ★★★★★ | ★★★☆☆ | ★★★★★ | 5 |
| 6 | Dự báo nhu cầu + tự động đặt hàng | ★★★★☆ | ★★★☆☆ | ★★★☆☆ | 4 |
| 7 | Phát hiện thất thoát bất thường | ★★★★☆ | ★★☆☆☆ | ★★★☆☆ | 4 |
| 8 | Xếp ca AI | ★★★★☆ | ★★★★☆ | ★★★☆☆ | 5 |
| 9 | AI Ordering Agent (Zalo/Messenger) | ★★★★☆ | ★★★☆☆ | ★★☆☆☆ | 5 |
| 10 | Menu Engineering + độ co giãn giá | ★★★★☆ | ★★★☆☆ | ★★★☆☆ | 5 |
| 11 | Customer 360 + dự đoán churn | ★★★☆☆ | ★★☆☆☆ | ★★☆☆☆ | 5 |
| 12 | Offline-first POS | ★★★★★ | ★★★★☆ | ★★☆☆☆ | 1 |
| 13 | Tuân thủ VN trọn gói (HĐĐT, sàn, ví) | ★★★★★ | ★★★☆☆ | ★☆☆☆☆ | 1–2 |
| 14 | Computer Vision (đếm hàng đợi, bàn trống) | ★★★☆☆ | ★★★★★ | ★★★★☆ | 6+ |

---

## 1–2. Prep-Time Intelligence & Smart Sequencing

Chi tiết đầy đủ ở [tài liệu 03](03-ai-du-doan-thoi-gian-cho.md). Đây là lý do tồn tại của sản phẩm.

---

## 3. Nudge món nhanh hơn — biến thời gian chờ thành doanh thu

Khi hệ thống phát hiện đơn hàng đang định gọi sẽ mất > 10 phút:

```
┌──────────────────────────────────────────────────┐
│  🕐 Sinh tố xoài của bạn sẽ mất khoảng 12 phút   │
│     (máy xay đang bận)                            │
│                                                   │
│  Gợi ý sẵn sàng nhanh hơn:                        │
│  ┌─────────────────────┬──────────────────────┐  │
│  │ Trà đào cam sả       │  ⚡ 3 phút · 45.000đ │  │
│  │ Cà phê sữa đá        │  ⚡ 2 phút · 30.000đ │  │
│  └─────────────────────┴──────────────────────┘  │
└──────────────────────────────────────────────────┘
```

**Vì sao lợi cả đôi:**
- Khách: không phải chờ lâu, hoặc chờ có ý thức
- Quán: giảm tải trạm nghẽn, tăng throughput, giảm bỏ đơn

**Thuật toán chọn món gợi ý:** ưu tiên món có (a) ETA thấp nhất, (b) **không dùng trạm nghẽn hiện tại**, (c) biên lợi nhuận ≥ món gốc, (d) phù hợp lịch sử của khách nếu đã nhận diện được.

**Đo lường:** tỷ lệ chấp nhận gợi ý, thay đổi AOV, thay đổi tỷ lệ bỏ đơn.

---

## 4. Arrival-time ordering — "đến là có, cà phê còn nóng"

Khách chọn **giờ muốn nhận**, không phải "đặt rồi chờ".

```
Khách chọn: "Tôi đến lúc 8:15"
                    ↓
Hệ thống tính ngược:  8:15 − prep_time − buffer = 8:07 (giờ bắt đầu pha)
                    ↓
Đơn "ngủ" trong hàng đợi, hiện trên KDS lúc 8:05 với nhãn "Bắt đầu lúc 8:07"
                    ↓
Geofencing (tùy chọn): khách vào bán kính 500m → kích hoạt sớm/muộn tự động
```

**Xử lý bất định:** Nếu khách đến sớm → đơn được đẩy lên ưu tiên. Nếu khách đến muộn > 10 phút → thông báo và hỏi có làm lại không.

Starbucks có tính năng này ở Mỹ. **Không có phần mềm nào tại Việt Nam có.** Rào cản kỹ thuật thấp nhưng cần chính xác ETA — mà ta có.

---

## 5. Digital Twin — mô phỏng what-if ⭐ (vũ khí bán hàng)

Chạy lại lịch sử thật của quán với tham số thay đổi:

```
┌── MÔ PHỎNG: Thứ 7, 7:00–10:00 (dữ liệu thật 4 tuần gần nhất) ──┐
│                                                                  │
│  Kịch bản A — Hiện tại                                          │
│    2 barista · 1 máy xay · thời gian chờ TB 6.2 phút            │
│    Throughput 47 đơn/giờ · 8 đơn bị bỏ                          │
│                                                                  │
│  Kịch bản B — Thêm 1 barista ca sáng                            │
│    Thời gian chờ TB 4.1 phút  (−34%)                            │
│    Throughput 58 đơn/giờ  (+23%)  ·  2 đơn bị bỏ                │
│    Chi phí thêm: 1.8tr/tháng · Doanh thu thêm: 6.4tr/tháng      │
│    ✅ ROI: +4.6tr/tháng — hoàn vốn ngay tháng đầu                │
│                                                                  │
│  Kịch bản C — Mua thêm 1 máy xay đá (8tr)                       │
│    Thời gian chờ TB 4.8 phút  (−23%)                            │
│    ✅ ROI: hoàn vốn sau 2.4 tháng                                │
│                                                                  │
│  Kịch bản D — Bỏ 3 món chậm nhất khỏi menu giờ cao điểm         │
│    Thời gian chờ TB 5.0 phút · Mất doanh thu 1.2tr/tháng        │
│    ❌ Không khuyến nghị                                          │
└──────────────────────────────────────────────────────────────────┘
```

**Vì sao đây là vũ khí bán hàng:** Trong buổi demo, ta lấy dữ liệu 2 tuần của chính quán đó và cho họ thấy con số ROI cụ thể. Không đối thủ nào làm được vì họ không có simulator.

**Kịch bản hỗ trợ:** thêm/bớt nhân sự, thêm thiết bị, đổi layout trạm, bỏ/thêm món, đổi giờ mở cửa, chạy khuyến mãi (tăng traffic X%).

---

## 6. Dự báo nhu cầu + tự động đặt hàng

```
Lịch sử bán hàng (theo món, theo giờ)
  + lịch nghỉ lễ VN + thời tiết + sự kiện
                ↓
    LightGBM / Prophet multi-horizon
                ↓
   Dự báo số lượng bán mỗi món, 14 ngày tới
                ↓
   × Định mức nguyên liệu (recipes)
                ↓
   Nhu cầu nguyên liệu − tồn kho hiện tại − đang về
                ↓
   ┌────────────────────────────────────────────┐
   │ ⚠️  Sữa tươi: đủ 2.3 ngày                  │
   │     Lead time NCC: 2 ngày → ĐẶT HÔM NAY   │
   │     Đề xuất: 45 lít  [Tạo đơn nhập]        │
   └────────────────────────────────────────────┘
```

Có tính đến: hạn sử dụng (`shelf_life_days`), giá trị đơn tối thiểu của NCC, và gộp đơn nhiều nguyên liệu cùng NCC.

---

## 7. Phát hiện thất thoát bất thường

```
Tiêu hao lý thuyết (từ đơn bán × định mức)
    vs
Tiêu hao thực tế (từ kiểm kê)
    ↓
Variance > ngưỡng động → cảnh báo có ngữ cảnh
```

```
🔴 Cảnh báo — Chi nhánh Quận 3, tuần 32
   Cà phê hạt: lý thuyết 12.4kg · thực tế 15.1kg
   Chênh lệch: +2.7kg (+21.8%) ≈ 810.000đ

   Phân tích:
   • Chênh lệch tập trung ca chiều T3, T5 (ca của N.V.A)
   • Không tương quan với số đơn hủy
   • So sánh: chi nhánh khác chênh lệch trung bình 4.2%

   Nguyên nhân có thể: đong sai định mức · lãng phí khi pha hỏng ·
   thất thoát · định mức trong hệ thống sai
```

**Nguyên tắc đạo đức:** Hệ thống **không kết luận ai gian lận**. Nó nêu dữ kiện và các khả năng. Quyết định thuộc về con người.

---

## 8. Xếp ca AI

Bài toán tối ưu ràng buộc (Google OR-Tools CP-SAT):

```
Mục tiêu:  min (chi phí nhân sự) + λ · (thời gian chờ dự kiến)

Ràng buộc:
  ✓ Đủ người theo dự báo lượng khách từng khung giờ
  ✓ Mỗi trạm có ít nhất 1 người đủ kỹ năng (staff_skills)
  ✓ Luật lao động: nghỉ giữa ca, tối đa giờ/tuần
  ✓ Nguyện vọng nhân viên (ngày nghỉ mong muốn) — soft constraint
  ✓ Công bằng: phân bổ ca đêm/cuối tuần đều
  ✓ Ổn định: hạn chế đổi lịch phút chót
```

Đầu ra: lịch ca đề xuất + **giải thích** ("Ca sáng T7 cần 3 người vì dự báo 62 đơn/giờ, vượt ngưỡng 2 người").

---

## 9. AI Ordering Agent — đặt món bằng tiếng Việt tự nhiên

Khách nhắn Zalo OA / Messenger / Web chat:

> *"cho anh 2 bạc xỉu ít đường với 1 bánh mì trứng, khoảng 8h anh qua lấy nhé"*

Agent hiểu và tạo đơn hoàn chỉnh, bao gồm cả `requested_ready_at = 8:00`.

### Kiến trúc

Dùng **Claude API** với tool calling. Model mặc định: `claude-opus-5` cho agent chính (chất lượng hiểu tiếng Việt và xử lý mơ hồ tốt nhất), `claude-haiku-4-5` cho các tác vụ phân loại đơn giản.

| Model | Giá input / output (per 1M tokens) | Dùng cho |
|---|---|---|
| `claude-opus-5` | $5.00 / $25.00 | Agent đặt món chính, xử lý hội thoại phức tạp |
| `claude-haiku-4-5` | $1.00 / $5.00 | Phân loại ý định, trích xuất SĐT, kiểm duyệt |

```python
# apps/ai-service/src/agents/ordering_agent.py
import anthropic
from anthropic import beta_tool

client = anthropic.Anthropic()

@beta_tool
def search_menu(query: str, store_id: str) -> str:
    """Tìm món trong menu theo tên hoặc mô tả tiếng Việt.

    Args:
        query: Tên món khách nói, có thể viết tắt hoặc sai chính tả.
        store_id: ID chi nhánh.
    """
    return menu_service.fuzzy_search(query, store_id)

@beta_tool
def get_eta(store_id: str, items_json: str) -> str:
    """Lấy thời gian dự kiến hoàn thành cho giỏ hàng hiện tại."""
    return eta_service.predict(store_id, json.loads(items_json))

@beta_tool
def create_draft_order(store_id: str, customer_phone: str,
                       items_json: str, requested_ready_at: str | None) -> str:
    """Tạo đơn nháp. Luôn hỏi xác nhận khách TRƯỚC khi gọi hàm này."""
    return order_service.create_draft(...)

runner = client.beta.messages.tool_runner(
    model="claude-opus-5",
    max_tokens=8000,
    thinking={"type": "adaptive"},
    output_config={"effort": "medium"},
    system=SYSTEM_PROMPT,          # xem dưới
    tools=[search_menu, get_eta, create_draft_order],
    messages=conversation_history,
)
```

### System prompt (rút gọn)

```
Bạn là trợ lý đặt món của {tên quán}. Trả lời bằng tiếng Việt, giọng thân thiện
và ngắn gọn như nhân viên quầy.

Quy trình:
1. Tìm món khách nói bằng search_menu. Người Việt hay viết tắt ("bs" = bạc xỉu,
   "cf sữa" = cà phê sữa) và sai chính tả — hãy đoán hợp lý.
2. Nếu món không rõ hoặc có nhiều lựa chọn (size, nóng/đá), hỏi lại một câu ngắn.
3. Ghi nhận tùy chọn: ít đường, nhiều đá, ít sữa...
4. Lấy ETA bằng get_eta và cho khách biết.
5. Đọc lại toàn bộ đơn + tổng tiền, chờ khách xác nhận.
6. Chỉ gọi create_draft_order SAU khi khách đã xác nhận rõ ràng.

Không bao giờ tự bịa món không có trong menu. Không tự ý áp dụng khuyến mãi.
Nếu khách hỏi ngoài phạm vi đặt món, chuyển tiếp cho nhân viên.
```

**Chi phí ước tính:** ~1.500 token/hội thoại → khoảng **200–300đ/đơn**. Chấp nhận được với AOV 60–80k.

**Rào cản an toàn:** Luôn có bước xác nhận trước khi tạo đơn. Đơn từ agent vào trạng thái `draft`, thu ngân duyệt (hoặc tự động duyệt sau khi khách xác nhận, tùy cấu hình).

---

## 10. Menu Engineering + độ co giãn giá

### 10.1 Ma trận kinh điển (nhưng có dữ liệu thật)

```
                    Biên lợi nhuận
                 Thấp        │        Cao
              ┌──────────────┼──────────────┐
        Cao   │  PLOWHORSE   │    STAR      │
   Độ         │  Tăng giá /  │  Đẩy mạnh    │
   phổ        │  giảm chi phí│  Giữ nguyên  │
   biến       ├──────────────┼──────────────┤
        Thấp  │     DOG      │   PUZZLE     │
              │  Cân nhắc bỏ │ Marketing lại│
              └──────────────┴──────────────┘
```

### 10.2 Thêm chiều thứ ba mà không ai có: **chi phí tài nguyên**

Món có biên lợi nhuận cao nhưng **chiếm trạm nghẽn 90 giây** có thể đang làm giảm tổng doanh thu giờ cao điểm.

```
Lợi nhuận thực trên một đơn vị thời gian trạm nghẽn
  = (giá bán − giá vốn) / occupancy_seconds_at_bottleneck
```

Đây là ứng dụng Lý thuyết Ràng buộc (Theory of Constraints) vào menu — insight mà không đối thủ nào tại VN đưa ra được.

### 10.3 Độ co giãn giá

Ước lượng từ lịch sử thay đổi giá + khuyến mãi (dùng mô hình log-log hoặc double ML nếu đủ dữ liệu):

```
Trà đào cam sả — độ co giãn ước tính: −1.4
  Tăng giá 45k → 50k (+11%):  sản lượng −15%,  doanh thu −5%   ❌
  Giảm giá 45k → 40k (−11%):  sản lượng +16%,  doanh thu +3%   ⚠️ nhưng tăng tải trạm
```

---

## 11. Customer 360 + dự đoán churn

| Thành phần | Chi tiết |
|---|---|
| Nhận diện | SĐT / QR loyalty / Zalo ID / thiết bị |
| Phân khúc RFM | Champions, Loyal, At Risk, Hibernating, Lost |
| Churn prediction | LightGBM: P(không quay lại trong 30 ngày) |
| Cá nhân hóa | Gợi ý món theo lịch sử + khung giờ thường đến |
| Automation | "Khách At Risk 21 ngày không quay lại → gửi voucher 20% qua Zalo" |

**Khác biệt so với loyalty truyền thống:** Không phải "tích 10 tem đổi 1 ly". Là **can thiệp đúng người, đúng lúc, đúng ưu đãi** dựa trên xác suất churn và độ nhạy giá của từng khách.

---

## 12. Offline-first POS

Chi tiết kiến trúc ở [tài liệu 01, mục 5](01-kien-truc-he-thong.md#5-offline-first--điều-kiện-sống-còn-ở-việt-nam).

Đây không phải "tính năng hay ho" — đây là **điều kiện để bán được hàng ở Việt Nam**. Rất nhiều phần mềm web-based thua ở đây.

---

## 13. Tuân thủ Việt Nam trọn gói

| Hạng mục | Chi tiết | Ưu tiên |
|---|---|---|
| Hóa đơn điện tử | Nghị định 123/2020 — tích hợp Viettel / VNPT / MISA / BKAV | P0 |
| Thanh toán | VietQR (chuyển khoản QR), Momo, ZaloPay, VNPay, thẻ | P0 |
| Sàn giao đồ ăn | GrabFood, ShopeeFood, beFood — đồng bộ menu + nhận đơn 2 chiều | P1 |
| Zalo OA | Kênh chăm sóc khách chủ đạo tại VN | P1 |
| Bảo vệ dữ liệu | Nghị định 13/2023 — consent, quyền xóa, nhật ký xử lý | P0 |
| Kế toán | Xuất file cho MISA / FAST | P2 |

> Đây là hạng mục **không tạo khác biệt nhưng bắt buộc phải có**. Thiếu HĐĐT là loại khỏi cuộc chơi ngay từ vòng đầu.

**Lưu ý về sàn giao đồ ăn:** Đơn từ GrabFood/ShopeeFood vào KDS phải được đối xử đặc biệt — có shipper đang chờ, SLA khác, và ETA phải tính cả thời gian shipper đến. Đây là feature riêng trong smart sequencing.

---

## 14. Computer Vision (giai đoạn sau)

| Ứng dụng | Giá trị | Ghi chú |
|---|---|---|
| Đếm người xếp hàng | Feature bổ sung cho ETA (bắt được khách chưa đặt) | Camera IP + YOLO on-edge |
| Phát hiện bàn trống/bẩn | Tối ưu quay vòng bàn, nhắc dọn | Cần góc camera tốt |
| Đo occupancy khu vực | Dữ liệu cho dự báo nhu cầu | — |

**Điều kiện triển khai:** Chi phí phần cứng + xử lý riêng tư (phải xử lý on-device, không lưu hình ảnh khuôn mặt, có biển thông báo — tuân thủ NĐ 13/2023). Chỉ nên làm khi đã có ≥200 khách hàng trả phí và có nhu cầu thật.

---

**Tiếp theo:** [05 — Chiến lược SEO & Tăng trưởng](05-seo-va-tang-truong.md)
