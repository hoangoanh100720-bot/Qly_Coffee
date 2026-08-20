# 03 — AI dự đoán thời gian chờ ⭐

> Đây là lõi khác biệt của sản phẩm. Toàn bộ phần còn lại tồn tại để nuôi và khai thác chương này.

---

## 1. Phát biểu bài toán cho đúng

**Sai lầm phổ biến:** Coi đây là bài toán hồi quy đơn giản — "cho danh sách món, dự đoán số phút".

**Bản chất thật:** Quán cà phê là một **flexible job-shop** — mỗi đơn hàng là một job gồm nhiều thao tác, mỗi thao tác cần một tài nguyên (trạm) có sức chứa hữu hạn, và các job chen nhau trong hàng đợi. Thời gian chờ của đơn thứ N phụ thuộc vào toàn bộ N-1 đơn trước nó và cách barista sắp xếp công việc.

Vì vậy ta cần **ba câu trả lời khác nhau**, không phải một:

| Câu hỏi | Đầu ra | Dùng ở đâu |
|---|---|---|
| Đơn này bao giờ xong? | Phân phối xác suất → lấy P80 | Hiển thị cho khách |
| Nên làm món nào trước? | Thứ tự tối ưu | Màn hình KDS |
| Nếu thay đổi X thì sao? | Kết quả mô phỏng | Digital twin cho chủ quán |

---

## 2. Kiến trúc 4 tầng

```
                    ┌──────────────────────────────────────┐
   Trạng thái       │  TẦNG 0 — QUEUE STATE (Redis)        │
   hàng đợi   ─────►│  Ai đang làm gì · Trạm nào bận bao lâu│
                    │  Còn bao nhiêu việc ở mỗi trạm       │
                    └──────────────┬───────────────────────┘
                                   │
                    ┌──────────────▼───────────────────────┐
                    │  TẦNG 1 — QUEUE SIMULATOR (SimPy)    │
                    │  Mô phỏng vật lý: tài nguyên, batch, │
                    │  ràng buộc đồng bộ                    │
                    │  → t_sim (baseline có thể giải thích)│
                    └──────────────┬───────────────────────┘
                                   │
                    ┌──────────────▼───────────────────────┐
                    │  TẦNG 2 — ML RESIDUAL (LightGBM)     │
                    │  Học phần simulator KHÔNG mô tả được:│
                    │  tay nghề, mệt mỏi, khách hỏi han,   │
                    │  custom phức tạp, hết nguyên liệu    │
                    │  → quantile regression P50/P80/P95   │
                    └──────────────┬───────────────────────┘
                                   │
                    ┌──────────────▼───────────────────────┐
                    │  TẦNG 3 — DISPLAY POLICY             │
                    │  Làm mượt · Ràng buộc đơn điệu ·     │
                    │  Làm tròn · Thông điệp trung thực    │
                    │  → ETA hiển thị cho khách            │
                    └──────────────────────────────────────┘
```

### Vì sao dùng hybrid mà không phải ML thuần?

| | ML thuần (end-to-end) | Hybrid (sim + ML residual) |
|---|---|---|
| Cold start (quán mới) | ❌ Không dùng được | ✅ Simulator chạy ngay từ ngày 1 |
| Giải thích được | ❌ Hộp đen | ✅ "Chờ 8 phút vì có 3 sinh tố trước bạn, máy xay chỉ có 1" |
| Ngoại suy (tải cực đại) | ❌ Sai nghiêm trọng | ✅ Vật lý vẫn đúng |
| Bắt được nhiễu thực tế | ✅ | ✅ (tầng 2 lo) |
| Dữ liệu cần | Rất nhiều | Ít (chỉ cần học phần dư) |

Hybrid thắng ở mọi tiêu chí quan trọng. Đây cũng là cách DoorDash/Uber Eats làm.

---

## 3. TẦNG 1 — Queue Simulator

### 3.1 Mô hình

