# 01 — Kiến trúc hệ thống

## 1. Sơ đồ tổng thể

```
                          ┌─────────────────────────────────┐
                          │        CDN (Cloudflare)         │
                          │   cache tĩnh · WAF · rate limit │
                          └───────────────┬─────────────────┘
                                          │
        ┌─────────────────────────────────┼──────────────────────────────────┐
        │                                 │                                  │
┌───────▼────────┐              ┌─────────▼─────────┐              ┌─────────▼────────┐
│  apps/web      │              │  apps/pos         │              │  apps/kds        │
│  Next.js 15    │              │  Next.js PWA      │              │  Next.js PWA     │
│  Marketing+SEO │              │  Offline-first    │              │  Màn hình bar    │
│  Admin dash    │              │  IndexedDB queue  │              │  Fullscreen      │
│  Menu QR khách │              │                   │              │  Touch-first     │
└───────┬────────┘              └─────────┬─────────┘              └─────────┬────────┘
        │                                 │                                  │
        └──────────────┬──────────────────┴──────────────────┬───────────────┘
                       │ REST/tRPC                            │ WebSocket
              ┌────────▼──────────────────────────────────────▼────────┐
              │                    apps/api  (NestJS)                  │
              │  ┌──────────┬──────────┬───────────┬────────────────┐  │
              │  │ Auth/RBAC│ Orders   │ Inventory │ Realtime GW    │  │
              │  │ Tenant   │ Menu     │ Loyalty   │ (Socket.IO)    │  │
              │  │ Billing  │ Payments │ Reports   │ Webhooks       │  │
              │  └──────────┴──────────┴───────────┴────────────────┘  │
              │            BullMQ Workers (jobs nền)                   │
              └────┬──────────────┬────────────────┬────────────┬──────┘
                   │              │                │            │
       ┌───────────▼──┐  ┌────────▼──────┐  ┌──────▼─────┐  ┌───▼──────────────┐
       │ PostgreSQL16 │  │   Redis 7     │  │  S3 / R2   │  │ apps/ai-service  │
       │ + TimescaleDB│  │ cache·pubsub  │  │ ảnh·báo cáo│  │ Python·FastAPI   │
       │ + RLS        │  │ BullMQ·queue  │  │            │  │ ┌──────────────┐ │
       │              │  │ state hàng đợi│  │            │  │ │ ETA Predictor│ │
       └──────────────┘  └───────────────┘  └────────────┘  │ │ Queue Sim    │ │
                                                            │ │ Scheduler    │ │
       ┌──────────────┐                                     │ │ Forecaster   │ │
       │  ClickHouse  │◄── CDC (Debezium) ── từ Phase 4     │ │ Training Pipe│ │
       │  analytics   │                                     │ └──────────────┘ │
       └──────────────┘                                     └──────────────────┘

  Tích hợp ngoài: VNPay·Momo·ZaloPay·VietQR │ Viettel/VNPT e-Invoice
                  GrabFood·ShopeeFood·beFood │ Zalo OA·Messenger │ Claude API
```

## 2. Cấu trúc monorepo

```
qly-coffee/
├── apps/
│   ├── web/               # Next.js — marketing (SEO), admin dashboard, menu QR
│   ├── pos/               # Next.js PWA — máy bán hàng, offline-first
│   ├── kds/               # Next.js PWA — Kitchen Display System
│   ├── api/               # NestJS — REST + WS + workers
│   └── ai-service/        # Python FastAPI — inference + training
├── packages/
│   ├── db/                # Prisma schema, migrations, seed
│   ├── shared/            # types, zod schemas, constants dùng chung FE/BE
│   ├── ui/                # design system (shadcn/ui mở rộng)
│   ├── sdk/               # client SDK sinh từ OpenAPI
│   └── config/            # eslint, tsconfig, tailwind preset
├── infra/
│   ├── docker/            # docker-compose.dev.yml, Dockerfile mỗi app
│   ├── k8s/               # helm charts
│   └── terraform/         # hạ tầng cloud
└── docs/
```

**Công cụ:** pnpm workspaces + Turborepo (cache build), Changesets (versioning packages).

---

## 3. Multi-tenant: Row-Level Security trên schema chung

### 3.1 Lựa chọn kiến trúc

| Phương án | Ưu | Nhược | Quyết định |
|---|---|---|---|
| DB riêng mỗi tenant | Cách ly tuyệt đối | Migration ác mộng, chi phí cao | ❌ |
| Schema riêng mỗi tenant | Cách ly tốt | Postgres chậm khi > 1000 schema | ❌ (nhưng để ngỏ cho tenant Enterprise) |
| **Schema chung + `tenant_id` + RLS** | Đơn giản, rẻ, migration 1 lần | Cần kỷ luật code | ✅ **Chọn** |

