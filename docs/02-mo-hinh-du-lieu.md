# 02 — Mô hình dữ liệu

> **Quy ước chung:** Mọi bảng nghiệp vụ có `id UUID PK (uuidv7)`, `tenant_id UUID NOT NULL`, `created_at`, `updated_at`, `deleted_at` (soft delete). Mọi bảng bật RLS theo `tenant_id`.

---

## 1. Nhóm Tenant & Tổ chức

```sql
tenants
  id, name, slug (unique, dùng cho subdomain SEO), plan,
  status, trial_ends_at, billing_customer_id, settings JSONB

brands
  id, tenant_id, name, slug, logo_url, brand_color, description

stores                              -- chi nhánh
  id, tenant_id, brand_id, name, slug,
  address, ward, district, province, lat, lng,       -- phục vụ Local SEO
  phone, timezone, opening_hours JSONB,
  google_place_id,                                    -- đồng bộ GMB
  capacity_seats, is_active

users
  id, tenant_id, email, phone, password_hash,
  full_name, avatar_url, locale, last_login_at

user_store_roles                    -- phân quyền theo chi nhánh
  user_id, store_id, role            -- owner|manager|cashier|barista|accountant
  PRIMARY KEY (user_id, store_id)

terminals                           -- máy POS vật lý
  id, tenant_id, store_id, code, device_fingerprint,
  invoice_number_range_start, invoice_number_range_end, invoice_number_cursor
```

---

## 2. Nhóm Menu & Công thức

```sql
categories
  id, tenant_id, brand_id, name, slug, sort_order, image_url

products
  id, tenant_id, brand_id, category_id,
  name, slug, description, image_url,
  base_price, cost_price, tax_rate,
  is_active, is_featured,
  -- SEO
  seo_title, seo_description,
  -- vận hành / AI
  base_prep_seconds INT,             -- thời gian pha chế chuẩn (không tính chờ)
  complexity_score SMALLINT,         -- 1-5, do hệ thống học lại
  is_batchable BOOLEAN,              -- có thể gộp làm chung nhiều ly không
  serve_temperature TEXT             -- hot|cold|room → ảnh hưởng ràng buộc đồng bộ

product_variants                    -- size, nóng/đá
  id, product_id, name, price_delta, base_prep_delta_seconds, sku

modifier_groups                     -- nhóm tùy chọn (mức đường, topping)
  id, tenant_id, name, min_select, max_select, is_required

modifiers
  id, modifier_group_id, name, price_delta, prep_delta_seconds

product_modifier_groups             -- N-N
  product_id, modifier_group_id, sort_order
```

### 2.1 Trạm pha chế — nền tảng của mô hình hàng đợi

Đây là bảng **không đối thủ nào có**. Nó biến menu từ danh sách giá thành mô hình tài nguyên.

```sql
stations                            -- trạm vật lý ở mỗi chi nhánh
  id, tenant_id, store_id,
  code,                             -- espresso|brew|blender|kitchen|assembly
  name, parallel_capacity SMALLINT, -- số việc chạy song song (2 group máy espresso = 2)
  is_bottleneck BOOLEAN             -- hệ thống tự đánh dấu

product_station_steps               -- một món chiếm những trạm nào, bao lâu
  id, product_id, variant_id NULL,
  station_code, step_order,
  occupancy_seconds INT,            -- thời gian CHIẾM trạm
  attended_seconds INT,             -- thời gian barista PHẢI đứng đó
                                    -- (VD: máy xay đá chiếm 60s nhưng chỉ cần người 10s)
  can_batch_with_same_product BOOLEAN,
  batch_marginal_seconds INT        -- ly thứ 2 cùng loại tốn thêm bao nhiêu giây
```

> **Ví dụ — Cà phê sữa đá:**
> | step | station | occupancy | attended | batch marginal |
> |---|---|---|---|---|
> | 1 | `brew` (phin/espresso) | 45s | 15s | +8s |
> | 2 | `assembly` (pha, đá, đóng ly) | 30s | 30s | +20s |
>
> **Sinh tố xoài:** `blender` 75s occupancy / 20s attended, `parallel_capacity = 1` → đây là nút thắt cổ chai kinh điển của quán VN.

**Cách khởi tạo:** Cung cấp **thư viện công thức chuẩn** cho ~120 món phổ biến VN (cà phê sữa, bạc xỉu, trà đào, sinh tố...). Quán mới chọn món từ thư viện → có ngay tham số trạm. Sau đó hệ thống tự học lại tham số riêng cho quán đó từ dữ liệu thực.

### 2.2 Nguyên liệu & định mức

```sql
ingredients
  id, tenant_id, name, unit,        -- g|ml|cái
  cost_per_unit, min_stock_level, shelf_life_days, supplier_id

recipes                             -- BOM: món → nguyên liệu
  id, product_id, variant_id NULL, modifier_id NULL,
  ingredient_id, quantity, is_optional

suppliers
  id, tenant_id, name, phone, email, lead_time_days, min_order_value
```

---

## 3. Nhóm Đơn hàng — thiết kế event-sourcing nhẹ