```python
# apps/ai-service/src/simulator/engine.py  (rút gọn minh họa)
import simpy

class StoreSimulator:
    def __init__(self, env, stations, staff):
        # Mỗi trạm là một Resource với capacity = parallel_capacity
        self.stations = {
            code: simpy.PriorityResource(env, capacity=st.parallel_capacity)
            for code, st in stations.items()
        }
        # Barista là resource riêng — vì attended_seconds cần người,
        # còn occupancy_seconds thì máy tự chạy
        self.staff = simpy.PriorityResource(env, capacity=len(staff))
        self.staff_skill = {s.id: s.proficiency for s in staff}

    def process_item(self, env, item, priority):
        for step in item.station_steps:
            with self.stations[step.station_code].request(priority=priority) as station:
                yield station
                # Giai đoạn cần người
                with self.staff.request(priority=priority) as worker:
                    yield worker
                    yield env.timeout(step.attended_seconds * self.skill_factor)
                # Giai đoạn máy tự chạy (người đã rảnh đi làm việc khác)
                yield env.timeout(step.occupancy_seconds - step.attended_seconds)
        item.ready_at = env.now
```

### 3.2 Ba ràng buộc phải mô hình đúng

**(a) Batching — gộp món cùng loại**
3 ly latte cùng lúc không tốn 3× thời gian. Steam sữa một lần cho cả 3.

```
prep_time(n ly cùng loại) = base_prep + (n-1) × batch_marginal
```
Ví dụ latte: `base = 55s`, `batch_marginal = 12s` → 3 ly = 55 + 24 = **79s**, không phải 165s.

**(b) Ràng buộc đồng bộ — đơn nhiều món phải xong cùng lúc**
Không được làm xong cà phê rồi để nguội 5 phút chờ bánh mì nướng. Simulator phải **lùi thời điểm bắt đầu** của món nhanh:

```
start_time(item) = order_ready_time − prep_duration(item) − buffer
```
Với `order_ready_time = max(earliest_possible_ready(item) for item in order)`.

Riêng đồ uống nóng có `max_hold_seconds` (VD: espresso 90s) — nếu vi phạm, ưu tiên làm sau.

**(c) Tài nguyên nút thắt**
`parallel_capacity` của máy xay đá thường = 1. Nếu 5 khách gọi sinh tố cùng lúc → khách thứ 5 phải chờ 4×75s = 5 phút **chỉ riêng ở trạm đó**. Đây là insight mà không phần mềm nào ở VN đang bắt được.

### 3.3 Chi phí tính toán

Simulator chạy trên hàng đợi hiện tại (thường < 30 items). Trên CPU thường: **~10–15ms**. Đủ nhanh để chạy lại mỗi khi có sự kiện.

---

## 4. TẦNG 2 — ML Residual với Quantile Regression

### 4.1 Bài toán học

```
y = t_actual − t_sim            (phần dư, tính bằng giây)
```

Model: **LightGBM** với `objective='quantile'`, train 3 model riêng cho `alpha = 0.5, 0.8, 0.95`.

### 4.2 ⚠️ Vì sao quantile chứ không phải mean — điểm quan trọng nhất chương này

Hàm mất mát của khách hàng **bất đối xứng nghiêm trọng**:

```
Hứa 5 phút, xong 3 phút  →  khách vui        (chi phí ≈ 0)
Hứa 5 phút, xong 7 phút  →  khách bực, review 1 sao  (chi phí rất cao)
```

Nếu dự đoán **trung bình**, ta sai về phía trễ **50% số lần**. On-time rate = 50%. Thảm họa.

Dự đoán **P80** → đúng hạn 80% số lần, đổi lại ETA hơi bảo thủ. Đây là đánh đổi đúng.

```python
import lightgbm as lgb

QUANTILES = {"p50": 0.5, "p80": 0.8, "p95": 0.95}

models = {
    name: lgb.LGBMRegressor(
        objective="quantile", alpha=q,
        n_estimators=600, learning_rate=0.04,
        num_leaves=48, min_child_samples=30,
        subsample=0.8, colsample_bytree=0.8,
    ).fit(X_train, y_train, eval_set=[(X_val, y_val)],
          eval_metric="quantile", callbacks=[lgb.early_stopping(50)])
    for name, q in QUANTILES.items()
}
```

**Chiến lược hiển thị:**
- Khách tại quầy → hiển thị **P80**
- Khách đặt qua app (kỳ vọng cao hơn) → hiển thị **P90**
- Bảng điều khiển nội bộ cho barista → hiển thị **P50** (để họ phấn đấu)

### 4.3 Bộ đặc trưng (features)

Sắp xếp theo tầm quan trọng dự kiến:

#### A. Trạng thái hàng đợi (mạnh nhất — đóng góp ~45% tín hiệu)
| Feature | Mô tả |
|---|---|
| `work_in_queue_seconds[station]` | Tổng công việc còn lại ở TỪNG trạm (vector) — **feature số 1** |
| `items_ahead_count` | Số item đứng trước trong hàng đợi |
| `orders_ahead_count` | Số đơn đứng trước |
| `bottleneck_station_load` | Tải của trạm nghẽn nhất |
| `queue_age_p90_seconds` | Đơn cũ nhất trong hàng đã chờ bao lâu |
| `active_batches_count` | Số nhóm batch đang chạy |

#### B. Đặc trưng đơn hàng (~20%)
`item_count`, `distinct_product_count`, `total_base_prep_seconds`, `max_complexity_score`, `has_blender_item`, `has_hot_food_item`, `modifier_count`, `has_custom_note` (ghi chú tự do → thường chậm hơn), `is_batchable_ratio`, `channel`, `order_type`

#### C. Nhân sự (~15%)
`staff_on_shift_count`, `avg_proficiency_score`, `min_proficiency_score`, `minutes_into_shift` (mệt mỏi), `is_shift_change_window` (giao ca → chậm), `station_coverage_ratio`

#### D. Thời gian (~10%)
`hour_of_day` (sin/cos encoding), `day_of_week`, `is_weekend`, `is_holiday`, `minutes_since_open`, `is_peak_window` (học từ dữ liệu chính quán đó)

#### E. Ngoại cảnh & cửa hàng (~10%)
`temperature`, `is_raining` (mưa → tăng delivery, giảm dine-in), `store_id` (target encoding), `store_avg_prep_multiplier` (hệ số nhanh/chậm riêng của quán), `days_since_store_onboarded`

### 4.4 Chống rò rỉ dữ liệu (data leakage) — cạm bẫy chết người

> ❌ **KHÔNG BAO GIỜ** dùng feature chỉ biết được *sau* thời điểm dự đoán.

| Feature nguy hiểm | Vì sao sai |
|---|---|
| `actual_prep_seconds` của chính đơn đó | Đó là nhãn |
| Số đơn trong 5 phút *tiếp theo* | Chưa biết lúc dự đoán |
| Barista nào *đã* làm món | Chưa gán lúc đơn vào hàng |
| Tổng doanh thu ngày hôm đó | Tương lai |

**Quy tắc thực thi:** Mọi feature phải sinh được từ hàm `build_features(order, queue_state_at_t, store_config)` với `queue_state_at_t` là snapshot tại đúng thời điểm dự đoán. Snapshot này được **lưu vào `eta_predictions.features`** — vừa để debug, vừa để retrain không cần tái dựng.

**Chia tập train/test theo thời gian, không random.** Train trên tuần 1–8, validate tuần 9, test tuần 10. Random split sẽ cho kết quả đẹp giả tạo vì các đơn cùng một khoảng thời gian tương quan mạnh.

---

## 5. Cold start & Flywheel dữ liệu (lợi thế cạnh tranh)

### 5.1 Ba giai đoạn của một quán mới

```
Ngày 1–14          │  Ngày 15–60             │  Ngày 61+
(0 đơn)            │  (~500-2000 đơn)         │  (>2000 đơn)
                   │                          │
SIMULATOR ONLY     │  GLOBAL MODEL            │  GLOBAL + PER-STORE
+ thư viện công    │  + hiệu chỉnh tuyến tính │  CALIBRATION
  thức chuẩn       │    riêng cho quán        │  (isotonic regression)
                   │                          │
MAE ~150s          │  MAE ~110s               │  MAE ~75s
```

### 5.2 Global model — trái tim của flywheel

```
Dữ liệu từ TẤT CẢ tenant (đã ẩn danh, bỏ tên món riêng,
chuẩn hóa theo station_code chuẩn)
                    ↓
        Train Global Residual Model
                    ↓
   Quán mới dùng ngay từ ngày 15 → chính xác hơn hẳn
                    ↓
   Quán mới đóng góp dữ liệu ngược lại
                    ↓
        Model tốt hơn nữa (vòng lặp)
```

**Đây là hào cạnh tranh (moat).** Đối thủ mới vào ngành phải mất 2 năm tích lũy dữ liệu mới đạt được độ chính xác của ta. KiotViet có nhiều khách hàng hơn nhưng **không thu thập mốc thời gian pha chế** → dữ liệu của họ vô dụng cho bài toán này.