### 3.2 Cách thực thi

Mọi bảng nghiệp vụ có cột `tenant_id UUID NOT NULL`. Bật RLS:

```sql
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE orders FORCE ROW LEVEL SECURITY;   -- áp dụng cả với owner

CREATE POLICY tenant_isolation ON orders
  USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
```

Trong NestJS, mọi request đi qua interceptor:

```typescript
// apps/api/src/common/tenant.interceptor.ts
@Injectable()
export class TenantInterceptor implements NestInterceptor {
  intercept(ctx: ExecutionContext, next: CallHandler) {
    const tenantId = ctx.switchToHttp().getRequest().user.tenantId;
    return from(
      this.prisma.$transaction(async (tx) => {
        await tx.$executeRawUnsafe(`SET LOCAL app.tenant_id = '${tenantId}'`);
        return firstValueFrom(next.handle());
      }),
    );
  }
}
```

**Ba lớp phòng thủ (defense in depth):**
1. RLS ở tầng DB (không thể vượt qua kể cả khi code sai)
2. Prisma middleware tự chèn `where: { tenantId }` vào mọi query
3. Test tự động: mỗi endpoint có 1 test "tenant A không đọc được data tenant B"

> **Lối thoát cho Enterprise:** Tenant lớn (>500k đơn/tháng) có thể tách sang DB riêng. Vì code luôn dùng `tenant_id`, việc di chuyển chỉ là đổi connection string theo tenant — không cần sửa logic.

### 3.3 Phân cấp tổ chức

```
Tenant (doanh nghiệp)
  └── Brand (thương hiệu — 1 tenant có thể có nhiều brand)
        └── Store (chi nhánh)
              ├── Station (trạm pha chế)
              ├── Terminal (máy POS)
              └── Zone/Table (khu vực/bàn)
```

---

## 4. Realtime — xương sống của trải nghiệm

### 4.1 Kênh Socket.IO

| Room | Ai vào | Nhận gì |
|---|---|---|
| `store:{id}:kds` | Màn hình bar/bếp | Đơn mới, thay đổi thứ tự làm món, cảnh báo SLA |
| `store:{id}:pos` | Máy tính tiền | Món xong, cần gọi khách |
| `store:{id}:dashboard` | Quản lý | Metrics realtime, cảnh báo |
| `order:{id}` | Khách (qua link/QR) | ETA cập nhật, trạng thái đơn |

Dùng **Redis adapter** để scale nhiều instance API. State hàng đợi hiện tại của mỗi store được cache trong Redis (`queue:store:{id}`) dạng hash để AI service đọc mà không phải query Postgres.

### 4.2 Nguyên tắc UX cho ETA realtime

Đây là chi tiết **quyết định thành bại** mà đối thủ nước ngoài học được đắt giá:

1. **ETA hiển thị chỉ được giảm hoặc giữ nguyên** trong điều kiện bình thường.
2. Khi buộc phải tăng, tăng có kiểm soát (tối đa +2 phút/lần) và **kèm lời xin lỗi + lý do**.
3. Không bao giờ hiển thị đếm ngược tới 0 rồi vẫn chưa xong → dùng "còn khoảng 2 phút" thay vì "01:59".
4. Khi đơn quá hạn ETA, chuyển sang thông điệp trung thực: "Đơn của bạn đang được ưu tiên xử lý, xin lỗi vì sự chậm trễ."

---

## 5. Offline-first — điều kiện sống còn ở Việt Nam

**Nguyên tắc:** POS phải bán được hàng khi mất mạng hoàn toàn. Không thương lượng.

### 5.1 Kiến trúc

```
┌─────────────────── apps/pos (PWA) ───────────────────┐
│                                                       │
│  UI  ──►  Local Store (Zustand)                       │
│            │                                          │
│            ├──► IndexedDB (Dexie)                     │
│            │     ├── menu_snapshot   (đọc)            │
│            │     ├── pending_events  (ghi, có thứ tự) │
│            │     └── local_orders                     │
│            │                                          │
│            └──► Sync Engine                           │
│                  ├── Outbox pattern                   │
│                  ├── Exponential backoff              │
│                  └── Conflict resolution              │
└───────────────────────────────────────────────────────┘
```

### 5.2 Quy tắc xử lý xung đột

| Loại dữ liệu | Chiến lược | Lý do |
|---|---|---|
| Đơn hàng, thanh toán | **Append-only event log** — không bao giờ xung đột | Mỗi sự kiện có `client_event_id` (UUIDv7) để idempotent |
| Tồn kho | Server là chân lý; client chỉ tính tạm | Tránh bán quá số lượng |
| Menu, giá | Server → client một chiều | Client không được sửa |
| Số hóa đơn | Cấp dải số trước (pre-allocated range) cho mỗi terminal | Tránh trùng số khi offline |