### 3.1 Bảng trạng thái hiện tại

```sql
orders
  id, tenant_id, store_id, terminal_id,
  order_number,                     -- theo ngày, theo store: "A-0042"
  channel,                          -- pos|qr_table|app|grabfood|shopeefood|zalo|phone
  order_type,                       -- dine_in|takeaway|delivery|preorder
  table_id NULL, customer_id NULL,
  status,                           -- draft|confirmed|preparing|ready|completed|cancelled
  subtotal, discount_total, tax_total, grand_total,
  -- ETA (lõi sản phẩm)
  promised_eta_at TIMESTAMPTZ,      -- ETA ĐÃ HỨA với khách (không đổi để chấm điểm)
  current_eta_at TIMESTAMPTZ,       -- ETA cập nhật realtime
  requested_ready_at TIMESTAMPTZ,   -- khách yêu cầu xong lúc (đặt trước)
  -- mốc thời gian thực tế (nhãn để train)
  placed_at, confirmed_at, prep_started_at, all_items_ready_at,
  handed_over_at, cancelled_at,
  client_event_id UUID UNIQUE,      -- idempotency cho offline sync
  notes

order_items
  id, order_id, product_id, variant_id, quantity,
  unit_price, modifiers JSONB, line_total,
  status,                           -- queued|in_progress|ready|served|voided
  assigned_station_code,
  assigned_barista_id NULL,
  -- mốc thời gian per-item (quan trọng hơn per-order!)
  queued_at, started_at, ready_at,
  batch_group_id UUID NULL,         -- các item được gộp làm cùng lúc
  predicted_prep_seconds INT,       -- model dự đoán
  actual_prep_seconds INT           -- thực tế (= ready_at - started_at)
```

### 3.2 Bảng sự kiện (append-only) — nguồn chân lý

```sql
order_events                        -- TimescaleDB hypertable, partition theo time
  id, tenant_id, store_id, order_id, order_item_id NULL,
  event_type,                       -- xem bảng dưới
  occurred_at TIMESTAMPTZ NOT NULL,
  recorded_at TIMESTAMPTZ,          -- lệch với occurred_at nếu sync offline
  actor_type,                       -- user|system|customer|integration
  actor_id, terminal_id,
  payload JSONB,
  client_event_id UUID UNIQUE
```

| `event_type` | Ai/cái gì phát ra | Dùng làm nhãn cho |
|---|---|---|
| `order.placed` | POS / QR / app | t0 của mọi phép đo |
| `order.confirmed` | Thu ngân | Thời gian xác nhận |
| `ticket.printed` | Hệ thống | — |
| `item.started` | **Barista bấm KDS** | ⭐ Nhãn quan trọng nhất |
| `item.ready` | **Barista bấm KDS** | ⭐ `actual_prep_seconds` |
| `order.all_ready` | Hệ thống (suy ra) | On-time rate |
| `order.handed_over` | Thu ngân/khách quét | Thời gian khách thực nhận |
| `order.cancelled` | Bất kỳ | Phân tích churn |
| `eta.predicted` | AI service | Đánh giá model |
| `eta.revised` | AI service | ETA stability |
| `station.blocked` | Barista | Phát hiện sự cố thiết bị |

> **Vì sao append-only?** (1) Là training data không thể sửa đổi. (2) Là audit trail chống gian lận. (3) Là cơ chế sync offline tự nhiên. (4) Cho phép tái dựng trạng thái tại bất kỳ thời điểm nào để debug.

### 3.3 Bảng đánh giá mô hình — **bắt buộc phải có**

Không có bảng này thì không thể chứng minh AI hoạt động.

```sql
eta_predictions                     -- hypertable
  id, tenant_id, store_id, order_id,
  predicted_at TIMESTAMPTZ,
  model_version TEXT,               -- vd: "global-v3.2+store-calib-v1"
  prediction_type,                  -- initial|revision
  predicted_ready_at TIMESTAMPTZ,
  predicted_p50_seconds INT,
  predicted_p80_seconds INT,        -- giá trị hiển thị cho khách
  predicted_p95_seconds INT,
  sim_baseline_seconds INT,         -- kết quả mô phỏng trước khi ML hiệu chỉnh
  ml_residual_seconds INT,          -- phần ML điều chỉnh
  features JSONB,                   -- snapshot feature để debug & retrain
  -- điền sau khi đơn xong (job nền)
  actual_ready_at TIMESTAMPTZ,
  error_seconds INT,
  was_on_time BOOLEAN
```

---

## 4. Nhóm Thanh toán & Hóa đơn

```sql
payments
  id, tenant_id, order_id, method,  -- cash|card|momo|zalopay|vnpay|vietqr|banking
  amount, status, provider_txn_id, paid_at, refunded_amount

e_invoices                          -- hóa đơn điện tử (Nghị định 123/2020)
  id, tenant_id, order_id, provider,-- viettel|vnpt|misa|bkav
  invoice_series, invoice_number, tax_authority_code,
  status, issued_at, pdf_url, xml_url, error_message
```

---