**Điều kiện pháp lý:** Ghi rõ trong ToS quyền sử dụng dữ liệu vận hành đã ẩn danh để cải thiện dịch vụ. Cho phép tenant Enterprise opt-out (và chấp nhận mô hình kém hơn).

### 5.3 Per-store calibration

Sau khi có ≥2000 đơn, train một lớp hiệu chỉnh mỏng riêng cho quán:

```python
from sklearn.isotonic import IsotonicRegression

# Học ánh xạ: dự đoán global → thực tế của quán này
calibrator = IsotonicRegression(out_of_bounds="clip")
calibrator.fit(global_predictions_for_store, actuals_for_store)

final_eta = calibrator.predict(global_model.predict(features))
```

Isotonic regression đơn giản, ít dữ liệu vẫn ổn định, và **bảo toàn thứ tự** (đơn được dự đoán lâu hơn vẫn lâu hơn sau hiệu chỉnh).

---

## 6. TẦNG 3 — Display Policy (quyết định trải nghiệm)

Model đúng nhưng hiển thị sai vẫn hỏng sản phẩm.

### 6.1 Ràng buộc đơn điệu

```python
def smooth_eta(order_id, new_eta_seconds, state):
    prev = state.last_shown_eta.get(order_id)
    if prev is None:
        shown = new_eta_seconds
    elif new_eta_seconds <= prev:
        shown = new_eta_seconds                      # giảm: cho phép ngay
    else:
        delta = min(new_eta_seconds - prev, 120)     # tăng: tối đa +2 phút/lần
        shown = prev + delta
        if delta > 30:
            emit_apology(order_id, reason=state.delay_reason)
    state.last_shown_eta[order_id] = shown
    return shown
```

### 6.2 Làm tròn có chủ đích

| ETA thực | Hiển thị | Lý do |
|---|---|---|
| < 120s | "Sắp xong" | Đếm giây tạo áp lực ngược |
| 120–600s | Làm tròn lên bội số 60s: "khoảng 5 phút" | Độ chính xác giả tạo gây mất tin cậy |
| > 600s | Khoảng: "12–15 phút" | Thừa nhận độ bất định |
| Quá hạn | "Đang ưu tiên xử lý đơn của bạn" | Trung thực > giả vờ |

### 6.3 Giải thích được (explainability) — tính năng bán hàng

Vì có simulator, ta trả lời được câu hỏi khách hay hỏi nhất: **"Sao lâu vậy?"**

```json
{
  "eta_seconds": 480,
  "explanation": {
    "summary": "Khoảng 8 phút",
    "breakdown": [
      { "reason": "Có 3 đơn đang được pha trước bạn", "seconds": 240 },
      { "reason": "Món Sinh tố xoài cần máy xay (đang có 1 đơn dùng)", "seconds": 150 },
      { "reason": "Thời gian pha chế món của bạn", "seconds": 90 }
    ],
    "faster_alternative": {
      "product_id": "...",
      "name": "Cà phê sữa đá",
      "eta_seconds": 180,
      "message": "Món này sẵn sàng trong 3 phút"
    }
  }
}
```

Không đối thủ nào ở Việt Nam làm được điều này, vì họ không có mô hình vật lý bên dưới.

---

## 7. Smart Sequencing — tối ưu thứ tự làm món

Đây là nơi AI **tạo ra giá trị**, không chỉ dự đoán giá trị.

### 7.1 Bài toán

FIFO (làm theo thứ tự đến) là chiến lược tệ. Tối ưu đa mục tiêu:

```
minimize:  w₁ · Σ(thời gian chờ)              # tổng thời gian chờ
         + w₂ · Σ(vi phạm SLA)²               # phạt nặng đơn quá hạn
         + w₃ · Σ(thời gian giữ món nóng)     # cà phê nguội
         − w₄ · (số món được batch)           # thưởng cho việc gộp
         + w₅ · Σ(chuyển trạm của barista)    # phạt context switching

subject to:  max_wait(bất kỳ đơn nào) ≤ SLA_hard    # công bằng — không bỏ rơi ai
             đơn có shipper đang chờ được ưu tiên
```

### 7.2 Thuật toán theo giai đoạn

| Phase | Thuật toán | Khi nào |
|---|---|---|
| MVP | Heuristic có trọng số (greedy + batch grouping) | Hàng đợi < 20 items |
| V2 | Beam search trên simulator (mô phỏng 5 thứ tự tốt nhất) | Hàng đợi < 40 items |
| V3 | Google OR-Tools CP-SAT với giới hạn 200ms | Mọi trường hợp |