**ID đơn hàng offline:** dùng UUIDv7 sinh ở client (có timestamp, sortable) + `terminal_code`. Khi sync, server nhận idempotent theo `client_event_id`.

---

## 6. AI Service — tách riêng, giao tiếp qua HTTP + Redis

### 6.1 Vì sao tách service Python

- Hệ sinh thái ML (LightGBM, SimPy, Polars, MLflow) chỉ có ở Python.
- Vòng đời triển khai khác nhau: model retrain hằng ngày, API deploy hằng tuần.
- Có thể scale độc lập (inference tốn CPU, API tốn I/O).

### 6.2 Giao diện

```
POST /v1/eta/predict        # đồng bộ, < 50ms — dự đoán ETA cho 1 đơn
POST /v1/eta/batch          # dự đoán lại toàn bộ hàng đợi (khi có sự kiện)
POST /v1/sequence/optimize  # trả về thứ tự làm món tối ưu
POST /v1/forecast/demand    # dự báo nhu cầu 7-14 ngày
POST /v1/schedule/optimize  # xếp ca nhân viên
POST /v1/simulate           # digital twin — chạy kịch bản what-if
GET  /v1/health             # + model version, latency p50/p95
```

**Latency budget cho `/eta/predict`:**
| Thành phần | Ngân sách |
|---|---|
| Đọc queue state từ Redis | 5ms |
| Chạy discrete-event simulation | 15ms |
| LightGBM inference | 5ms |
| Hiệu chỉnh + làm mượt | 5ms |
| Tổng (p95) | **< 50ms** |

Nếu AI service chết → API fallback về **ETA tĩnh** (tổng `base_prep_time` × hệ số tải). Không bao giờ để lỗi AI làm sập luồng bán hàng.

---

## 7. Bảo mật

| Lớp | Biện pháp |
|---|---|
| Xác thực | JWT access (15 phút) + refresh token (rotate, lưu httpOnly cookie). OAuth Google/Zalo. |
| Phân quyền | RBAC: `owner` / `manager` / `cashier` / `barista` / `accountant`. Kèm ABAC theo `store_id`. |
| Cách ly tenant | RLS (mục 3.2) + audit test bắt buộc trong CI |
| Dữ liệu nhạy cảm | Mã hóa cột (pgcrypto) cho SĐT khách, thông tin thanh toán. **Không bao giờ lưu số thẻ.** |
| Chống gian lận nội bộ | Mọi thao tác hủy đơn/giảm giá/mở két đều ghi `audit_log` với `user_id` + lý do bắt buộc |
| Rate limiting | Cloudflare + Redis token bucket theo tenant |
| Bí mật | Doppler / AWS Secrets Manager. Không bao giờ có `.env` trong repo. |
| Tuân thủ | Nghị định 13/2023/NĐ-CP về bảo vệ dữ liệu cá nhân — có trang chính sách, cơ chế xóa dữ liệu theo yêu cầu |

---

## 8. Observability

```
OpenTelemetry (traces + metrics)  →  Grafana Tempo + Prometheus
Structured logs (pino)            →  Loki
Error tracking                    →  Sentry (FE + BE)
Product analytics                 →  PostHog (self-host)
```

**Dashboard bắt buộc phải có từ ngày 1:**
1. `ETA accuracy` theo store, theo ngày (MAE, on-time rate)
2. `Label coverage` — % đơn có đủ mốc thời gian
3. `Sync lag` — độ trễ đồng bộ offline của từng terminal
4. `Order funnel` — đặt → nhận → bắt đầu làm → xong → giao

---

## 9. Môi trường & CI/CD

| Môi trường | Mục đích | Dữ liệu |
|---|---|---|
| `local` | Dev — docker-compose | Seed data giả lập |
| `preview` | Mỗi PR một môi trường | Snapshot ẩn danh |
| `staging` | QA, demo bán hàng | Dữ liệu giả lập nhưng realistic |
| `production` | Thật | — |

**Pipeline (GitHub Actions):**
```
lint → typecheck → unit test → build → integration test (testcontainers)
  → e2e (Playwright) → tenant-isolation test → deploy preview
  → [manual approve] → deploy staging → smoke test → deploy prod (canary 10%)
```

**Cổng chất lượng bắt buộc:**
- Coverage ≥ 70% cho `apps/api`, ≥ 85% cho `apps/ai-service`
- Không merge nếu tenant-isolation test fail
- Lighthouse CI: Performance ≥ 90, SEO = 100 cho `apps/web` (xem [tài liệu 05](05-seo-va-tang-truong.md))

---

**Tiếp theo:** [02 — Mô hình dữ liệu](02-mo-hinh-du-lieu.md)