## 5. Nhóm Khách hàng & Loyalty

```sql
customers
  id, tenant_id, phone (unique per tenant), name, email, birthday,
  -- CDP fields (tính bằng job nền)
  first_order_at, last_order_at, total_orders, total_spent,
  avg_order_value, favorite_product_id, favorite_time_slot,
  rfm_recency, rfm_frequency, rfm_monetary, rfm_segment,
  churn_risk_score,                 -- 0-1, model dự đoán
  consent_marketing BOOLEAN,        -- tuân thủ NĐ 13/2023
  consent_at

loyalty_accounts
  id, customer_id, tenant_id, points_balance, tier, tier_expires_at

loyalty_transactions
  id, loyalty_account_id, order_id, type, points, expires_at

vouchers / voucher_redemptions
  ... (mã, loại giảm, điều kiện, giới hạn, ngày hiệu lực)
```

---

## 6. Nhóm Kho & Ca làm việc

```sql
inventory_stocks
  id, tenant_id, store_id, ingredient_id,
  quantity_on_hand, quantity_reserved, last_counted_at

inventory_transactions              -- hypertable
  id, tenant_id, store_id, ingredient_id,
  type,                             -- purchase|consumption|adjustment|waste|transfer
  quantity_delta, unit_cost, reference_type, reference_id,
  reason, actor_id, occurred_at

purchase_orders / purchase_order_items
  ... (bao gồm cờ `auto_generated` cho đơn AI tự sinh)

staff_shifts
  id, tenant_id, store_id, user_id,
  starts_at, ends_at, role, station_assignments JSONB,
  clock_in_at, clock_out_at, is_ai_generated

staff_skills                        -- để xếp ca thông minh + feature cho ETA
  user_id, station_code,
  proficiency_score NUMERIC,        -- 0-1, tính từ actual_prep_seconds lịch sử
  orders_completed INT, last_updated_at
```

---

## 7. Dữ liệu ngoại cảnh (feature cho model)

```sql
external_signals                    -- hypertable
  id, store_id, signal_type,        -- weather|holiday|event|traffic
  occurred_at, value JSONB
```

Nguồn: OpenWeather API (thời tiết theo giờ), lịch nghỉ lễ VN (hard-code + cập nhật), sự kiện lớn gần quán (nhập tay hoặc Google Places).

---

## 8. Chỉ mục & phân vùng — không thể bỏ qua

```sql
-- Truy vấn nóng nhất: hàng đợi hiện tại của 1 chi nhánh
CREATE INDEX idx_orders_active ON orders (store_id, status, placed_at)
  WHERE status IN ('confirmed','preparing') AND deleted_at IS NULL;

CREATE INDEX idx_order_items_station ON order_items (assigned_station_code, status, queued_at)
  WHERE status IN ('queued','in_progress');

-- Mọi bảng có tenant_id: index composite đặt tenant_id đầu tiên
CREATE INDEX idx_orders_tenant_time ON orders (tenant_id, store_id, placed_at DESC);

-- TimescaleDB hypertables
SELECT create_hypertable('order_events', 'occurred_at', chunk_time_interval => INTERVAL '7 days');
SELECT create_hypertable('eta_predictions', 'predicted_at', chunk_time_interval => INTERVAL '7 days');
SELECT create_hypertable('inventory_transactions', 'occurred_at', chunk_time_interval => INTERVAL '30 days');

-- Nén dữ liệu cũ (tiết kiệm ~90% dung lượng)
ALTER TABLE order_events SET (timescaledb.compress, timescaledb.compress_segmentby = 'store_id');
SELECT add_compression_policy('order_events', INTERVAL '30 days');

-- Continuous aggregate: metrics ETA theo giờ (dashboard đọc từ đây, không quét raw)
CREATE MATERIALIZED VIEW eta_accuracy_hourly
WITH (timescaledb.continuous) AS
SELECT store_id,
       time_bucket('1 hour', predicted_at) AS bucket,
       count(*) AS n,
       avg(abs(error_seconds)) AS mae,
       percentile_cont(0.9) WITHIN GROUP (ORDER BY abs(error_seconds)) AS p90_error,
       avg(was_on_time::int) AS on_time_rate
FROM eta_predictions
WHERE actual_ready_at IS NOT NULL
GROUP BY store_id, bucket;
```

---

## 9. Chính sách lưu trữ dữ liệu

| Dữ liệu | Nóng (Postgres) | Lạnh (S3/ClickHouse) | Xóa |
|---|---|---|---|
| `order_events` | 90 ngày | 3 năm | — |
| `eta_predictions` | 180 ngày (cần để retrain) | 2 năm | — |
| Ảnh món | — | Vĩnh viễn | Khi tenant xóa |
| Dữ liệu cá nhân khách | Theo thời hạn đồng ý | — | Theo yêu cầu (NĐ 13) |
| Log ứng dụng | 14 ngày (Loki) | 90 ngày | — |

---

**Tiếp theo:** [03 — AI dự đoán thời gian chờ](03-ai-du-doan-thoi-gian-cho.md) ⭐