### 7.3 Hiển thị trên KDS — bí quyết được chấp nhận

> ⚠️ **Không được ra lệnh cho barista.** Barista giỏi biết những thứ hệ thống không biết.

```
┌─────────────────────── KDS ────────────────────────┐
│  ⚡ GỢI Ý: Làm 3 ly Latte cùng lúc — tiết kiệm 90s │
│                              [Áp dụng]  [Bỏ qua]   │
├────────────────────────────────────────────────────┤
│  #A042  ●●● Latte ×3        [Bắt đầu] ⏱ hứa 4:00  │
│  #A039  ●   Sinh tố xoài    [Bắt đầu] ⏱ hứa 2:30 ⚠│
│  #A044  ●●  Cà phê sữa ×2   [Bắt đầu] ⏱ hứa 6:00  │
└────────────────────────────────────────────────────┘
```

Hệ thống **gợi ý**, barista **quyết định**. Ghi lại tỷ lệ chấp nhận gợi ý → chỉ số tin cậy vào AI, và là tín hiệu để cải thiện thuật toán.

---

## 8. Vấn đề khó nhất: thu thập nhãn chất lượng

**Nếu barista không bấm nút, toàn bộ chương này sụp đổ.** Đây là rủi ro số 1 của sản phẩm.

### 8.1 Thiết kế KDS để việc bấm gần như không tốn công

| Nguyên tắc | Thực thi |
|---|---|
| Một chạm | Nút "Bắt đầu" và "Xong" chiếm ≥ 25% chiều cao thẻ đơn |
| Không cần nhìn kỹ | Màu sắc + rung/âm thanh phản hồi tức thì |
| Bấm nhầm dễ sửa | Undo trong 10 giây, không cần xác nhận |
| Không chặn công việc | Không bao giờ có modal/popup bắt buộc |
| Batch một chạm | Bấm 1 lần cho cả nhóm batch |

### 8.2 Suy luận khi thiếu nhãn

Khi barista quên bấm `item.started`:

```python
def infer_started_at(item, events):
    if item.started_at:
        return item.started_at, "explicit"
    # Suy luận 1: bấm ready mà không bấm started
    if item.ready_at:
        est = item.ready_at - median_prep_time(item.product_id, item.store_id)
        return max(est, item.queued_at), "inferred_from_ready"
    # Suy luận 2: item trước đó ở cùng trạm đã ready
    prev = previous_ready_at_same_station(item)
    if prev:
        return max(prev, item.queued_at), "inferred_from_previous"
    return None, "missing"
```

**Quan trọng:** Nhãn suy luận được gắn cờ `label_quality`. Khi train, gán trọng số:
`explicit = 1.0`, `inferred_from_ready = 0.6`, `inferred_from_previous = 0.3`, `missing = loại bỏ`.

### 8.3 Gamification nhẹ (không tạo áp lực độc hại)

```
Dashboard barista cuối ca:
  ✓ 47 món hoàn thành
  ✓ 94% đúng thời gian đã hứa với khách
  ⚡ Nhanh hơn 12% so với trung bình quán
  🏅 Chuỗi 8 ca liên tiếp ghi nhận đầy đủ
```

Thưởng cho **tính đầy đủ của dữ liệu**, không thưởng cho **tốc độ**. Thưởng tốc độ sẽ khiến barista làm ẩu hoặc bấm "xong" trước khi thực sự xong (gian lận nhãn).

### 8.4 Phát hiện nhãn gian lận

Job nền quét bất thường:
- `actual_prep_seconds` < 30% của `base_prep_seconds` lặp lại → nghi bấm sớm
- Nhiều item `ready` cùng một giây → nghi bấm hàng loạt cuối ca
- Tương quan bất thường giữa `ready_at` và giờ tan ca

Cảnh báo cho quản lý, và **loại các nhãn nghi ngờ khỏi tập train**.

---

## 9. MLOps

### 9.1 Vòng đời model

```
Hằng đêm 02:00 (giờ VN):
  1. Job ETL: order_events + eta_predictions → feature store (Parquet trên S3)
  2. Tính actual_ready_at, error_seconds cho các dự đoán đã có kết quả
  3. Train lại global model (nếu có ≥5% dữ liệu mới)
  4. Backtest trên tuần gần nhất (chưa từng thấy)
  5. So sánh với model đang chạy:
       nếu MAE giảm ≥3% VÀ on-time_rate không giảm → promote
       ngược lại → giữ nguyên, cảnh báo
  6. Đăng ký vào MLflow Model Registry với version tag
  7. Deploy shadow mode 24h → nếu ổn, chuyển sang canary 10% store

Hằng tuần:
  - Retrain per-store calibrator cho các quán đủ dữ liệu
  - Cập nhật base_prep_seconds của từng món theo dữ liệu thực (EWMA)
  - Báo cáo drift: so sánh phân phối feature tuần này vs tháng trước
```

### 9.2 Shadow mode — bắt buộc trước mọi lần promote

Model mới chạy song song, ghi dự đoán vào `eta_predictions` với `prediction_type='shadow'` nhưng **không hiển thị cho khách**. Sau 24h so sánh trực tiếp trên cùng tập đơn.

### 9.3 Giám sát drift

| Loại drift | Cách phát hiện | Ngưỡng cảnh báo |
|---|---|---|
| Data drift | PSI (Population Stability Index) trên từng feature | PSI > 0.2 |
| Concept drift | MAE 7 ngày gần nhất vs 30 ngày | Tăng > 20% |
| Label drift | `label_quality='explicit'` ratio | Giảm dưới 70% |
| Prediction drift | Phân phối ETA dự đoán | KS test p < 0.01 |

---

## 10. Chiến lược đánh giá

### 10.1 Baseline phải đánh bại

| Baseline | Mô tả | MAE dự kiến |
|---|---|---|
| B0 — Hằng số | Luôn nói "5 phút" | ~240s |
| B1 — Tổng base_prep | Cộng thời gian pha chế các món | ~200s |
| B2 — Trung bình lịch sử theo giờ | Trung bình theo `(store, hour)` | ~160s |
| **B3 — Simulator only** | Tầng 1 | **~130s** |
| **B4 — Sim + ML (mục tiêu)** | Tầng 1+2 | **< 90s** |

> Nếu B4 không đánh bại B2 một cách rõ rệt, **dừng lại và xem xét lại bài toán** thay vì thêm feature.

### 10.2 Metrics phải theo dõi cùng lúc

```python
metrics = {
    "mae_seconds":        mean(abs(y_true - y_pred_p50)),
    "pinball_loss_p80":   pinball(y_true, y_pred_p80, alpha=0.8),
    "on_time_rate":       mean(y_true <= y_pred_p80),          # ⭐ metric BUSINESS
    "over_promise_rate":  mean(y_true > y_pred_p80),
    "avg_over_delay":     mean((y_true - y_pred_p80)[y_true > y_pred_p80]),
    "eta_padding":        mean(y_pred_p80 - y_true),           # hứa thừa bao nhiêu
}
```

**Đánh đổi cần theo dõi:** Tăng `on_time_rate` bằng cách hứa dài hơn (`eta_padding` tăng) là gian lận. Phải tối ưu cả hai: **on_time_rate ≥ 85% VỚI eta_padding ≤ 90s**.

### 10.3 Cắt lát phân tích (bắt buộc)

Model tốt "trung bình" nhưng tệ ở giờ cao điểm là model vô dụng. Báo cáo metrics riêng cho:
- Giờ cao điểm vs giờ thường
- Đơn 1 món vs đơn ≥4 món
- Có món blender vs không
- Quán mới (<30 ngày) vs quán cũ
- Từng kênh (POS / QR / GrabFood)

---

## 11. Đảm bảo an toàn (fail-safe)

| Tình huống | Hành vi |
|---|---|
| AI service không phản hồi > 200ms | Fallback ETA tĩnh, log cảnh báo |
| AI service chết hoàn toàn | Circuit breaker → ETA tĩnh, banner cho quản lý |
| Model trả về giá trị vô lý (<0 hoặc >3600s) | Clamp về [60, 1800], ghi anomaly |
| MAE tăng đột biến 1 store | Tự động chuyển store đó về simulator-only, cảnh báo |
| Store chưa cấu hình trạm | Dùng cấu hình mặc định của loại hình quán, hiển thị nhắc nhở setup |

**Nguyên tắc:** Lỗi AI **không bao giờ** được chặn việc bán hàng.

---

**Tiếp theo:** [04 — Tính năng khác biệt](04-tinh-nang-khac-biet.md)
