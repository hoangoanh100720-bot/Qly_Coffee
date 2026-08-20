# ARCHITECTURE.md — Qly Coffee

> **Tài liệu kiến trúc thi công.** Viết để dùng trực tiếp cho vibe coding: mọi bảng, mọi
> quy tắc nghiệp vụ, mọi API và mọi thuật toán đều được đặc tả đủ chi tiết để một AI
> coding assistant thực thi mà không phải đoán.
>
> **Cách dùng:** Đi tuần tự theo [§13 Lộ trình build](#13-lộ-trình-build-cho-vibe-coding).
> Mỗi bước có tiêu chí nghiệm thu. Không nhảy bước — bước sau phụ thuộc bước trước.

---

## Mục lục

| § | Nội dung |
|---|---|
| [00](#00-đọc-trước-khi-bắt-đầu) | Đọc trước khi bắt đầu — giả định & thay đổi so với kế hoạch cũ |
| [01](#01-sản-phẩm--phạm-vi) | Sản phẩm & phạm vi |
| [02](#02-tech-stack) | Tech stack & lý do chọn |
| [03](#03-cấu-trúc-thư-mục) | Cấu trúc thư mục |
| [04](#04-mô-hình-dữ-liệu) | Mô hình dữ liệu — Prisma schema đầy đủ |
| [05](#05-quy-tắc-nghiệp-vụ-cốt-lõi) | Quy tắc nghiệp vụ cốt lõi ⭐ |
| [06](#06-ai-engine--trợ-lý-tồn-kho--khuyến-mãi) | AI Engine — trợ lý tồn kho & khuyến mãi ⭐ |
| [07](#07-api-contract) | API contract |
| [08](#08-giao-diện--sitemap--màn-hình) | Giao diện — sitemap & màn hình |
| [09](#09-xác-thực--phân-quyền) | Xác thực & phân quyền |
| [10](#10-biến-môi-trường) | Biến môi trường |
| [11](#11-seed-data--menu-giới-trẻ--công-thức) | Seed data — menu giới trẻ + công thức |
| [12](#12-jobs-nền--lịch-chạy) | Jobs nền & lịch chạy |
| [13](#13-lộ-trình-build-cho-vibe-coding) | Lộ trình build ⭐ |
| [14](#14-testing) | Testing |
| [15](#15-mười-lỗi-hay-gặp-khi-vibe-coding-phần-này) | 10 lỗi hay gặp |

---

## 00. Đọc trước khi bắt đầu

### 0.1 Ba thay đổi so với kế hoạch trong `docs/`

| # | Thay đổi | Lý do |
|---|---|---|
| 1 | **Lõi AI đổi từ "dự đoán thời gian chờ" sang "quản trị tồn kho & xả hàng cận hạn"** | Theo yêu cầu mới. Động cơ ETA chuyển thành Phase tương lai (§1.4), không nằm trong phạm vi tài liệu này. |
| 2 | **Bỏ NestJS + service Python. Dùng Next.js full-stack** | Phạm vi mới không cần realtime KDS hay simulator. Một codebase, một ngôn ngữ, một lần deploy → vibe coding nhanh hơn nhiều lần. Nếu sau này lên SaaS đa tenant quy mô lớn thì tách backend riêng. |
| 3 | **Bỏ LightGBM khỏi MVP** | Dự báo tiêu thụ ở MVP dùng EWMA (trung bình trượt có trọng số) viết bằng TypeScript — đủ tốt và không cần hạ tầng Python. Nâng lên ML thật khi có ≥ 90 ngày dữ liệu. |

### 0.2 Giả định mình đã chốt (sửa nếu sai)

| Giả định | Ghi chú |
|---|---|
| **Một quán, một chi nhánh ở MVP** | Nhưng schema vẫn giữ cột `storeId` ở mọi bảng nghiệp vụ. Bật đa chi nhánh sau này chỉ là thêm filter + UI, không phải viết lại. |
| Khách đặt món **online tại chỗ hoặc mang đi**, không giao hàng | Giao hàng là Phase sau. |
| Thanh toán MVP: **tiền mặt + VietQR (chuyển khoản)** | Cổng thanh toán online là Phase sau. |
| Đơn vị tiền tệ: **VND, số nguyên, không có phần thập phân** | Lưu `Int` trong DB. Không bao giờ dùng `Float` cho tiền. |
| Múi giờ: **Asia/Ho_Chi_Minh** | Mọi phép tính "theo ngày" phải dùng múi giờ này, không dùng UTC. |

### 0.3 Quy ước bắt buộc trong toàn bộ codebase

```
Tiền tệ            → Int (đồng), không Float, không Decimal
Khối lượng/thể tích → Float, luôn quy về đơn vị cơ sở (g / ml / pcs)
Thời gian           → DateTime UTC trong DB, quy đổi Asia/Ho_Chi_Minh khi hiển thị/tính ngày
ID                  → String @default(cuid())
Xóa                 → Soft delete (deletedAt), không DELETE thật ở bảng nghiệp vụ
Sổ cái kho          → Append-only. Không bao giờ UPDATE hay DELETE StockMovement
Tên biến/hàm        → tiếng Anh. Nội dung hiển thị → tiếng Việt
```

---

## 01. Sản phẩm & phạm vi

### 1.1 Một câu mô tả

> Web bán hàng của quán cà phê với menu hiện đại cho giới trẻ, có công thức định lượng
> chuẩn cho từng món, tự động trừ nguyên liệu tồn kho theo từng đơn, và mỗi tối tự sinh
> **bản kế hoạch xả hàng cận hạn** gửi về trang quản lý.

### 1.2 Ba khối chức năng

```
┌─────────────────────────────────────────────────────────────────────┐
│  KHỐI A — CỬA HÀNG (công khai)                                      │
│  Trang chủ · Menu theo danh mục · Chi tiết món · Giỏ hàng           │
│  Đặt món · Theo dõi đơn · Banner khuyến mãi (do AI đề xuất)         │
├─────────────────────────────────────────────────────────────────────┤
│  KHỐI B — VẬN HÀNH (nội bộ)                                         │
│  Quản lý món & công thức định lượng                                 │
│  Quản lý nguyên liệu · Lô hàng & hạn dùng · Nhập kho · Kiểm kê      │
│  Xử lý đơn · Báo cáo doanh thu · Báo cáo hao hụt                    │
├─────────────────────────────────────────────────────────────────────┤
│  KHỐI C — AI ENGINE ⭐                                              │
│  C1. Auto-deduct: đơn hàng → phân rã công thức → trừ kho FEFO       │
│  C2. Guard: chặn bán khi thiếu nguyên liệu, cảnh báo sắp hết        │
│  C3. Daily Planner: phân tích rủi ro hết hạn → đề xuất khuyến mãi   │
│      → sinh bản kế hoạch có diễn giải → gửi trang quản lý           │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.3 Trong phạm vi MVP

- ✅ Menu công khai, đặt món online (tại chỗ / mang đi)
- ✅ Công thức định lượng theo món + biến thể (size) + topping
- ✅ Nguyên liệu, lô hàng có hạn dùng, nhập kho, kiểm kê, ghi nhận hao hụt
- ✅ Tự động trừ kho theo FEFO khi đơn được xác nhận
- ✅ Chặn bán / cảnh báo khi không đủ nguyên liệu
- ✅ Job cuối ngày: phân tích rủi ro hết hạn → kế hoạch khuyến mãi có AI diễn giải
- ✅ Trang quản lý: dashboard, duyệt/từ chối đề xuất khuyến mãi, áp dụng khuyến mãi
- ✅ Báo cáo: doanh thu, món bán chạy, hao hụt, giá vốn

### 1.4 Ngoài phạm vi MVP (Phase sau)

- ❌ Dự đoán thời gian chờ (ETA) — xem `docs/03-ai-du-doan-thoi-gian-cho.md`
- ❌ Đa chi nhánh, đa tenant SaaS
- ❌ Giao hàng, tích hợp GrabFood/ShopeeFood
- ❌ Hóa đơn điện tử
- ❌ Loyalty, tích điểm
- ❌ Ứng dụng di động native

---

## 02. Tech Stack

| Lớp | Công nghệ | Ghi chú |
|---|---|---|
| Framework | **Next.js 15** (App Router) | Cả FE và BE trong một app |
| Ngôn ngữ | **TypeScript** strict mode | `"strict": true`, không dùng `any` |
| Database | **PostgreSQL 16** | Neon / Supabase / Docker local |
| ORM | **Prisma 6** | Migration + type safety |
| Validation | **Zod** | Dùng chung cho form, API, env |
| UI | **Tailwind CSS 4** + **shadcn/ui** | |
| State (client) | **TanStack Query** + **Zustand** (giỏ hàng) | |
| Form | **react-hook-form** + `@hookform/resolvers/zod` | |
| Auth | **Auth.js v5** (NextAuth) — Credentials + Google | |
| Job nền | **Vercel Cron** (prod) / **node-cron** (self-host) | Gọi Route Handler có bảo vệ bằng secret |
| AI | **Anthropic SDK** — `claude-opus-5` | Chỉ dùng để diễn giải & viết nội dung, KHÔNG dùng để tính toán số |
| Biểu đồ | **Recharts** | |
| Ảnh | **UploadThing** hoặc **Cloudflare R2** | |
| Email/Thông báo | **Resend** | Gửi bản kế hoạch hằng ngày |
| Test | **Vitest** (unit) + **Playwright** (e2e) | |

### 2.1 Vì sao dùng Next.js full-stack thay vì tách backend

| | Next.js full-stack | Next.js + NestJS |
|---|---|---|
| Số codebase phải giữ đồng bộ | 1 | 2 |
| Type sharing FE ↔ BE | Tự nhiên | Phải sinh SDK |
| Số lần deploy | 1 | 2 |
| Phù hợp vibe coding | ✅ Rất tốt | ⚠️ Nhiều context switch |
| Khi nào nên tách | Khi cần worker chạy dài, nhiều consumer, hoặc đội ≥ 4 người | |

**Quy tắc để sau này tách được:** Toàn bộ logic nghiệp vụ nằm trong `src/server/services/*`,
**không bao giờ** viết logic trực tiếp trong Route Handler hay Server Component. Route Handler
chỉ làm: xác thực → validate → gọi service → trả response. Khi muốn tách sang NestJS, chỉ cần
bê nguyên thư mục `services` sang.

---

## 03. Cấu trúc thư mục

```
qly-coffee/
├── prisma/
│   ├── schema.prisma
│   ├── migrations/
│   └── seed.ts                       # menu + công thức + nguyên liệu mẫu
│
├── src/
│   ├── app/
│   │   ├── (shop)/                   # KHỐI A — công khai
│   │   │   ├── layout.tsx
│   │   │   ├── page.tsx              # trang chủ
│   │   │   ├── menu/
│   │   │   │   ├── page.tsx          # danh sách món
│   │   │   │   └── [slug]/page.tsx   # chi tiết món
│   │   │   ├── gio-hang/page.tsx
│   │   │   ├── dat-mon/page.tsx      # checkout
│   │   │   └── don-hang/[code]/page.tsx
│   │   │
│   │   ├── (admin)/                  # KHỐI B — nội bộ
│   │   │   ├── layout.tsx            # kiểm tra quyền ở đây
│   │   │   ├── dashboard/page.tsx
│   │   │   ├── mon-an/
│   │   │   │   ├── page.tsx
│   │   │   │   ├── moi/page.tsx
│   │   │   │   └── [id]/page.tsx     # sửa món + công thức định lượng
│   │   │   ├── nguyen-lieu/
│   │   │   │   ├── page.tsx
│   │   │   │   └── [id]/page.tsx
│   │   │   ├── kho/
│   │   │   │   ├── page.tsx          # tồn kho theo lô + hạn dùng
│   │   │   │   ├── nhap/page.tsx     # nhập hàng
│   │   │   │   ├── kiem-ke/page.tsx
│   │   │   │   └── hao-hut/page.tsx
│   │   │   ├── don-hang/
│   │   │   │   ├── page.tsx
│   │   │   │   └── [id]/page.tsx
│   │   │   ├── ke-hoach/             # ⭐ bản kế hoạch AI
│   │   │   │   ├── page.tsx          # danh sách kế hoạch theo ngày
│   │   │   │   └── [date]/page.tsx   # chi tiết + duyệt đề xuất
│   │   │   ├── khuyen-mai/page.tsx
│   │   │   └── bao-cao/page.tsx
│   │   │
│   │   ├── api/
│   │   │   ├── auth/[...nextauth]/route.ts
│   │   │   ├── orders/route.ts
│   │   │   ├── orders/[id]/status/route.ts
│   │   │   ├── menu/availability/route.ts
│   │   │   ├── inventory/receive/route.ts
│   │   │   ├── inventory/count/route.ts
│   │   │   ├── inventory/waste/route.ts
│   │   │   ├── plans/[date]/route.ts
│   │   │   ├── plans/[date]/decide/route.ts
│   │   │   └── cron/
│   │   │       ├── daily-plan/route.ts     # ⭐ job cuối ngày
│   │   │       ├── expiry-check/route.ts
│   │   │       └── refresh-availability/route.ts
│   │   │
│   │   ├── layout.tsx
│   │   └── globals.css
│   │
│   ├── server/                       # ⭐ TẤT CẢ logic nghiệp vụ ở đây
│   │   ├── db.ts                     # Prisma singleton
│   │   ├── auth.ts                   # cấu hình Auth.js
│   │   ├── services/
│   │   │   ├── menu.service.ts
│   │   │   ├── recipe.service.ts     # phân rã công thức định lượng
│   │   │   ├── inventory.service.ts  # ⭐ trừ kho, FEFO, sổ cái
│   │   │   ├── order.service.ts      # ⭐ vòng đời đơn + trừ kho
│   │   │   ├── availability.service.ts
│   │   │   ├── forecast.service.ts   # EWMA dự báo tiêu thụ
│   │   │   ├── waste-risk.service.ts # ⭐ tính rủi ro hết hạn
│   │   │   ├── promotion.service.ts  # ⭐ sinh phương án khuyến mãi
│   │   │   ├── plan.service.ts       # ⭐ điều phối job cuối ngày
│   │   │   └── report.service.ts
│   │   ├── ai/
│   │   │   ├── client.ts             # Anthropic client
│   │   │   ├── prompts/
│   │   │   │   └── daily-plan.ts     # ⭐ prompt sinh kế hoạch
│   │   │   └── schemas.ts            # Zod schema cho output của AI
│   │   └── lib/
│   │       ├── units.ts              # quy đổi đơn vị
│   │       ├── money.ts              # xử lý tiền VND
│   │       ├── date.ts               # múi giờ VN, ngày kinh doanh
│   │       └── errors.ts             # lớp lỗi nghiệp vụ
│   │
│   ├── components/
│   │   ├── ui/                       # shadcn
│   │   ├── shop/
│   │   └── admin/
│   │
│   ├── lib/
│   │   ├── utils.ts
│   │   └── validations/              # Zod schema dùng chung FE/BE
│   │
│   └── types/
│
├── .env.example
├── docker-compose.yml                # postgres cho dev
└── ARCHITECTURE.md                   # file này
```

---

## 04. Mô hình dữ liệu

> Copy nguyên khối này vào `prisma/schema.prisma`.

### 4.1 Cấu hình

```prisma
generator client {
  provider = "prisma-client-js"
}

datasource db {
  provider = "postgresql"
  url      = env("DATABASE_URL")
}
```

### 4.2 Người dùng & cửa hàng

```prisma
enum UserRole {
  OWNER       // toàn quyền
  MANAGER     // vận hành, duyệt kế hoạch
  STAFF       // xử lý đơn, nhập kho
  CUSTOMER    // khách hàng
}

model User {
  id            String    @id @default(cuid())
  email         String    @unique
  phone         String?   @unique
  name          String
  passwordHash  String?
  image         String?
  role          UserRole  @default(CUSTOMER)
  emailVerified DateTime?
  createdAt     DateTime  @default(now())
  updatedAt     DateTime  @updatedAt
  deletedAt     DateTime?

  orders        Order[]
  stockMovements StockMovement[]
  planDecisions PlanDecision[]
  accounts      Account[]
  sessions      Session[]
}

model Store {
  id            String   @id @default(cuid())
  name          String
  slug          String   @unique
  address       String
  phone         String
  email         String?
  openTime      String   @default("07:00")   // HH:mm
  closeTime     String   @default("22:00")
  timezone      String   @default("Asia/Ho_Chi_Minh")
  // Ngày kinh doanh kết thúc lúc mấy giờ — job cuối ngày chạy sau mốc này
  businessDayEndHour Int @default(23)
  isOpen        Boolean  @default(true)
  createdAt     DateTime @default(now())
  updatedAt     DateTime @updatedAt
}

// Auth.js models — sinh theo tài liệu chính thức của @auth/prisma-adapter
model Account { /* ... theo chuẩn Auth.js ... */
  id                String  @id @default(cuid())
  userId            String
  type              String
  provider          String
  providerAccountId String
  refresh_token     String? @db.Text
  access_token      String? @db.Text
  expires_at        Int?
  token_type        String?
  scope             String?
  id_token          String? @db.Text
  session_state     String?
  user              User    @relation(fields: [userId], references: [id], onDelete: Cascade)
  @@unique([provider, providerAccountId])
}

model Session {
  id           String   @id @default(cuid())
  sessionToken String   @unique
  userId       String
  expires      DateTime
  user         User     @relation(fields: [userId], references: [id], onDelete: Cascade)
}
```

### 4.3 Nguyên liệu & đơn vị

```prisma
// Đơn vị CƠ SỞ — mọi phép tính tồn kho đều quy về đây
enum BaseUnit {
  G     // gram        — nguyên liệu khô, bột, hạt
  ML    // mililit     — chất lỏng
  PCS   // cái/chiếc   — đếm được (ly, ống hút, quả trứng)
}

enum IngredientCategory {
  COFFEE        // cà phê
  TEA           // trà
  DAIRY         // sữa, kem, phô mai
  SYRUP         // siro, sốt
  FRUIT         // trái cây
  TOPPING       // trân châu, thạch, kem cheese
  POWDER        // bột matcha, cacao, bột kem
  SWEETENER     // đường, mật ong
  PACKAGING     // ly, nắp, ống hút, túi
  OTHER
}

model Ingredient {
  id            String              @id @default(cuid())
  storeId       String
  name          String                                    // "Sữa tươi không đường"
  sku           String              @unique                // "DAIRY-MILK-001"
  category      IngredientCategory
  baseUnit      BaseUnit                                   // đơn vị lưu kho & tính toán

  // Ngưỡng cảnh báo — tính theo baseUnit
  minStockLevel Float               @default(0)            // dưới mức này => cảnh báo
  reorderPoint  Float               @default(0)            // dưới mức này => gợi ý nhập

  // Hạn dùng mặc định khi nhập lô mới (ngày). null = không có hạn
  defaultShelfLifeDays Int?
  // Số ngày trước hạn bắt đầu coi là "cận hạn"
  expiryWarningDays    Int          @default(7)

  // Hao hụt tự nhiên khi chế biến, ví dụ 0.03 = hao 3%
  wastageRate   Float               @default(0)

  // Giá vốn tham chiếu (đồng / 1 baseUnit) — cập nhật theo bình quân gia quyền khi nhập
  avgUnitCost   Int                 @default(0)

  isActive      Boolean             @default(true)
  imageUrl      String?
  note          String?
  createdAt     DateTime            @default(now())
  updatedAt     DateTime            @updatedAt
  deletedAt     DateTime?

  lots          InventoryLot[]
  movements     StockMovement[]
  recipeItems   RecipeItem[]
  purchaseItems PurchaseOrderItem[]

  @@index([storeId, category])
  @@index([storeId, isActive])
}

// Đơn vị mua hàng — quy đổi sang baseUnit
// Ví dụ: sữa tươi mua theo "thùng" = 12 hộp × 1000ml = 12000 ml
model PurchaseUnit {
  id            String     @id @default(cuid())
  ingredientId  String
  name          String                 // "Thùng 12 hộp 1L"
  conversionQty Float                  // 12000 (số baseUnit trong 1 đơn vị mua)
  isDefault     Boolean    @default(false)

  @@index([ingredientId])
}
```

### 4.4 Món & công thức định lượng ⭐

```prisma
model Category {
  id        String    @id @default(cuid())
  storeId   String
  name      String                     // "Cà phê", "Trà sữa", "Đá xay"
  slug      String    @unique
  imageUrl  String?
  sortOrder Int       @default(0)
  isActive  Boolean   @default(true)
  deletedAt DateTime?

  products  Product[]
}

model Product {
  id            String    @id @default(cuid())
  storeId       String
  categoryId    String
  name          String                        // "Cà phê muối"
  slug          String    @unique
  description   String?
  imageUrl      String?

  basePrice     Int                           // giá size mặc định, VND
  // Giá vốn TÍNH TOÁN từ công thức — cập nhật bởi recipe.service
  computedCost  Int       @default(0)

  isActive      Boolean   @default(true)
  isFeatured    Boolean   @default(false)
  sortOrder     Int       @default(0)

  // Trạng thái khả dụng — cập nhật bởi availability.service
  isAvailable   Boolean   @default(true)
  unavailableReason String?                   // "Hết sữa tươi"
  maxServings   Int?                          // số ly tối đa còn làm được, null = không giới hạn

  tags          String[]  @default([])        // ["best-seller", "mới", "ít đường"]

  createdAt     DateTime  @default(now())
  updatedAt     DateTime  @updatedAt
  deletedAt     DateTime?

  category      Category           @relation(fields: [categoryId], references: [id])
  variants      ProductVariant[]
  recipeItems   RecipeItem[]                  // công thức của size mặc định
  modifierLinks ProductModifier[]
  orderItems    OrderItem[]
  promotions    Promotion[]

  @@index([storeId, categoryId, isActive])
  @@index([storeId, isAvailable])
}

// Biến thể: size M / L, nóng / đá
model ProductVariant {
  id            String   @id @default(cuid())
  productId     String
  name          String                        // "Size L"
  priceDelta    Int      @default(0)          // cộng thêm vào basePrice
  // Hệ số nhân công thức. Size L = 1.4 => mọi nguyên liệu × 1.4
  recipeMultiplier Float @default(1)
  isDefault     Boolean  @default(false)
  sortOrder     Int      @default(0)
  isActive      Boolean  @default(true)

  product       Product  @relation(fields: [productId], references: [id], onDelete: Cascade)
  orderItems    OrderItem[]

  @@index([productId])
}

// ⭐ CÔNG THỨC ĐỊNH LƯỢNG — trái tim của việc trừ kho
model RecipeItem {
  id            String     @id @default(cuid())
  productId     String
  ingredientId  String

  // Số lượng cho MỘT ly size mặc định, tính theo Ingredient.baseUnit
  quantity      Float                          // vd: 180 (ml sữa) hoặc 18 (g cà phê)

  isOptional    Boolean    @default(false)     // khách có thể yêu cầu bỏ
  note          String?                        // "đánh bông trước khi rót"
  sortOrder     Int        @default(0)

  product       Product    @relation(fields: [productId], references: [id], onDelete: Cascade)
  ingredient    Ingredient @relation(fields: [ingredientId], references: [id])

  @@unique([productId, ingredientId])
  @@index([ingredientId])
}

// Nhóm tùy chọn: mức đường, mức đá, topping
model ModifierGroup {
  id          String            @id @default(cuid())
  storeId     String
  name        String                              // "Topping"
  minSelect   Int               @default(0)
  maxSelect   Int               @default(1)
  isRequired  Boolean           @default(false)
  sortOrder   Int               @default(0)

  modifiers   Modifier[]
  productLinks ProductModifier[]
}

model Modifier {
  id              String   @id @default(cuid())
  modifierGroupId String
  name            String                          // "Trân châu đen"
  priceDelta      Int      @default(0)
  sortOrder       Int      @default(0)
  isActive        Boolean  @default(true)

  group           ModifierGroup     @relation(fields: [modifierGroupId], references: [id], onDelete: Cascade)
  recipeItems     ModifierRecipeItem[]

  @@index([modifierGroupId])
}

// Công thức riêng của topping: "Trân châu đen" => +30g trân châu
model ModifierRecipeItem {
  id           String   @id @default(cuid())
  modifierId   String
  ingredientId String
  quantity     Float                              // theo baseUnit của ingredient

  modifier     Modifier @relation(fields: [modifierId], references: [id], onDelete: Cascade)

  @@unique([modifierId, ingredientId])
  @@index([ingredientId])
}

model ProductModifier {
  productId       String
  modifierGroupId String
  sortOrder       Int    @default(0)

  product         Product       @relation(fields: [productId], references: [id], onDelete: Cascade)
  group           ModifierGroup @relation(fields: [modifierGroupId], references: [id], onDelete: Cascade)

  @@id([productId, modifierGroupId])
}
```

### 4.5 Kho — lô hàng & sổ cái ⭐

```prisma
enum LotStatus {
  ACTIVE      // đang dùng được
  DEPLETED    // đã dùng hết
  EXPIRED     // đã quá hạn, không dùng nữa
  DISPOSED    // đã tiêu hủy
}

// Lô hàng — mỗi lần nhập tạo một lô có hạn dùng riêng
model InventoryLot {
  id            String     @id @default(cuid())
  storeId       String
  ingredientId  String
  lotCode       String                             // "SUA-20260810-01"

  receivedQty   Float                              // số lượng nhập, theo baseUnit
  remainingQty  Float                              // còn lại, theo baseUnit
  unitCost      Int                                // giá vốn 1 baseUnit tại thời điểm nhập

  receivedAt    DateTime   @default(now())
  expiryDate    DateTime?                          // null = không có hạn
  status        LotStatus  @default(ACTIVE)

  supplierId    String?
  purchaseOrderItemId String?
  note          String?

  createdAt     DateTime   @default(now())
  updatedAt     DateTime   @updatedAt

  ingredient    Ingredient @relation(fields: [ingredientId], references: [id])
  movements     StockMovement[]

  @@unique([storeId, lotCode])
  // Index phục vụ truy vấn FEFO — quan trọng nhất của cả schema
  @@index([storeId, ingredientId, status, expiryDate])
  @@index([storeId, expiryDate, status])
}

enum MovementType {
  PURCHASE_IN     // nhập hàng
  SALE_OUT        // bán hàng (trừ theo công thức)
  WASTE           // hao hụt / đổ bỏ
  EXPIRED_OUT     // tiêu hủy do hết hạn
  ADJUST_IN       // kiểm kê thừa
  ADJUST_OUT      // kiểm kê thiếu
  RETURN_IN       // hoàn lại do hủy đơn
  PRODUCTION_OUT  // dùng để chế biến nguyên liệu trung gian
  PRODUCTION_IN   // thành phẩm trung gian nhập kho
}

// ⭐ SỔ CÁI KHO — APPEND ONLY. Không bao giờ UPDATE hay DELETE.
// Tồn kho thực = SUM(remainingQty) của các lot ACTIVE.
// Sổ cái là bằng chứng đối soát, lot là trạng thái hiện tại.
model StockMovement {
  id             String       @id @default(cuid())
  storeId        String
  ingredientId   String
  lotId          String?                          // null với ADJUST khi chưa xác định lô

  type           MovementType
  // Dương = nhập, Âm = xuất. Luôn theo baseUnit.
  quantityDelta  Float
  unitCost       Int          @default(0)
  totalCost      Int          @default(0)         // |quantityDelta| × unitCost

  // Truy vết nguồn gốc
  referenceType  String?                          // "ORDER" | "PURCHASE" | "COUNT" | "WASTE"
  referenceId    String?
  reason         String?

  // ⭐ Idempotency — chống trừ kho hai lần khi retry
  idempotencyKey String?      @unique

  actorId        String?
  occurredAt     DateTime     @default(now())
  createdAt      DateTime     @default(now())

  ingredient     Ingredient   @relation(fields: [ingredientId], references: [id])
  lot            InventoryLot? @relation(fields: [lotId], references: [id])
  actor          User?        @relation(fields: [actorId], references: [id])

  @@index([storeId, ingredientId, occurredAt])
  @@index([storeId, type, occurredAt])
  @@index([referenceType, referenceId])
}

model Supplier {
  id            String   @id @default(cuid())
  storeId       String
  name          String
  phone         String?
  email         String?
  address       String?
  leadTimeDays  Int      @default(1)
  minOrderValue Int      @default(0)
  isActive      Boolean  @default(true)
  deletedAt     DateTime?

  purchaseOrders PurchaseOrder[]
}

enum PurchaseOrderStatus {
  DRAFT
  ORDERED
  PARTIALLY_RECEIVED
  RECEIVED
  CANCELLED
}

model PurchaseOrder {
  id            String              @id @default(cuid())
  storeId       String
  supplierId    String
  code          String              @unique
  status        PurchaseOrderStatus @default(DRAFT)
  orderedAt     DateTime?
  expectedAt    DateTime?
  receivedAt    DateTime?
  totalCost     Int                 @default(0)
  note          String?
  // true nếu do AI đề xuất tự động
  isAiGenerated Boolean             @default(false)
  createdAt     DateTime            @default(now())

  supplier      Supplier            @relation(fields: [supplierId], references: [id])
  items         PurchaseOrderItem[]
}

model PurchaseOrderItem {
  id              String        @id @default(cuid())
  purchaseOrderId String
  ingredientId    String
  quantity        Float                          // theo baseUnit
  unitCost        Int
  receivedQty     Float         @default(0)
  expiryDate      DateTime?

  purchaseOrder   PurchaseOrder @relation(fields: [purchaseOrderId], references: [id], onDelete: Cascade)
  ingredient      Ingredient    @relation(fields: [ingredientId], references: [id])
}

// Phiếu kiểm kê
model StockCount {
  id          String   @id @default(cuid())
  storeId     String
  code        String   @unique
  countedAt   DateTime @default(now())
  actorId     String
  note        String?
  isFinalized Boolean  @default(false)

  lines       StockCountLine[]
}

model StockCountLine {
  id           String     @id @default(cuid())
  stockCountId String
  ingredientId String
  systemQty    Float                            // hệ thống ghi nhận
  countedQty   Float                            // thực đếm
  varianceQty  Float                            // countedQty - systemQty
  varianceCost Int
  note         String?

  stockCount   StockCount @relation(fields: [stockCountId], references: [id], onDelete: Cascade)
}
```

### 4.6 Đơn hàng

```prisma
enum OrderStatus {
  PENDING      // khách vừa đặt, chưa xác nhận
  CONFIRMED    // đã xác nhận => ĐÃ TRỪ KHO
  PREPARING    // đang pha chế
  READY        // đã xong, chờ khách nhận
  COMPLETED    // đã giao cho khách
  CANCELLED    // đã hủy => HOÀN KHO nếu đã trừ
}

enum OrderType {
  DINE_IN
  TAKEAWAY
}

enum PaymentMethod {
  CASH
  BANK_TRANSFER   // VietQR
}

enum PaymentStatus {
  UNPAID
  PAID
  REFUNDED
}

model Order {
  id             String        @id @default(cuid())
  storeId        String
  code           String        @unique               // "QC-260810-0042"
  userId         String?                             // null nếu khách vãng lai
  customerName   String
  customerPhone  String
  orderType      OrderType     @default(TAKEAWAY)
  note           String?

  status         OrderStatus   @default(PENDING)
  paymentMethod  PaymentMethod @default(CASH)
  paymentStatus  PaymentStatus @default(UNPAID)

  subtotal       Int
  discountTotal  Int           @default(0)
  grandTotal     Int
  // Tổng giá vốn nguyên liệu thực tế đã trừ — điền khi CONFIRMED
  costTotal      Int           @default(0)

  appliedPromotionId String?

  // ⭐ Cờ chống trừ kho hai lần
  stockDeducted  Boolean       @default(false)
  stockReturned  Boolean       @default(false)

  placedAt       DateTime      @default(now())
  confirmedAt    DateTime?
  readyAt        DateTime?
  completedAt    DateTime?
  cancelledAt    DateTime?
  cancelReason   String?

  createdAt      DateTime      @default(now())
  updatedAt      DateTime      @updatedAt

  user           User?         @relation(fields: [userId], references: [id])
  items          OrderItem[]

  @@index([storeId, status, placedAt])
  @@index([storeId, placedAt])
  @@index([customerPhone])
}

model OrderItem {
  id            String          @id @default(cuid())
  orderId       String
  productId     String
  variantId     String?

  productName   String                              // snapshot tên tại thời điểm đặt
  variantName   String?
  quantity      Int
  unitPrice     Int                                 // snapshot giá
  lineTotal     Int

  // Snapshot topping đã chọn: [{ modifierId, name, priceDelta }]
  modifiers     Json            @default("[]")
  note          String?

  product       Product         @relation(fields: [productId], references: [id])
  variant       ProductVariant? @relation(fields: [variantId], references: [id])
  order         Order           @relation(fields: [orderId], references: [id], onDelete: Cascade)

  @@index([orderId])
  @@index([productId])
}
```

### 4.7 Khuyến mãi & Kế hoạch AI ⭐

```prisma
enum PromotionType {
  PERCENT_OFF     // giảm %
  AMOUNT_OFF      // giảm số tiền
  COMBO           // mua kèm
}

enum PromotionStatus {
  DRAFT
  ACTIVE
  PAUSED
  EXPIRED
}

model Promotion {
  id            String          @id @default(cuid())
  storeId       String
  name          String
  description   String?
  type          PromotionType
  value         Int                                 // 20 (%) hoặc 10000 (đồng)
  status        PromotionStatus @default(DRAFT)

  startsAt      DateTime
  endsAt        DateTime
  maxRedemptions Int?
  redemptionCount Int           @default(0)

  productId     String?                             // null = áp dụng toàn menu
  bannerText    String?                             // hiển thị trên trang shop

  // Truy vết: khuyến mãi này sinh từ đề xuất AI nào
  sourcePlanId  String?
  sourceSuggestionId String?

  createdAt     DateTime        @default(now())
  updatedAt     DateTime        @updatedAt

  product       Product?        @relation(fields: [productId], references: [id])

  @@index([storeId, status, startsAt, endsAt])
}

// ⭐ BẢN KẾ HOẠCH HẰNG NGÀY DO AI SINH
model DailyPlan {
  id            String   @id @default(cuid())
  storeId       String
  // Ngày kinh doanh, dạng YYYY-MM-DD theo giờ VN
  businessDate  String
  generatedAt   DateTime @default(now())

  // ---- Phần SỐ LIỆU: do code tính, KHÔNG do AI ----
  // Tổng giá trị nguyên liệu có nguy cơ bỏ đi (VND)
  totalValueAtRisk   Int   @default(0)
  criticalCount      Int   @default(0)     // nguyên liệu hết hạn trong ≤ 2 ngày
  warningCount       Int   @default(0)     // hết hạn trong 3–7 ngày
  lowStockCount      Int   @default(0)     // dưới ngưỡng tối thiểu

  // Snapshot phân tích đầy đủ để hiển thị biểu đồ, xem schema §6.2
  riskAnalysis  Json

  // ---- Phần DIỄN GIẢI: do Claude sinh ----
  headline      String?                    // "3 nguyên liệu cần xả trong 48 giờ"
  summary       String?  @db.Text          // đoạn tóm tắt tình hình
  aiModel       String?                    // "claude-opus-5"
  aiTokensUsed  Int      @default(0)
  aiError       String?                    // nếu gọi AI thất bại

  status        String   @default("NEW")   // NEW | REVIEWED | ARCHIVED

  suggestions   PlanSuggestion[]
  decisions     PlanDecision[]

  @@unique([storeId, businessDate])
  @@index([storeId, generatedAt])
}

enum SuggestionType {
  DISCOUNT          // giảm giá đẩy hàng
  BUNDLE            // combo
  STAFF_PUSH        // nhân viên chủ động mời khách
  REDUCE_PURCHASE   // giảm lượng nhập kỳ tới
  DISPOSE           // không cứu được, chuẩn bị hủy
  RESTOCK           // sắp hết, cần nhập
}

enum SuggestionPriority {
  CRITICAL
  HIGH
  MEDIUM
  LOW
}

model PlanSuggestion {
  id            String             @id @default(cuid())
  dailyPlanId   String
  type          SuggestionType
  priority      SuggestionPriority

  // Đối tượng liên quan
  ingredientId  String?
  productId     String?
  lotId         String?

  title         String                                  // "Giảm 20% Trà đào cam sả trong 2 ngày"
  reasoning     String   @db.Text                       // do AI viết, dựa trên số liệu

  // ---- Con số do CODE tính, AI không được sửa ----
  discountPercent    Int?                               // 20
  targetUnits        Int?                               // cần bán thêm bao nhiêu ly
  expectedWasteAvoided Int?                             // VND cứu được
  expectedMarginImpact Int?                             // VND lãi/lỗ ròng
  daysUntilExpiry    Int?
  quantityAtRisk     Float?

  suggestedFrom DateTime?
  suggestedTo   DateTime?
  bannerCopy    String?                                 // nội dung banner do AI viết

  sortOrder     Int                @default(0)

  plan          DailyPlan          @relation(fields: [dailyPlanId], references: [id], onDelete: Cascade)
  decisions     PlanDecision[]

  @@index([dailyPlanId, priority])
}

enum DecisionAction {
  APPROVED
  REJECTED
  MODIFIED
}

model PlanDecision {
  id             String         @id @default(cuid())
  dailyPlanId    String
  suggestionId   String
  action         DecisionAction
  actorId        String
  note           String?
  // Nếu MODIFIED: giá trị người dùng sửa lại
  modifiedPayload Json?
  createdPromotionId String?
  decidedAt      DateTime       @default(now())

  plan           DailyPlan      @relation(fields: [dailyPlanId], references: [id], onDelete: Cascade)
  suggestion     PlanSuggestion @relation(fields: [suggestionId], references: [id], onDelete: Cascade)
  actor          User           @relation(fields: [actorId], references: [id])

  @@index([dailyPlanId])
}

// Lưu lịch sử tiêu thụ đã tổng hợp — dùng cho dự báo EWMA
model DailyConsumption {
  id            String   @id @default(cuid())
  storeId       String
  ingredientId  String
  businessDate  String                             // YYYY-MM-DD
  quantityUsed  Float                              // theo baseUnit
  costUsed      Int
  createdAt     DateTime @default(now())

  @@unique([storeId, ingredientId, businessDate])
  @@index([storeId, ingredientId, businessDate])
}

model DailySales {
  id            String   @id @default(cuid())
  storeId       String
  productId     String
  businessDate  String
  unitsSold     Int
  revenue       Int
  cost          Int

  @@unique([storeId, productId, businessDate])
  @@index([storeId, productId, businessDate])
}
```

---

## 05. Quy tắc nghiệp vụ cốt lõi

> Đây là phần **quan trọng nhất** của tài liệu. Vibe coding hay sai ở chính những quy tắc này.

### 5.1 Đơn vị và quy đổi

```typescript
// src/server/lib/units.ts

export const BASE_UNITS = ['G', 'ML', 'PCS'] as const;
export type BaseUnit = (typeof BASE_UNITS)[number];

// Bảng quy đổi các đơn vị nhập hàng phổ biến sang đơn vị cơ sở
export const UNIT_CONVERSIONS: Record<string, { base: BaseUnit; factor: number }> = {
  KG:    { base: 'G',  factor: 1000 },
  G:     { base: 'G',  factor: 1 },
  L:     { base: 'ML', factor: 1000 },
  ML:    { base: 'ML', factor: 1 },
  PCS:   { base: 'PCS', factor: 1 },
  BOX:   { base: 'PCS', factor: 1 },   // hệ số thật lấy từ PurchaseUnit.conversionQty
};

/** Quy đổi về đơn vị cơ sở. Ném lỗi nếu không cùng nhóm đơn vị. */
export function toBaseUnit(qty: number, from: string, targetBase: BaseUnit): number {
  const conv = UNIT_CONVERSIONS[from.toUpperCase()];
  if (!conv) throw new Error(`Đơn vị không hợp lệ: ${from}`);
  if (conv.base !== targetBase) {
    throw new Error(`Không thể quy đổi ${from} sang ${targetBase}`);
  }
  return qty * conv.factor;
}

/** Làm tròn về 4 chữ số thập phân để tránh lỗi dấu phẩy động tích lũy. */
export function roundQty(qty: number): number {
  return Math.round(qty * 10000) / 10000;
}
```

**Quy tắc bắt buộc:**
- Mọi số lượng lưu trong DB (`RecipeItem.quantity`, `InventoryLot.remainingQty`,
  `StockMovement.quantityDelta`) **đều là đơn vị cơ sở của nguyên liệu đó**.
- Quy đổi chỉ xảy ra ở ranh giới nhập liệu (form nhập hàng) và hiển thị.
- Luôn gọi `roundQty()` sau mỗi phép cộng/trừ số lượng.

---

### 5.2 Phân rã công thức định lượng

**Đầu vào:** một `OrderItem` (productId, variantId, modifiers, quantity)
**Đầu ra:** danh sách `{ ingredientId, quantity }` — tổng nguyên liệu cần dùng.

```typescript
// src/server/services/recipe.service.ts

export interface IngredientRequirement {
  ingredientId: string;
  ingredientName: string;
  baseUnit: BaseUnit;
  quantity: number;        // đã nhân số lượng ly và hệ số size
  unitCost: number;
}

/**
 * Phân rã một dòng đơn hàng thành nhu cầu nguyên liệu.
 *
 * Công thức:
 *   qty(nguyên liệu) = [ recipeQty × variantMultiplier + Σ modifierQty ]
 *                      × (1 + wastageRate)
 *                      × orderQuantity
 *
 * Lưu ý thứ tự: hệ số size CHỈ nhân vào công thức gốc, KHÔNG nhân vào topping.
 * (Thêm trân châu vào size L vẫn là 30g trân châu, không phải 42g.)
 */
export async function explodeOrderItem(input: {
  productId: string;
  variantId?: string | null;
  modifierIds: string[];
  quantity: number;
}): Promise<IngredientRequirement[]> {
  const [product, variant, modifierRecipes] = await Promise.all([
    db.product.findUniqueOrThrow({
      where: { id: input.productId },
      include: { recipeItems: { include: { ingredient: true } } },
    }),
    input.variantId
      ? db.productVariant.findUniqueOrThrow({ where: { id: input.variantId } })
      : null,
    input.modifierIds.length
      ? db.modifierRecipeItem.findMany({
          where: { modifierId: { in: input.modifierIds } },
          include: { /* cần join ingredient */ },
        })
      : [],
  ]);

  const multiplier = variant?.recipeMultiplier ?? 1;
  const acc = new Map<string, IngredientRequirement>();

  // 1) Công thức gốc × hệ số size
  for (const ri of product.recipeItems) {
    add(acc, ri.ingredient, ri.quantity * multiplier);
  }

  // 2) Topping — KHÔNG nhân hệ số size
  for (const mr of modifierRecipes) {
    add(acc, mr.ingredient, mr.quantity);
  }

  // 3) Hao hụt chế biến + nhân số ly
  return Array.from(acc.values()).map((r) => ({
    ...r,
    quantity: roundQty(r.quantity * (1 + r.wastageRate) * input.quantity),
  }));
}
```

**Tính giá vốn món (`Product.computedCost`):**
```
computedCost = Σ ( recipeItem.quantity × ingredient.avgUnitCost × (1 + wastageRate) )
```
Chạy lại mỗi khi: sửa công thức, sửa `avgUnitCost` (tức mỗi lần nhập hàng), hoặc theo job hằng đêm.

---

### 5.3 Vòng đời trừ kho ⭐

Đây là quy tắc mà **sai một lần là hỏng toàn bộ số liệu**.

```
   Khách đặt món
        │
        ▼
   ┌──────────┐   Kiểm tra khả dụng (KHÔNG trừ kho)
   │ PENDING  │   → nếu thiếu: từ chối ngay, báo món nào thiếu
   └────┬─────┘
        │ nhân viên xác nhận / tự động xác nhận
        ▼
   ┌──────────┐   ⭐ TRỪ KHO TẠI ĐÂY (FEFO), trong 1 transaction
   │CONFIRMED │   → tạo StockMovement type=SALE_OUT cho từng nguyên liệu
   └────┬─────┘   → set Order.stockDeducted = true
        │         → set Order.costTotal
        ▼
   ┌──────────┐
   │PREPARING │   không đụng vào kho
   └────┬─────┘
        ▼
   ┌──────────┐
   │  READY   │   không đụng vào kho
   └────┬─────┘
        ▼
   ┌──────────┐
   │COMPLETED │   không đụng vào kho
   └──────────┘

   Hủy đơn từ bất kỳ trạng thái nào:
   ┌──────────┐   Nếu stockDeducted = true và stockReturned = false:
   │CANCELLED │   → tạo StockMovement type=RETURN_IN (đảo dấu)
   └──────────┘   → cộng lại remainingQty vào đúng lô đã trừ
                  → set stockReturned = true
```

**Vì sao trừ ở `CONFIRMED` chứ không phải `PENDING` hay `COMPLETED`:**

| Thời điểm | Vấn đề |
|---|---|
| `PENDING` | Khách bỏ giỏ hàng giữa chừng → kho bị trừ oan, phải viết cơ chế hết hạn giữ chỗ |
| `COMPLETED` | Trong lúc pha chế, hệ thống vẫn tưởng còn hàng → bán quá số lượng |
| **`CONFIRMED`** ✅ | Đúng lúc quán cam kết làm món. Đơn giản, không cần giữ chỗ. |

**Cài đặt (bắt buộc dùng transaction + khóa hàng):**

```typescript
// src/server/services/order.service.ts

export async function confirmOrder(orderId: string, actorId: string) {
  return db.$transaction(async (tx) => {
    // 1) Khóa đơn, kiểm tra trạng thái và cờ chống trùng
    const order = await tx.order.findUniqueOrThrow({
      where: { id: orderId },
      include: { items: true },
    });
    if (order.status !== 'PENDING') {
      throw new BusinessError('Đơn đã được xử lý trước đó');
    }
    if (order.stockDeducted) {
      throw new BusinessError('Đơn này đã trừ kho rồi');
    }

    // 2) Phân rã toàn bộ đơn thành nhu cầu nguyên liệu (gộp lại)
    const requirements = await aggregateRequirements(order.items);

    // 3) Trừ kho theo FEFO — xem §5.4
    let costTotal = 0;
    for (const req of requirements) {
      const { cost } = await consumeFefo(tx, {
        storeId: order.storeId,
        ingredientId: req.ingredientId,
        quantity: req.quantity,
        referenceType: 'ORDER',
        referenceId: order.id,
        idempotencyKey: `ORDER:${order.id}:ING:${req.ingredientId}`,  // ⭐
        actorId,
      });
      costTotal += cost;
    }

    // 4) Cập nhật đơn
    const updated = await tx.order.update({
      where: { id: orderId },
      data: {
        status: 'CONFIRMED',
        confirmedAt: new Date(),
        stockDeducted: true,
        costTotal,
      },
    });

    return updated;
  }, {
    isolationLevel: 'Serializable',   // ⭐ chống hai đơn cùng lấy lô cuối cùng
    timeout: 15_000,
  });
}
```

**Sau khi transaction thành công (ngoài transaction):**
```typescript
await recomputeAvailability(affectedIngredientIds);   // cập nhật Product.isAvailable
```

---

### 5.4 FEFO — First Expired, First Out ⭐

**Nguyên tắc:** luôn tiêu thụ lô có hạn dùng **gần nhất** trước. Lô không có hạn dùng
(`expiryDate = null`) xếp cuối cùng.

```typescript
// src/server/services/inventory.service.ts

export async function consumeFefo(
  tx: Prisma.TransactionClient,
  input: {
    storeId: string;
    ingredientId: string;
    quantity: number;          // > 0, theo baseUnit
    referenceType: string;
    referenceId: string;
    idempotencyKey: string;
    actorId?: string;
  },
): Promise<{ cost: number; consumedLots: Array<{ lotId: string; qty: number }> }> {

  // ⭐ Chống trừ hai lần: nếu đã có movement với key này thì thoát ngay
  const existing = await tx.stockMovement.findUnique({
    where: { idempotencyKey: input.idempotencyKey },
  });
  if (existing) {
    return { cost: existing.totalCost, consumedLots: [] };
  }

  // Lấy các lô còn hàng, sắp theo FEFO. NULLS LAST vì lô không hạn dùng để cuối.
  // ⭐ Bắt buộc FOR UPDATE để khóa hàng, chống race condition.
  const lots = await tx.$queryRaw<Array<{
    id: string; remainingQty: number; unitCost: number; expiryDate: Date | null;
  }>>`
    SELECT id, "remainingQty", "unitCost", "expiryDate"
    FROM "InventoryLot"
    WHERE "storeId" = ${input.storeId}
      AND "ingredientId" = ${input.ingredientId}
      AND status = 'ACTIVE'
      AND "remainingQty" > 0
    ORDER BY "expiryDate" ASC NULLS LAST, "receivedAt" ASC
    FOR UPDATE
  `;

  const available = lots.reduce((s, l) => s + l.remainingQty, 0);
  if (available < input.quantity) {
    throw new InsufficientStockError({
      ingredientId: input.ingredientId,
      required: input.quantity,
      available,
    });
  }

  let remaining = input.quantity;
  let totalCost = 0;
  const consumedLots: Array<{ lotId: string; qty: number }> = [];

  for (const lot of lots) {
    if (remaining <= 0) break;

    const take = Math.min(lot.remainingQty, remaining);
    const newRemaining = roundQty(lot.remainingQty - take);

    await tx.inventoryLot.update({
      where: { id: lot.id },
      data: {
        remainingQty: newRemaining,
        status: newRemaining <= 0 ? 'DEPLETED' : 'ACTIVE',
      },
    });

    const lineCost = Math.round(take * lot.unitCost);

    await tx.stockMovement.create({
      data: {
        storeId: input.storeId,
        ingredientId: input.ingredientId,
        lotId: lot.id,
        type: 'SALE_OUT',
        quantityDelta: -take,                    // ⭐ ÂM vì là xuất kho
        unitCost: lot.unitCost,
        totalCost: lineCost,
        referenceType: input.referenceType,
        referenceId: input.referenceId,
        // Key phải duy nhất cho từng lô để tránh đụng unique constraint
        idempotencyKey: `${input.idempotencyKey}:LOT:${lot.id}`,
        actorId: input.actorId,
      },
    });

    totalCost += lineCost;
    remaining = roundQty(remaining - take);
    consumedLots.push({ lotId: lot.id, qty: take });
  }

  return { cost: totalCost, consumedLots };
}
```

**Hoàn kho khi hủy đơn** — đảo ngược đúng các lô đã trừ:

```typescript
export async function returnStockForOrder(tx, orderId: string, actorId: string) {
  const movements = await tx.stockMovement.findMany({
    where: { referenceType: 'ORDER', referenceId: orderId, type: 'SALE_OUT' },
  });

  for (const m of movements) {
    const qty = Math.abs(m.quantityDelta);
    await tx.inventoryLot.update({
      where: { id: m.lotId! },
      data: {
        remainingQty: { increment: qty },
        status: 'ACTIVE',                       // đánh thức lô đã DEPLETED
      },
    });
    await tx.stockMovement.create({
      data: {
        ...pickCommonFields(m),
        type: 'RETURN_IN',
        quantityDelta: qty,                     // DƯƠNG
        idempotencyKey: `RETURN:${m.id}`,
        actorId,
      },
    });
  }
}
```

---

### 5.5 Kiểm tra khả dụng & chặn bán hết hàng

**Hai tầng kiểm tra, khác nhau về mục đích:**

| Tầng | Khi nào | Mục đích | Chi phí |
|---|---|---|---|
| **Cached** | Hiển thị menu | Nhanh, hiển thị nhãn "Tạm hết" | Đọc `Product.isAvailable` |
| **Live** | Trước khi CONFIRM | Chính xác tuyệt đối | Query tồn kho thật, trong transaction |

```typescript
// src/server/services/availability.service.ts

/**
 * Tính số ly tối đa còn làm được cho một món.
 * maxServings = min( tồnKho(nguyênLiệu) / lượngCần(nguyênLiệu) ) trên mọi nguyên liệu bắt buộc.
 */
export async function computeMaxServings(productId: string): Promise<{
  maxServings: number;
  blockingIngredient: string | null;
}> {
  const recipe = await db.recipeItem.findMany({
    where: { productId, isOptional: false },
    include: { ingredient: true },
  });

  let maxServings = Infinity;
  let blocking: string | null = null;

  for (const item of recipe) {
    const stock = await getAvailableStock(item.ingredientId);   // SUM lot ACTIVE
    const needPerServing = item.quantity * (1 + item.ingredient.wastageRate);
    const possible = Math.floor(stock / needPerServing);
    if (possible < maxServings) {
      maxServings = possible;
      blocking = item.ingredient.name;
    }
  }

  return {
    maxServings: maxServings === Infinity ? 9999 : maxServings,
    blockingIngredient: maxServings === 0 ? blocking : null,
  };
}

/** Cập nhật Product.isAvailable + maxServings. Gọi sau mỗi lần kho thay đổi. */
export async function recomputeAvailability(ingredientIds: string[]) {
  const productIds = await db.recipeItem
    .findMany({ where: { ingredientId: { in: ingredientIds } }, select: { productId: true } })
    .then((rows) => [...new Set(rows.map((r) => r.productId))]);

  for (const pid of productIds) {
    const { maxServings, blockingIngredient } = await computeMaxServings(pid);
    await db.product.update({
      where: { id: pid },
      data: {
        maxServings,
        isAvailable: maxServings > 0,
        unavailableReason: blockingIngredient ? `Hết ${blockingIngredient}` : null,
      },
    });
  }
}
```

**Quy tắc UI:**
- `maxServings === 0` → nút "Thêm vào giỏ" bị vô hiệu, hiện nhãn "Tạm hết"
- `0 < maxServings <= 5` → hiện "Chỉ còn {n} ly"
- Giỏ hàng không cho tăng số lượng vượt `maxServings`

---

### 5.6 Nhập kho

```typescript
export async function receiveStock(input: {
  storeId: string;
  ingredientId: string;
  quantity: number;          // theo đơn vị NHẬP (vd: 12 thùng)
  purchaseUnitId?: string;   // để quy đổi
  unitCost: number;          // giá 1 baseUnit
  expiryDate?: Date;
  lotCode?: string;
  supplierId?: string;
  actorId: string;
}) {
  return db.$transaction(async (tx) => {
    const ingredient = await tx.ingredient.findUniqueOrThrow({
      where: { id: input.ingredientId },
    });

    // 1) Quy đổi về baseUnit
    const baseQty = input.purchaseUnitId
      ? input.quantity * (await getConversionQty(tx, input.purchaseUnitId))
      : input.quantity;

    // 2) Hạn dùng: ưu tiên nhập tay, không có thì tính từ defaultShelfLifeDays
    const expiry = input.expiryDate
      ?? (ingredient.defaultShelfLifeDays
          ? addDays(new Date(), ingredient.defaultShelfLifeDays)
          : null);

    // 3) Tạo lô
    const lot = await tx.inventoryLot.create({
      data: {
        storeId: input.storeId,
        ingredientId: input.ingredientId,
        lotCode: input.lotCode ?? generateLotCode(ingredient.sku),
        receivedQty: baseQty,
        remainingQty: baseQty,
        unitCost: input.unitCost,
        expiryDate: expiry,
        supplierId: input.supplierId,
        status: 'ACTIVE',
      },
    });

    // 4) Ghi sổ cái
    await tx.stockMovement.create({
      data: {
        storeId: input.storeId,
        ingredientId: input.ingredientId,
        lotId: lot.id,
        type: 'PURCHASE_IN',
        quantityDelta: baseQty,                     // DƯƠNG
        unitCost: input.unitCost,
        totalCost: Math.round(baseQty * input.unitCost),
        referenceType: 'PURCHASE',
        referenceId: lot.id,
        idempotencyKey: `RECEIVE:${lot.id}`,
        actorId: input.actorId,
      },
    });

    // 5) ⭐ Cập nhật giá vốn bình quân gia quyền
    await updateWeightedAverageCost(tx, input.ingredientId);

    return lot;
  });
}

/**
 * Giá vốn bình quân gia quyền trên các lô còn hàng.
 * avgUnitCost = Σ(remainingQty × unitCost) / Σ(remainingQty)
 */
async function updateWeightedAverageCost(tx, ingredientId: string) {
  const lots = await tx.inventoryLot.findMany({
    where: { ingredientId, status: 'ACTIVE', remainingQty: { gt: 0 } },
    select: { remainingQty: true, unitCost: true },
  });
  const totalQty = lots.reduce((s, l) => s + l.remainingQty, 0);
  if (totalQty === 0) return;
  const totalValue = lots.reduce((s, l) => s + l.remainingQty * l.unitCost, 0);
  await tx.ingredient.update({
    where: { id: ingredientId },
    data: { avgUnitCost: Math.round(totalValue / totalQty) },
  });
}
```

---

## 06. AI Engine — Trợ lý tồn kho & khuyến mãi

### 6.1 Kiến trúc ba lớp — ranh giới trách nhiệm

```
┌──────────────────────────────────────────────────────────────────┐
│  LỚP 1 — DETERMINISTIC (TypeScript thuần)                        │
│  Trừ kho FEFO · Tính tồn · Tính giá vốn · Kiểm tra khả dụng      │
│  ⚠️ TUYỆT ĐỐI KHÔNG cho LLM đụng vào. Đây là số tiền thật.       │
├──────────────────────────────────────────────────────────────────┤
│  LỚP 2 — ANALYTICAL (TypeScript + thống kê)                      │
│  Dự báo tiêu thụ (EWMA) · Tính rủi ro hết hạn · Sinh phương án   │
│  khuyến mãi & tính lãi lỗ từng phương án                         │
│  ⚠️ Vẫn là code. LLM KHÔNG được tính toán ở đây.                 │
├──────────────────────────────────────────────────────────────────┤
│  LỚP 3 — GENERATIVE (Claude API)                                 │
│  Nhận SỐ LIỆU ĐÃ TÍNH XONG → viết diễn giải, xếp ưu tiên theo    │
│  ngữ cảnh, viết nội dung banner khuyến mãi                       │
│  ✅ LLM chỉ làm việc nó giỏi: ngôn ngữ và phán đoán định tính    │
└──────────────────────────────────────────────────────────────────┘
```

> **Quy tắc vàng:** LLM không bao giờ được làm phép tính liên quan đến tiền, số lượng
> hay tồn kho. Nó nhận số đã tính sẵn và diễn giải. Nếu để LLM cộng trừ, bạn sẽ có
> báo cáo tài chính sai mà không biết.

---

### 6.2 Thuật toán tính rủi ro hết hạn (Lớp 2)

```typescript
// src/server/services/waste-risk.service.ts

export interface LotRisk {
  lotId: string;
  lotCode: string;
  ingredientId: string;
  ingredientName: string;
  baseUnit: BaseUnit;

  remainingQty: number;
  unitCost: number;
  expiryDate: Date;
  daysUntilExpiry: number;

  // Dự báo
  avgDailyUsage: number;         // EWMA 28 ngày
  projectedUsage: number;        // avgDailyUsage × daysUntilExpiry
  quantityAtRisk: number;        // max(0, remainingQty - projectedUsage)
  valueAtRisk: number;           // quantityAtRisk × unitCost, VND

  severity: 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';
  // Các món dùng nguyên liệu này, sắp theo lượng tiêu thụ/ly giảm dần
  carriers: CarrierProduct[];
}

export interface CarrierProduct {
  productId: string;
  productName: string;
  qtyPerServing: number;         // lượng nguyên liệu dùng cho 1 ly
  price: number;
  cost: number;
  marginPercent: number;
  avgDailyUnits: number;         // số ly bán trung bình/ngày
}

/** BƯỚC 1 — Dự báo tiêu thụ hằng ngày bằng EWMA có trọng số ngày trong tuần. */
export async function forecastDailyUsage(
  ingredientId: string,
  lookbackDays = 28,
): Promise<number> {
  const rows = await db.dailyConsumption.findMany({
    where: {
      ingredientId,
      businessDate: { gte: formatBusinessDate(subDays(new Date(), lookbackDays)) },
    },
    orderBy: { businessDate: 'asc' },
  });

  if (rows.length === 0) return 0;

  // EWMA với alpha = 0.25 — dữ liệu gần đây có trọng số cao hơn
  const ALPHA = 0.25;
  let ewma = rows[0].quantityUsed;
  for (let i = 1; i < rows.length; i++) {
    ewma = ALPHA * rows[i].quantityUsed + (1 - ALPHA) * ewma;
  }
  return roundQty(ewma);
}

/** BƯỚC 2 — Quét toàn bộ lô, tính rủi ro. */
export async function analyzeWasteRisk(storeId: string): Promise<LotRisk[]> {
  const HORIZON_DAYS = 14;   // chỉ quan tâm lô hết hạn trong 14 ngày tới

  const lots = await db.inventoryLot.findMany({
    where: {
      storeId,
      status: 'ACTIVE',
      remainingQty: { gt: 0 },
      expiryDate: { not: null, lte: addDays(new Date(), HORIZON_DAYS) },
    },
    include: { ingredient: true },
    orderBy: { expiryDate: 'asc' },
  });

  const risks: LotRisk[] = [];

  for (const lot of lots) {
    const daysLeft = differenceInCalendarDays(lot.expiryDate!, startOfToday());
    const avgDaily = await forecastDailyUsage(lot.ingredientId);

    // Dự kiến dùng hết bao nhiêu trước khi hết hạn
    const projected = roundQty(avgDaily * Math.max(daysLeft, 0));
    const atRisk = Math.max(0, roundQty(lot.remainingQty - projected));
    const valueAtRisk = Math.round(atRisk * lot.unitCost);

    if (atRisk <= 0) continue;   // sẽ dùng hết kịp, không cần lo

    risks.push({
      lotId: lot.id,
      lotCode: lot.lotCode,
      ingredientId: lot.ingredientId,
      ingredientName: lot.ingredient.name,
      baseUnit: lot.ingredient.baseUnit,
      remainingQty: lot.remainingQty,
      unitCost: lot.unitCost,
      expiryDate: lot.expiryDate!,
      daysUntilExpiry: daysLeft,
      avgDailyUsage: avgDaily,
      projectedUsage: projected,
      quantityAtRisk: atRisk,
      valueAtRisk,
      severity: classifySeverity(daysLeft, valueAtRisk),
      carriers: await findCarrierProducts(lot.ingredientId),
    });
  }

  return risks.sort((a, b) => b.valueAtRisk - a.valueAtRisk);
}

/** Ma trận phân loại mức độ: kết hợp thời gian còn lại và giá trị. */
function classifySeverity(daysLeft: number, valueAtRisk: number): LotRisk['severity'] {
  if (daysLeft <= 1) return 'CRITICAL';
  if (daysLeft <= 3) return valueAtRisk >= 200_000 ? 'CRITICAL' : 'HIGH';
  if (daysLeft <= 7) return valueAtRisk >= 500_000 ? 'HIGH' : 'MEDIUM';
  return 'LOW';
}

/** BƯỚC 3 — Tìm "món chở hàng": món nào tiêu thụ nguyên liệu này nhiều nhất. */
async function findCarrierProducts(ingredientId: string): Promise<CarrierProduct[]> {
  const recipeItems = await db.recipeItem.findMany({
    where: { ingredientId, product: { isActive: true, deletedAt: null } },
    include: { product: true },
  });

  const carriers = await Promise.all(
    recipeItems.map(async (ri) => {
      const avgUnits = await getAvgDailyUnitsSold(ri.productId, 28);
      const margin = ri.product.basePrice - ri.product.computedCost;
      return {
        productId: ri.productId,
        productName: ri.product.name,
        qtyPerServing: ri.quantity,
        price: ri.product.basePrice,
        cost: ri.product.computedCost,
        marginPercent: Math.round((margin / ri.product.basePrice) * 100),
        avgDailyUnits: avgUnits,
      };
    }),
  );

  // Ưu tiên món tiêu thụ nhiều nguyên liệu VÀ đang bán được
  return carriers.sort(
    (a, b) =>
      b.qtyPerServing * Math.max(b.avgDailyUnits, 0.5) -
      a.qtyPerServing * Math.max(a.avgDailyUnits, 0.5),
  );
}
```

---

### 6.3 Sinh phương án khuyến mãi (Lớp 2)

```typescript
// src/server/services/promotion.service.ts

export interface PromotionCandidate {
  productId: string;
  productName: string;
  ingredientId: string;
  ingredientName: string;

  discountPercent: number;
  targetUnits: number;              // cần bán thêm bao nhiêu ly
  baselineUnits: number;            // số ly vốn sẽ bán được nếu không giảm giá
  expectedExtraUnits: number;       // số ly tăng thêm nhờ giảm giá

  wasteAvoided: number;             // VND cứu được
  marginGivenUp: number;            // VND lãi mất trên phần vốn đã bán được
  extraMargin: number;              // VND lãi thêm từ ly bán thêm
  netBenefit: number;               // ⭐ chỉ số xếp hạng

  feasible: boolean;                // có bán kịp trước hạn không
  daysAvailable: number;
}

// Độ co giãn giá mặc định cho đồ uống. Âm = giảm giá thì bán nhiều hơn.
// Khi có ≥ 90 ngày dữ liệu khuyến mãi thì ước lượng lại từ thực tế.
const DEFAULT_PRICE_ELASTICITY = -1.6;
const DISCOUNT_LEVELS = [10, 15, 20, 25, 30] as const;
const MIN_MARGIN_PERCENT = 15;      // không bao giờ giảm xuống dưới mức lãi này

export function generatePromotionCandidates(risk: LotRisk): PromotionCandidate[] {
  const candidates: PromotionCandidate[] = [];
  const days = Math.max(risk.daysUntilExpiry, 1);

  // Chỉ xét 3 món chở hàng tốt nhất — nhiều hơn sẽ rối cho khách
  for (const carrier of risk.carriers.slice(0, 3)) {
    // Cần bán thêm bao nhiêu ly để tiêu hết phần nguyên liệu có nguy cơ
    const targetUnits = Math.ceil(risk.quantityAtRisk / carrier.qtyPerServing);
    const baselineUnits = Math.round(carrier.avgDailyUnits * days);

    for (const discount of DISCOUNT_LEVELS) {
      const newPrice = Math.round(carrier.price * (1 - discount / 100));
      const newMargin = newPrice - carrier.cost;
      const newMarginPercent = (newMargin / newPrice) * 100;

      // Chặn cứng: không bán dưới ngưỡng lãi tối thiểu
      if (newMarginPercent < MIN_MARGIN_PERCENT) continue;

      // Mô hình co giãn: Q2/Q1 = (P2/P1)^elasticity
      const priceRatio = newPrice / carrier.price;
      const demandMultiplier = Math.pow(priceRatio, DEFAULT_PRICE_ELASTICITY);
      const expectedTotalUnits = Math.round(baselineUnits * demandMultiplier);
      const extraUnits = Math.max(0, expectedTotalUnits - baselineUnits);

      // Lượng nguyên liệu thực sự cứu được (không vượt quá lượng có nguy cơ)
      const qtySaved = Math.min(risk.quantityAtRisk, extraUnits * carrier.qtyPerServing);
      const wasteAvoided = Math.round(qtySaved * risk.unitCost);

      // Lãi bị mất trên số ly vốn dĩ đã bán được với giá gốc
      const marginGivenUp = baselineUnits * (carrier.price - newPrice);
      // Lãi thu thêm từ số ly bán thêm
      const extraMargin = extraUnits * newMargin;

      const netBenefit = wasteAvoided + extraMargin - marginGivenUp;

      candidates.push({
        productId: carrier.productId,
        productName: carrier.productName,
        ingredientId: risk.ingredientId,
        ingredientName: risk.ingredientName,
        discountPercent: discount,
        targetUnits,
        baselineUnits,
        expectedExtraUnits: extraUnits,
        wasteAvoided,
        marginGivenUp,
        extraMargin,
        netBenefit,
        feasible: extraUnits >= targetUnits,
        daysAvailable: days,
      });
    }
  }

  // Chỉ giữ phương án có lợi ròng dương, lấy phương án tốt nhất cho mỗi món
  const best = new Map<string, PromotionCandidate>();
  for (const c of candidates) {
    if (c.netBenefit <= 0) continue;
    const cur = best.get(c.productId);
    if (!cur || c.netBenefit > cur.netBenefit) best.set(c.productId, c);
  }

  return [...best.values()].sort((a, b) => b.netBenefit - a.netBenefit);
}
```

**Khi nào KHÔNG đề xuất giảm giá — sinh loại đề xuất khác:**

| Điều kiện | Loại đề xuất |
|---|---|
| Không có phương án nào `netBenefit > 0` và `daysLeft <= 1` | `DISPOSE` — chuẩn bị hủy, ghi nhận hao hụt |
| `daysLeft <= 2` và không có món chở hàng nào bán chạy | `STAFF_PUSH` — nhân viên chủ động mời khách |
| Nguyên liệu liên tục dư 3 kỳ nhập liên tiếp | `REDUCE_PURCHASE` — giảm lượng nhập lần sau |
| Nguyên liệu dưới `reorderPoint` | `RESTOCK` — cần nhập thêm |
| `netBenefit > 0` nhưng cần giảm > 30% | `BUNDLE` — làm combo thay vì giảm sâu |

---

### 6.4 Prompt Claude & schema đầu ra (Lớp 3)

```typescript
// src/server/ai/client.ts
import Anthropic from '@anthropic-ai/sdk';

export const anthropic = new Anthropic();   // đọc ANTHROPIC_API_KEY từ env

export const AI_MODEL = 'claude-opus-5';
```

```typescript
// src/server/ai/schemas.ts
import { z } from 'zod';

export const AiPlanOutputSchema = z.object({
  headline: z.string().max(120),
  summary: z.string().max(1200),
  suggestions: z.array(
    z.object({
      // Phải khớp với candidateId mà code truyền vào — AI không được bịa
      candidateId: z.string(),
      priority: z.enum(['CRITICAL', 'HIGH', 'MEDIUM', 'LOW']),
      title: z.string().max(120),
      reasoning: z.string().max(600),
      bannerCopy: z.string().max(80).nullable(),
    }),
  ).max(6),
});

export type AiPlanOutput = z.infer<typeof AiPlanOutputSchema>;
```

```typescript
// src/server/ai/prompts/daily-plan.ts

export const DAILY_PLAN_SYSTEM_PROMPT = `
Bạn là trợ lý vận hành của một quán cà phê tại Việt Nam. Cuối mỗi ngày bạn nhận
số liệu phân tích tồn kho đã được hệ thống tính sẵn, và viết một bản kế hoạch
ngắn gọn cho chủ quán đọc.

## Nhiệm vụ của bạn
1. Viết một câu headline nêu điều quan trọng nhất cần xử lý.
2. Viết đoạn tóm tắt tình hình (3–5 câu) bằng tiếng Việt tự nhiên, giọng như một
   quản lý ca báo cáo — trực tiếp, không màu mè.
3. Với mỗi phương án được cung cấp, viết tiêu đề và lý do ngắn gọn giải thích
   VÌ SAO nên làm, dựa trên chính các con số được đưa.
4. Với phương án giảm giá, viết thêm một dòng nội dung banner ngắn (dưới 80 ký tự)
   để hiển thị trên web bán hàng — viết cho khách trẻ, tự nhiên, không sáo rỗng.

## Ràng buộc tuyệt đối
- KHÔNG tự tính toán lại bất kỳ con số nào. Mọi số liệu đã được tính sẵn và đúng.
  Bạn chỉ trích dẫn chúng.
- KHÔNG đề xuất phương án không có trong danh sách được cung cấp.
- KHÔNG bịa candidateId. Chỉ dùng đúng id được đưa.
- KHÔNG hứa hẹn với khách những điều quán không kiểm soát được.
- Nếu số liệu cho thấy không có việc gì gấp, hãy nói thẳng là hôm nay ổn.
  Không cần bịa ra việc để đề xuất.

## Giọng văn
Ngắn gọn, cụ thể, không dùng từ hoa mỹ. Nêu con số khi nó giúp người đọc quyết định.
Ưu tiên trình bày điều quan trọng nhất trước.
`.trim();

export function buildDailyPlanUserPrompt(input: {
  businessDate: string;
  risks: LotRisk[];
  candidates: Array<PromotionCandidate & { candidateId: string }>;
  otherSuggestions: Array<{ candidateId: string; type: string; payload: unknown }>;
  yesterdaySummary: { revenue: number; orders: number; wasteValue: number };
}): string {
  return `
# Ngày kinh doanh: ${input.businessDate}

## Kết quả hôm nay
- Doanh thu: ${formatVnd(input.yesterdaySummary.revenue)}
- Số đơn: ${input.yesterdaySummary.orders}
- Giá trị hao hụt ghi nhận: ${formatVnd(input.yesterdaySummary.wasteValue)}

## Nguyên liệu có nguy cơ bỏ đi
${input.risks.map((r) => `
- **${r.ingredientName}** (lô ${r.lotCode})
  - Còn lại: ${r.remainingQty} ${r.baseUnit}
  - Hạn dùng: ${formatDate(r.expiryDate)} — còn ${r.daysUntilExpiry} ngày
  - Tiêu thụ trung bình: ${r.avgDailyUsage} ${r.baseUnit}/ngày
  - Dự kiến dùng được: ${r.projectedUsage} ${r.baseUnit}
  - **Có nguy cơ bỏ: ${r.quantityAtRisk} ${r.baseUnit} ≈ ${formatVnd(r.valueAtRisk)}**
  - Mức độ: ${r.severity}
`).join('')}

## Phương án khuyến mãi hệ thống đã tính
${input.candidates.map((c) => `
### candidateId: ${c.candidateId}
- Món: ${c.productName}
- Giảm giá đề xuất: ${c.discountPercent}%
- Cần bán thêm: ${c.targetUnits} ly trong ${c.daysAvailable} ngày
- Dự kiến bán thêm được: ${c.expectedExtraUnits} ly
- Khả thi: ${c.feasible ? 'CÓ' : 'KHÔNG — không kịp bán hết'}
- Cứu được nguyên liệu: ${formatVnd(c.wasteAvoided)}
- Lãi mất trên phần vốn bán được: ${formatVnd(c.marginGivenUp)}
- Lãi thêm từ ly bán thêm: ${formatVnd(c.extraMargin)}
- **Lợi ích ròng: ${formatVnd(c.netBenefit)}**
`).join('')}

## Đề xuất khác
${JSON.stringify(input.otherSuggestions, null, 2)}

---
Hãy trả về JSON đúng schema đã quy định.
`.trim();
}
```

**Gọi API với structured output:**

```typescript
// src/server/services/plan.service.ts

export async function generateAiNarrative(payload: DailyPlanPayload): Promise<AiPlanOutput> {
  const response = await anthropic.messages.create({
    model: AI_MODEL,
    max_tokens: 8000,
    thinking: { type: 'adaptive' },
    output_config: {
      effort: 'medium',
      format: {
        type: 'json_schema',
        schema: zodToJsonSchema(AiPlanOutputSchema),
      },
    },
    system: DAILY_PLAN_SYSTEM_PROMPT,
    messages: [{ role: 'user', content: buildDailyPlanUserPrompt(payload) }],
  });

  const text = response.content.find((b) => b.type === 'text')?.text ?? '';
  const parsed = AiPlanOutputSchema.parse(JSON.parse(text));

  // ⭐ Chốt chặn: loại bỏ mọi suggestion có candidateId không tồn tại
  const validIds = new Set(payload.candidates.map((c) => c.candidateId));
  parsed.suggestions = parsed.suggestions.filter((s) => validIds.has(s.candidateId));

  return parsed;
}
```

**Xử lý khi AI lỗi — bắt buộc phải có:**

```typescript
try {
  narrative = await generateAiNarrative(payload);
} catch (err) {
  // Kế hoạch VẪN được tạo, chỉ thiếu phần diễn giải.
  // Số liệu và đề xuất là do code tính nên luôn có.
  narrative = buildFallbackNarrative(payload);   // template tiếng Việt cố định
  aiError = err instanceof Error ? err.message : String(err);
}
```

> **Nguyên tắc:** Lỗi AI không bao giờ được làm mất bản kế hoạch. Số liệu là phần
> quan trọng; diễn giải là phần tô điểm.

**Chi phí ước tính:** ~4.000 token input + ~1.500 token output mỗi ngày.
Với `claude-opus-5` ($5/1M input, $25/1M output) ≈ **1.400đ/ngày ≈ 42.000đ/tháng**.

---

### 6.5 Điều phối job cuối ngày

```typescript
// src/app/api/cron/daily-plan/route.ts

export const maxDuration = 300;   // Vercel: cho phép chạy 5 phút

export async function POST(req: Request) {
  // 1) Bảo vệ endpoint
  const secret = req.headers.get('x-cron-secret');
  if (secret !== process.env.CRON_SECRET) {
    return Response.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const storeId = process.env.DEFAULT_STORE_ID!;
  const businessDate = getBusinessDate(new Date());   // theo giờ VN

  // 2) Idempotent: nếu đã có kế hoạch cho ngày này thì bỏ qua
  const existing = await db.dailyPlan.findUnique({
    where: { storeId_businessDate: { storeId, businessDate } },
  });
  if (existing) {
    return Response.json({ skipped: true, planId: existing.id });
  }

  const plan = await runDailyPlanJob(storeId, businessDate);
  return Response.json({ ok: true, planId: plan.id });
}
```

```typescript
// src/server/services/plan.service.ts

export async function runDailyPlanJob(storeId: string, businessDate: string) {
  // BƯỚC 1 — Tổng hợp tiêu thụ trong ngày vào DailyConsumption & DailySales
  await aggregateDailyConsumption(storeId, businessDate);
  await aggregateDailySales(storeId, businessDate);

  // BƯỚC 2 — Đánh dấu lô đã quá hạn, ghi hao hụt tự động
  await expireOverdueLots(storeId);

  // BƯỚC 3 — Phân tích rủi ro (Lớp 2)
  const risks = await analyzeWasteRisk(storeId);

  // BƯỚC 4 — Sinh phương án khuyến mãi (Lớp 2)
  const candidates = risks
    .flatMap((r) => generatePromotionCandidates(r))
    .slice(0, 6)                              // tối đa 6 để không rối
    .map((c, i) => ({ ...c, candidateId: `cand-${i + 1}` }));

  // BƯỚC 5 — Các đề xuất khác (restock, dispose, reduce purchase)
  const others = await generateOtherSuggestions(storeId, risks);

  // BƯỚC 6 — Gọi Claude viết diễn giải (Lớp 3)
  const yesterdaySummary = await getDailySummary(storeId, businessDate);
  let narrative: AiPlanOutput;
  let aiError: string | null = null;
  let tokensUsed = 0;
  try {
    const result = await generateAiNarrative({
      businessDate, risks, candidates, otherSuggestions: others, yesterdaySummary,
    });
    narrative = result;
  } catch (e) {
    narrative = buildFallbackNarrative({ risks, candidates, others });
    aiError = String(e);
  }

  // BƯỚC 7 — Lưu kế hoạch
  const plan = await db.dailyPlan.create({
    data: {
      storeId,
      businessDate,
      totalValueAtRisk: risks.reduce((s, r) => s + r.valueAtRisk, 0),
      criticalCount: risks.filter((r) => r.severity === 'CRITICAL').length,
      warningCount: risks.filter((r) => r.severity === 'HIGH' || r.severity === 'MEDIUM').length,
      lowStockCount: await countLowStock(storeId),
      riskAnalysis: risks as unknown as Prisma.JsonObject,
      headline: narrative.headline,
      summary: narrative.summary,
      aiModel: AI_MODEL,
      aiTokensUsed: tokensUsed,
      aiError,
      suggestions: {
        create: narrative.suggestions.map((s, idx) => {
          const cand = candidates.find((c) => c.candidateId === s.candidateId);
          return {
            type: cand ? 'DISCOUNT' : 'STAFF_PUSH',
            priority: s.priority,
            ingredientId: cand?.ingredientId,
            productId: cand?.productId,
            title: s.title,
            reasoning: s.reasoning,
            bannerCopy: s.bannerCopy,
            // ⭐ Số liệu lấy từ CODE, không lấy từ AI
            discountPercent: cand?.discountPercent,
            targetUnits: cand?.targetUnits,
            expectedWasteAvoided: cand?.wasteAvoided,
            expectedMarginImpact: cand?.netBenefit,
            daysUntilExpiry: cand ? cand.daysAvailable : null,
            quantityAtRisk: risks.find((r) => r.ingredientId === cand?.ingredientId)?.quantityAtRisk,
            sortOrder: idx,
          };
        }),
      },
    },
    include: { suggestions: true },
  });

  // BƯỚC 8 — Gửi thông báo
  await sendPlanNotification(plan);

  return plan;
}
```

**Duyệt đề xuất → tạo khuyến mãi thật:**

```typescript
export async function approveSuggestion(input: {
  suggestionId: string;
  actorId: string;
  overrides?: { discountPercent?: number; startsAt?: Date; endsAt?: Date };
}) {
  return db.$transaction(async (tx) => {
    const s = await tx.planSuggestion.findUniqueOrThrow({
      where: { id: input.suggestionId },
      include: { plan: true },
    });

    const promo = await tx.promotion.create({
      data: {
        storeId: s.plan.storeId,
        name: s.title,
        description: s.reasoning,
        type: 'PERCENT_OFF',
        value: input.overrides?.discountPercent ?? s.discountPercent ?? 10,
        status: 'ACTIVE',
        startsAt: input.overrides?.startsAt ?? new Date(),
        endsAt: input.overrides?.endsAt ?? addDays(new Date(), s.daysUntilExpiry ?? 2),
        productId: s.productId,
        bannerText: s.bannerCopy,
        sourcePlanId: s.dailyPlanId,
        sourceSuggestionId: s.id,
      },
    });

    await tx.planDecision.create({
      data: {
        dailyPlanId: s.dailyPlanId,
        suggestionId: s.id,
        action: input.overrides ? 'MODIFIED' : 'APPROVED',
        actorId: input.actorId,
        modifiedPayload: input.overrides ?? undefined,
        createdPromotionId: promo.id,
      },
    });

    return promo;
  });
}
```

---

## 07. API Contract

> Mọi endpoint trả về `{ data }` khi thành công, `{ error: { code, message, details? } }` khi lỗi.
> Validate input bằng Zod ở đầu mỗi handler.

### 7.1 Công khai (không cần đăng nhập)

| Method | Path | Mô tả |
|---|---|---|
| `GET` | `/api/menu` | Danh sách danh mục + món, kèm `isAvailable`, `maxServings`, khuyến mãi đang chạy |
| `GET` | `/api/menu/[slug]` | Chi tiết món + biến thể + nhóm topping |
| `POST` | `/api/menu/availability` | Kiểm tra khả dụng theo thời gian thực cho giỏ hàng |
| `POST` | `/api/orders` | Tạo đơn (trạng thái `PENDING`) |
| `GET` | `/api/orders/[code]` | Tra cứu đơn theo mã |

```typescript
// POST /api/orders — request
{
  customerName: string;
  customerPhone: string;               // regex VN: /^(0|\+84)[0-9]{9}$/
  orderType: 'DINE_IN' | 'TAKEAWAY';
  paymentMethod: 'CASH' | 'BANK_TRANSFER';
  note?: string;
  items: Array<{
    productId: string;
    variantId?: string;
    modifierIds: string[];
    quantity: number;                  // 1..20
    note?: string;
  }>;
}

// response 201
{ data: { orderId: string; code: string; grandTotal: number; status: 'PENDING' } }

// response 409 — không đủ nguyên liệu
{
  error: {
    code: 'INSUFFICIENT_STOCK',
    message: 'Một số món đã hết nguyên liệu',
    details: [{ productId, productName, maxServings: 0, reason: 'Hết sữa tươi' }]
  }
}
```

### 7.2 Nội bộ (yêu cầu `STAFF` trở lên)

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| `GET` | `/api/admin/orders` | STAFF | Danh sách đơn, lọc theo trạng thái/ngày |
| `POST` | `/api/orders/[id]/status` | STAFF | Đổi trạng thái — **trừ kho khi sang CONFIRMED** |
| `POST` | `/api/inventory/receive` | STAFF | Nhập kho, tạo lô mới |
| `POST` | `/api/inventory/waste` | STAFF | Ghi nhận hao hụt / đổ bỏ |
| `POST` | `/api/inventory/count` | MANAGER | Kiểm kê, tạo phiếu điều chỉnh |
| `GET` | `/api/inventory/lots` | STAFF | Danh sách lô, lọc theo hạn dùng |
| `GET` | `/api/inventory/alerts` | STAFF | Cảnh báo: cận hạn, dưới ngưỡng |
| `POST` | `/api/products` | MANAGER | Tạo món + công thức |
| `PUT` | `/api/products/[id]/recipe` | MANAGER | Cập nhật công thức định lượng |
| `GET` | `/api/plans` | MANAGER | Danh sách kế hoạch theo ngày |
| `GET` | `/api/plans/[date]` | MANAGER | Chi tiết kế hoạch + đề xuất |
| `POST` | `/api/plans/[date]/decide` | MANAGER | Duyệt / từ chối / sửa đề xuất |
| `GET` | `/api/reports/*` | MANAGER | Doanh thu, hao hụt, giá vốn |

### 7.3 Cron (bảo vệ bằng `x-cron-secret`)

| Method | Path | Lịch | Mô tả |
|---|---|---|---|
| `POST` | `/api/cron/daily-plan` | `0 23 * * *` (VN) | ⭐ Sinh kế hoạch cuối ngày |
| `POST` | `/api/cron/expiry-check` | `0 8 * * *` | Đánh dấu lô hết hạn, cảnh báo sáng |
| `POST` | `/api/cron/refresh-availability` | `*/15 * * * *` | Đồng bộ lại `isAvailable` |

---

## 08. Giao diện — sitemap & màn hình

### 8.1 Khối A — Cửa hàng

| Trang | Nội dung chính |
|---|---|
| `/` | Hero, **banner khuyến mãi AI đề xuất**, món nổi bật, danh mục |
| `/menu` | Lọc theo danh mục, nhãn "Tạm hết" / "Chỉ còn N ly", giá gạch ngang khi có KM |
| `/menu/[slug]` | Ảnh, mô tả, chọn size, chọn topping, hiển thị giá cập nhật theo lựa chọn |
| `/gio-hang` | Danh sách, sửa số lượng (**giới hạn theo `maxServings`**), tổng tiền |
| `/dat-mon` | Tên, SĐT, loại đơn, phương thức thanh toán, ghi chú |
| `/don-hang/[code]` | Trạng thái đơn, chi tiết, QR chuyển khoản nếu chọn VietQR |

### 8.2 Khối B — Quản lý

| Trang | Nội dung chính |
|---|---|
| `/dashboard` | 4 thẻ số: doanh thu hôm nay · đơn hôm nay · **giá trị cận hạn** · món sắp hết. Biểu đồ doanh thu 7 ngày. **Banner kế hoạch AI mới nhất.** |
| `/mon-an` | Bảng món: tên, giá, **giá vốn tính từ công thức**, biên lợi nhuận, khả dụng |
| `/mon-an/[id]` | Form món + **bảng công thức định lượng** (thêm nguyên liệu, nhập số lượng, xem giá vốn cập nhật realtime) |
| `/nguyen-lieu` | Bảng nguyên liệu: tồn hiện tại, đơn vị, giá vốn TB, ngưỡng tối thiểu |
| `/kho` | **Bảng lô hàng sắp theo hạn dùng.** Cột: nguyên liệu, mã lô, còn lại, hạn dùng, số ngày còn, giá trị. Tô màu theo mức độ. |
| `/kho/nhap` | Form nhập: nguyên liệu, số lượng + đơn vị, giá, hạn dùng, NCC |
| `/kho/kiem-ke` | Nhập số thực đếm, hệ thống tính chênh lệch, xác nhận tạo phiếu điều chỉnh |
| `/kho/hao-hut` | Ghi nhận đổ bỏ với lý do bắt buộc |
| `/don-hang` | Danh sách đơn realtime (poll 10s), nút chuyển trạng thái |
| **`/ke-hoach`** ⭐ | Danh sách kế hoạch theo ngày, tổng giá trị rủi ro |
| **`/ke-hoach/[date]`** ⭐ | Xem §8.3 |
| `/khuyen-mai` | Khuyến mãi đang chạy, nguồn gốc (AI hay tự tạo), hiệu quả thực tế |
| `/bao-cao` | Doanh thu, món bán chạy, hao hụt, giá vốn theo kỳ |

### 8.3 Màn hình kế hoạch AI ⭐ — bố cục

```
┌────────────────────────────────────────────────────────────────────┐
│  Kế hoạch ngày 10/08/2026                    [Đánh dấu đã xem]     │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┬──────────────┬──────────────┬──────────────┐     │
│  │ Giá trị rủi ro│  Khẩn cấp    │   Cảnh báo   │  Sắp hết hàng│     │
│  │  1.240.000đ   │      2       │      5       │      3       │     │
│  └──────────────┴──────────────┴──────────────┴──────────────┘     │
├────────────────────────────────────────────────────────────────────┤
│  💬 "3 nguyên liệu cần xả trong 48 giờ"                            │
│                                                                     │
│  Hôm nay doanh thu 4,2 triệu với 87 đơn. Có hai việc cần xử lý     │
│  ngay: 2,4 lít sữa tươi hết hạn sau 2 ngày và 1,8kg đào ngâm hết   │
│  hạn sau 3 ngày. Với tốc độ bán hiện tại, khoảng 1,1 lít sữa và    │
│  0,9kg đào sẽ phải bỏ đi — tương đương 1.240.000đ...               │
├────────────────────────────────────────────────────────────────────┤
│  BIỂU ĐỒ: Rủi ro theo số ngày còn lại                              │
│  ▓▓▓▓▓ 1 ngày    620.000đ  ← đỏ                                    │
│  ▓▓▓   3 ngày    420.000đ  ← cam                                   │
│  ▓     7 ngày    200.000đ  ← vàng                                  │
├────────────────────────────────────────────────────────────────────┤
│  ĐỀ XUẤT                                                            │
│                                                                     │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ 🔴 KHẨN CẤP · Giảm 20% Trà đào cam sả trong 2 ngày         │    │
│  │                                                             │    │
│  │ Đào ngâm còn 1,8kg, hết hạn 12/08. Bán trung bình 6 ly/ngày │    │
│  │ chỉ tiêu thụ được 0,9kg. Giảm 20% dự kiến bán thêm 8 ly,    │    │
│  │ đủ dùng hết trước hạn.                                       │    │
│  │                                                             │    │
│  │  Cứu được      +420.000đ                                    │    │
│  │  Lãi mất       −180.000đ                                    │    │
│  │  Lãi thêm       +96.000đ                                    │    │
│  │  ─────────────────────────                                  │    │
│  │  Lợi ích ròng  +336.000đ                                    │    │
│  │                                                             │    │
│  │  Banner: "Trà đào cam sả −20% — chỉ 2 ngày ☀️"              │    │
│  │                                                             │    │
│  │  [Duyệt & tạo khuyến mãi]  [Sửa]  [Bỏ qua]                 │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                     │
│  (các đề xuất tiếp theo...)                                        │
└────────────────────────────────────────────────────────────────────┘
```

---

## 09. Xác thực & phân quyền

```typescript
// src/server/auth.ts — ma trận quyền
export const PERMISSIONS = {
  OWNER:   ['*'],
  MANAGER: [
    'order:*', 'product:*', 'ingredient:*', 'inventory:*',
    'plan:read', 'plan:decide', 'promotion:*', 'report:*',
  ],
  STAFF: [
    'order:read', 'order:update-status',
    'inventory:receive', 'inventory:waste', 'inventory:read',
    'product:read', 'plan:read',
  ],
  CUSTOMER: ['order:create-own', 'order:read-own'],
} as const;
```

**Bảo vệ ở ba nơi (bắt buộc cả ba):**

1. **`src/middleware.ts`** — chặn route `/(admin)` nếu chưa đăng nhập
2. **`src/app/(admin)/layout.tsx`** — kiểm tra role, redirect nếu không đủ quyền
3. **Từng Route Handler** — `await requirePermission('inventory:receive')` ở dòng đầu

> Chỉ bảo vệ ở middleware là **không đủ** — API vẫn gọi trực tiếp được.

---

## 10. Biến môi trường

```bash
# .env.example

# --- Database ---
DATABASE_URL="postgresql://postgres:postgres@localhost:5432/qly_coffee"

# --- Auth ---
AUTH_SECRET="chạy: openssl rand -base64 32"
AUTH_URL="http://localhost:3000"
AUTH_GOOGLE_ID=""
AUTH_GOOGLE_SECRET=""

# --- AI ---
ANTHROPIC_API_KEY="sk-ant-..."

# --- Cron ---
CRON_SECRET="chuỗi ngẫu nhiên dài, dùng để bảo vệ endpoint cron"

# --- Store ---
DEFAULT_STORE_ID=""          # điền sau khi chạy seed
NEXT_PUBLIC_STORE_NAME="Qly Coffee"
TZ="Asia/Ho_Chi_Minh"

# --- Upload ---
UPLOADTHING_TOKEN=""

# --- Email ---
RESEND_API_KEY=""
PLAN_NOTIFICATION_EMAIL="chu-quan@example.com"

# --- Tham số nghiệp vụ (có thể chỉnh) ---
WASTE_RISK_HORIZON_DAYS="14"
MIN_MARGIN_PERCENT="15"
DEFAULT_PRICE_ELASTICITY="-1.6"
MAX_SUGGESTIONS_PER_PLAN="6"
```

**Validate env bằng Zod, fail sớm khi thiếu:**

```typescript
// src/env.ts
import { z } from 'zod';

const envSchema = z.object({
  DATABASE_URL: z.string().url(),
  AUTH_SECRET: z.string().min(32),
  ANTHROPIC_API_KEY: z.string().startsWith('sk-ant-'),
  CRON_SECRET: z.string().min(16),
  DEFAULT_STORE_ID: z.string().min(1),
  MIN_MARGIN_PERCENT: z.coerce.number().min(0).max(100).default(15),
  DEFAULT_PRICE_ELASTICITY: z.coerce.number().max(0).default(-1.6),
});

export const env = envSchema.parse(process.env);
```

---

## 11. Seed data — menu giới trẻ + công thức

> File `prisma/seed.ts`. Đây là dữ liệu **thật, dùng được ngay**, không phải placeholder.

### 11.1 Danh mục

```typescript
const categories = [
  { name: 'Cà phê',        slug: 'ca-phe',        sortOrder: 1 },
  { name: 'Trà sữa',       slug: 'tra-sua',       sortOrder: 2 },
  { name: 'Trà trái cây',  slug: 'tra-trai-cay',  sortOrder: 3 },
  { name: 'Đá xay & Sinh tố', slug: 'da-xay',     sortOrder: 4 },
  { name: 'Matcha & Cacao', slug: 'matcha-cacao', sortOrder: 5 },
  { name: 'Bánh & Ăn vặt', slug: 'banh-an-vat',   sortOrder: 6 },
];
```

### 11.2 Nguyên liệu (trích, cần đủ ~40 loại)

| SKU | Tên | Nhóm | Đơn vị | HSD (ngày) | Hao hụt | Giá/đơn vị |
|---|---|---|---|---|---|---|
| `COF-ROBUSTA-01` | Cà phê Robusta rang xay | COFFEE | G | 180 | 0.02 | 180 |
| `COF-ARABICA-01` | Cà phê Arabica rang xay | COFFEE | G | 180 | 0.02 | 320 |
| `DAI-MILK-01` | Sữa tươi không đường | DAIRY | ML | 7 | 0.03 | 32 |
| `DAI-CONDEN-01` | Sữa đặc | DAIRY | ML | 90 | 0.02 | 55 |
| `DAI-WHIP-01` | Kem sữa béo (whipping) | DAIRY | ML | 14 | 0.05 | 120 |
| `DAI-CHEESE-01` | Kem cheese | DAIRY | G | 10 | 0.05 | 180 |
| `TEA-BLACK-01` | Hồng trà | TEA | G | 365 | 0.02 | 250 |
| `TEA-OOLONG-01` | Trà ô long | TEA | G | 365 | 0.02 | 380 |
| `TEA-JASMINE-01` | Trà lài | TEA | G | 365 | 0.02 | 300 |
| `POW-MATCHA-01` | Bột matcha Nhật | POWDER | G | 180 | 0.03 | 1400 |
| `POW-CACAO-01` | Bột cacao | POWDER | G | 365 | 0.03 | 450 |
| `TOP-BOBA-01` | Trân châu đen | TOPPING | G | 3 | 0.08 | 45 |
| `TOP-BOBA-WHITE-01` | Trân châu trắng | TOPPING | G | 3 | 0.08 | 60 |
| `TOP-JELLY-01` | Thạch dừa | TOPPING | G | 14 | 0.05 | 40 |
| `TOP-PUDDING-01` | Pudding trứng | TOPPING | G | 3 | 0.06 | 70 |
| `FRU-PEACH-01` | Đào ngâm | FRUIT | G | 5 | 0.05 | 95 |
| `FRU-MANGO-01` | Xoài tươi | FRUIT | G | 4 | 0.12 | 55 |
| `FRU-LEMON-01` | Chanh tươi | FRUIT | PCS | 10 | 0.10 | 3000 |
| `FRU-LEMONGRASS-01` | Sả tươi | FRUIT | G | 7 | 0.15 | 35 |
| `SYR-SALT-CREAM-01` | Kem muối | SYRUP | ML | 5 | 0.04 | 140 |
| `SYR-SUGAR-01` | Nước đường | SYRUP | ML | 30 | 0.01 | 18 |
| `SWE-SUGAR-01` | Đường cát | SWEETENER | G | 730 | 0.01 | 22 |
| `OTH-ICE-01` | Đá viên | OTHER | G | null | 0.10 | 2 |
| `OTH-EGG-01` | Trứng gà | OTHER | PCS | 21 | 0.05 | 3500 |
| `PKG-CUP-M-01` | Ly nhựa 500ml + nắp | PACKAGING | PCS | null | 0.02 | 1800 |
| `PKG-CUP-L-01` | Ly nhựa 700ml + nắp | PACKAGING | PCS | null | 0.02 | 2200 |
| `PKG-STRAW-01` | Ống hút | PACKAGING | PCS | null | 0.03 | 350 |

### 11.3 Công thức định lượng mẫu ⭐

```typescript
// Món 1 — CÀ PHÊ MUỐI  (best-seller giới trẻ)
{
  name: 'Cà phê muối', slug: 'ca-phe-muoi', categorySlug: 'ca-phe',
  basePrice: 35000, tags: ['best-seller'],
  description: 'Cà phê phin đậm, phủ lớp kem muối béo mặn đặc trưng Huế.',
  recipe: [
    { sku: 'COF-ROBUSTA-01',   quantity: 20 },   // 20g cà phê
    { sku: 'DAI-CONDEN-01',    quantity: 20 },   // 20ml sữa đặc
    { sku: 'SYR-SALT-CREAM-01',quantity: 45 },   // 45ml kem muối
    { sku: 'OTH-ICE-01',       quantity: 180 },  // 180g đá
    { sku: 'PKG-CUP-M-01',     quantity: 1 },
    { sku: 'PKG-STRAW-01',     quantity: 1 },
  ],
  variants: [
    { name: 'Size M', priceDelta: 0,    recipeMultiplier: 1,   isDefault: true },
    { name: 'Size L', priceDelta: 8000, recipeMultiplier: 1.4 },
  ],
}

// Món 2 — CÀ PHÊ KEM TRỨNG
{
  name: 'Cà phê kem trứng', slug: 'ca-phe-kem-trung', categorySlug: 'ca-phe',
  basePrice: 42000, tags: ['mới'],
  recipe: [
    { sku: 'COF-ROBUSTA-01', quantity: 22 },
    { sku: 'OTH-EGG-01',     quantity: 1 },
    { sku: 'DAI-CONDEN-01',  quantity: 25 },
    { sku: 'DAI-WHIP-01',    quantity: 20 },
    { sku: 'PKG-CUP-M-01',   quantity: 1 },
  ],
}

// Món 3 — TRÀ SỮA TRÂN CHÂU ĐƯỜNG ĐEN
{
  name: 'Trà sữa trân châu đường đen', slug: 'tra-sua-tran-chau-duong-den',
  categorySlug: 'tra-sua', basePrice: 45000, tags: ['best-seller'],
  recipe: [
    { sku: 'TEA-BLACK-01',  quantity: 8 },
    { sku: 'DAI-MILK-01',   quantity: 200 },
    { sku: 'SYR-SUGAR-01',  quantity: 25 },
    { sku: 'TOP-BOBA-01',   quantity: 60 },
    { sku: 'OTH-ICE-01',    quantity: 150 },
    { sku: 'PKG-CUP-M-01',  quantity: 1 },
    { sku: 'PKG-STRAW-01',  quantity: 1 },
  ],
}

// Món 4 — TRÀ ĐÀO CAM SẢ  (dùng nguyên liệu HSD ngắn → hay xuất hiện trong kế hoạch AI)
{
  name: 'Trà đào cam sả', slug: 'tra-dao-cam-sa', categorySlug: 'tra-trai-cay',
  basePrice: 45000, tags: ['best-seller'],
  recipe: [
    { sku: 'TEA-BLACK-01',       quantity: 6 },
    { sku: 'FRU-PEACH-01',       quantity: 80 },   // ⭐ HSD 5 ngày
    { sku: 'FRU-LEMON-01',       quantity: 0.5 },
    { sku: 'FRU-LEMONGRASS-01',  quantity: 15 },
    { sku: 'SYR-SUGAR-01',       quantity: 30 },
    { sku: 'OTH-ICE-01',         quantity: 180 },
    { sku: 'PKG-CUP-M-01',       quantity: 1 },
  ],
}

// Món 5 — MATCHA LATTE
{
  name: 'Matcha latte', slug: 'matcha-latte', categorySlug: 'matcha-cacao',
  basePrice: 48000,
  recipe: [
    { sku: 'POW-MATCHA-01', quantity: 6 },
    { sku: 'DAI-MILK-01',   quantity: 220 },   // ⭐ HSD 7 ngày
    { sku: 'SYR-SUGAR-01',  quantity: 20 },
    { sku: 'OTH-ICE-01',    quantity: 150 },
    { sku: 'PKG-CUP-M-01',  quantity: 1 },
  ],
}

// Món 6 — SINH TỐ XOÀI
{
  name: 'Sinh tố xoài', slug: 'sinh-to-xoai', categorySlug: 'da-xay',
  basePrice: 50000,
  recipe: [
    { sku: 'FRU-MANGO-01',  quantity: 180 },   // ⭐ HSD 4 ngày, hao hụt 12%
    { sku: 'DAI-MILK-01',   quantity: 80 },
    { sku: 'SYR-SUGAR-01',  quantity: 25 },
    { sku: 'OTH-ICE-01',    quantity: 200 },
    { sku: 'PKG-CUP-L-01',  quantity: 1 },
  ],
}

// (Cần seed thêm ~18 món nữa: Bạc xỉu, Americano, Cold brew, Latte, Cappuccino,
//  Trà sữa ô long, Trà sữa matcha, Hồng trà chanh, Trà vải, Soda việt quất,
//  Cacao đá xay, Cookie đá xay, Sữa chua trân châu, Bánh su kem, Bánh tiramisu...)
```

### 11.4 Nhóm topping

```typescript
const modifierGroups = [
  {
    name: 'Topping', minSelect: 0, maxSelect: 3, isRequired: false,
    modifiers: [
      { name: 'Trân châu đen',  priceDelta: 8000,  recipe: [{ sku: 'TOP-BOBA-01',       quantity: 40 }] },
      { name: 'Trân châu trắng',priceDelta: 10000, recipe: [{ sku: 'TOP-BOBA-WHITE-01', quantity: 35 }] },
      { name: 'Thạch dừa',      priceDelta: 7000,  recipe: [{ sku: 'TOP-JELLY-01',      quantity: 40 }] },
      { name: 'Pudding trứng',  priceDelta: 10000, recipe: [{ sku: 'TOP-PUDDING-01',    quantity: 45 }] },
      { name: 'Kem cheese',     priceDelta: 12000, recipe: [{ sku: 'DAI-CHEESE-01',     quantity: 35 }] },
    ],
  },
  {
    name: 'Mức đường', minSelect: 1, maxSelect: 1, isRequired: true,
    modifiers: [
      { name: '100% đường', priceDelta: 0, recipe: [] },
      { name: '70% đường',  priceDelta: 0, recipe: [] },   // điều chỉnh ở §Ghi chú
      { name: '50% đường',  priceDelta: 0, recipe: [] },
      { name: 'Không đường',priceDelta: 0, recipe: [] },
    ],
  },
  {
    name: 'Mức đá', minSelect: 1, maxSelect: 1, isRequired: true,
    modifiers: [
      { name: '100% đá', priceDelta: 0, recipe: [] },
      { name: '70% đá',  priceDelta: 0, recipe: [] },
      { name: 'Ít đá',   priceDelta: 0, recipe: [] },
      { name: 'Không đá',priceDelta: 0, recipe: [] },
    ],
  },
];
```

> **Ghi chú về mức đường/đá:** Ở MVP, các mức này **không** điều chỉnh công thức
> (giữ `recipe: []`) vì tác động nhỏ và làm phức tạp logic. Nếu muốn chính xác hơn,
> mở rộng `ModifierRecipeItem` thêm cột `quantityDelta` cho phép giá trị âm
> (vd: "50% đường" → `SYR-SUGAR-01: -15ml`), rồi cộng dồn với công thức gốc và
> chặn kết quả không âm.

### 11.5 Lô hàng mẫu (để kế hoạch AI có việc để làm ngay)

```typescript
// Cố tình tạo vài lô cận hạn để test job cuối ngày
const seedLots = [
  { sku: 'DAI-MILK-01',  qty: 4000, unitCost: 32,  expiresInDays: 2 },  // ⭐ sắp hết hạn
  { sku: 'FRU-PEACH-01', qty: 1800, unitCost: 95,  expiresInDays: 3 },  // ⭐
  { sku: 'FRU-MANGO-01', qty: 2500, unitCost: 55,  expiresInDays: 4 },  // ⭐
  { sku: 'TOP-BOBA-01',  qty: 1500, unitCost: 45,  expiresInDays: 1 },  // ⭐ khẩn cấp
  { sku: 'COF-ROBUSTA-01', qty: 5000, unitCost: 180, expiresInDays: 150 },
  // ... các nguyên liệu còn lại với hạn dài
];
```

Đồng thời seed **28 ngày lịch sử `DailyConsumption`** giả lập để EWMA có dữ liệu chạy,
nếu không thì `avgDailyUsage = 0` và mọi lô đều bị coi là rủi ro 100%.

---

## 12. Jobs nền & lịch chạy

```json
// vercel.json
{
  "crons": [
    { "path": "/api/cron/daily-plan",          "schedule": "0 16 * * *" },
    { "path": "/api/cron/expiry-check",        "schedule": "0 1 * * *"  },
    { "path": "/api/cron/refresh-availability","schedule": "*/15 * * * *" }
  ]
}
```

> ⚠️ **Vercel Cron chạy theo UTC.** `0 16 * * *` UTC = **23:00 giờ Việt Nam**.
> Đây là lỗi hay gặp nhất khi làm cron cho thị trường VN — luôn quy đổi UTC+7.

| Job | Giờ VN | Việc làm |
|---|---|---|
| `daily-plan` | 23:00 | Tổng hợp tiêu thụ → phân tích rủi ro → sinh đề xuất → gọi Claude → lưu + gửi mail |
| `expiry-check` | 08:00 | Đánh dấu lô `EXPIRED`, ghi `StockMovement` type `EXPIRED_OUT`, gửi cảnh báo sáng |
| `refresh-availability` | mỗi 15 phút | Đồng bộ lại `Product.isAvailable` (phòng trường hợp lệch do lỗi) |

**Self-host (không dùng Vercel):** dùng `node-cron` trong một process riêng gọi HTTP
tới các endpoint trên, kèm header `x-cron-secret`.

---

## 13. Lộ trình build cho vibe coding

> Mỗi bước là một prompt riêng cho AI coding assistant. **Không gộp bước.**
> Chỉ sang bước sau khi tiêu chí nghiệm thu đã pass.

### Bước 1 — Khởi tạo dự án

**Việc:** `create-next-app` với TypeScript + Tailwind + App Router. Cài Prisma, Zod,
shadcn/ui, TanStack Query, Auth.js. Tạo `docker-compose.yml` cho Postgres. Tạo cấu trúc
thư mục theo §03.

**Nghiệm thu:** `pnpm dev` chạy được, `pnpm prisma db push` kết nối được DB.

---

### Bước 2 — Schema & seed

**Việc:** Copy toàn bộ §04 vào `prisma/schema.prisma`. Chạy migration. Viết
`prisma/seed.ts` theo §11 — đủ 6 danh mục, ~27 nguyên liệu, ≥ 12 món có công thức,
3 nhóm topping, lô hàng mẫu, và 28 ngày `DailyConsumption` giả lập.

**Nghiệm thu:**
- `pnpm prisma migrate dev` thành công
- `pnpm prisma db seed` chạy xong không lỗi
- Mở Prisma Studio thấy đủ dữ liệu, mỗi món có ít nhất 4 dòng `RecipeItem`

---

### Bước 3 — Thư viện nền

**Việc:** Viết `src/server/lib/units.ts`, `money.ts`, `date.ts`, `errors.ts` theo §5.1.
`date.ts` phải có `getBusinessDate(date)` trả về `YYYY-MM-DD` theo giờ VN.

**Nghiệm thu:** Unit test pass cho:
- `toBaseUnit(2, 'KG', 'G') === 2000`
- `toBaseUnit(1, 'L', 'G')` ném lỗi
- `getBusinessDate(new Date('2026-08-10T18:30:00Z')) === '2026-08-11'` (vì UTC+7)

---

### Bước 4 — Recipe service ⭐

**Việc:** Viết `recipe.service.ts` theo §5.2: `explodeOrderItem()` và `computeProductCost()`.

**Nghiệm thu (test bắt buộc):**
- Cà phê muối size M, không topping → đúng 6 nguyên liệu với số lượng khớp seed
- Cà phê muối size L → mọi nguyên liệu × 1.4
- Trà sữa + trân châu đen → có `TOP-BOBA-01` với quantity = `60 + 40 = 100` (topping **không** nhân hệ số size)
- Số lượng đã nhân `(1 + wastageRate)`

---

### Bước 5 — Inventory service ⭐⭐

**Việc:** Viết `inventory.service.ts` theo §5.4 và §5.6: `consumeFefo()`,
`receiveStock()`, `getAvailableStock()`, `returnStockForOrder()`,
`updateWeightedAverageCost()`.

**Nghiệm thu (đây là phần dễ sai nhất — test kỹ):**
- Có 2 lô: lô A hết hạn 10/08 (100g), lô B hết hạn 20/08 (100g). Tiêu thụ 150g
  → lô A về 0 và `DEPLETED`, lô B còn 50g
- Lô không có `expiryDate` được dùng **sau cùng**
- Tiêu thụ 250g khi chỉ có 200g → ném `InsufficientStockError`, **không có lô nào bị thay đổi**
- Gọi `consumeFefo` hai lần với cùng `idempotencyKey` → chỉ trừ một lần
- Nhập 2 lô giá 100đ và 200đ, mỗi lô 50 đơn vị → `avgUnitCost === 150`
- Chạy 20 lần `consumeFefo` song song trên cùng nguyên liệu → tổng trừ đúng, không âm

---

### Bước 6 — Order service ⭐

**Việc:** Viết `order.service.ts` theo §5.3: `createOrder()`, `confirmOrder()`,
`cancelOrder()`, `updateStatus()`.

**Nghiệm thu:**
- Tạo đơn → `status = PENDING`, `stockDeducted = false`, kho **không đổi**
- Confirm → kho giảm đúng theo công thức, `costTotal > 0`, `stockDeducted = true`
- Confirm lần hai → ném lỗi, kho **không** giảm thêm
- Hủy đơn đã confirm → kho trở về đúng số ban đầu, `stockReturned = true`
- Confirm đơn khi thiếu nguyên liệu → toàn bộ transaction rollback, đơn vẫn `PENDING`

---

### Bước 7 — Availability service

**Việc:** Viết `availability.service.ts` theo §5.5. Gọi `recomputeAvailability()`
sau mọi thay đổi kho.

**Nghiệm thu:**
- Xóa hết sữa tươi → Matcha latte và Trà sữa có `isAvailable = false`,
  `unavailableReason = "Hết Sữa tươi không đường"`
- Còn đủ sữa cho đúng 3 ly → `maxServings === 3`

---

### Bước 8 — API + giao diện cửa hàng

**Việc:** Route Handlers §7.1. Trang `/`, `/menu`, `/menu/[slug]`, `/gio-hang`,
`/dat-mon`, `/don-hang/[code]`. Giỏ hàng dùng Zustand + persist localStorage.

**Nghiệm thu:**
- Đặt được đơn từ đầu đến cuối, nhận mã đơn
- Món hết hàng hiện nhãn "Tạm hết", không thêm vào giỏ được
- Giỏ hàng không cho tăng quá `maxServings`

---

### Bước 9 — Giao diện quản lý: món & kho

**Việc:** Trang `/mon-an`, `/mon-an/[id]` (có bảng công thức định lượng),
`/nguyen-lieu`, `/kho`, `/kho/nhap`, `/kho/hao-hut`, `/don-hang`.

**Nghiệm thu:**
- Sửa công thức → `computedCost` cập nhật ngay trên UI
- Nhập kho → thấy lô mới trong `/kho`, sắp đúng theo hạn dùng
- Xác nhận đơn → thấy tồn kho giảm trong `/nguyen-lieu`

---

### Bước 10 — Forecast & Waste-risk service ⭐

**Việc:** Viết `forecast.service.ts` (EWMA) và `waste-risk.service.ts` theo §6.2.

**Nghiệm thu:**
- `analyzeWasteRisk()` trả về đúng các lô cận hạn từ seed
- Lô có `avgDailyUsage` đủ lớn để dùng hết trước hạn → **không** xuất hiện trong danh sách
- `severity` phân loại đúng theo ma trận

---

### Bước 11 — Promotion candidates ⭐

**Việc:** Viết `promotion.service.ts` theo §6.3.

**Nghiệm thu:**
- Không có phương án nào đẩy biên lợi nhuận xuống dưới `MIN_MARGIN_PERCENT`
- `netBenefit` tính đúng theo công thức: `wasteAvoided + extraMargin − marginGivenUp`
- Chỉ trả về phương án có `netBenefit > 0`
- Mỗi món chỉ có một phương án tốt nhất

---

### Bước 12 — AI narrative ⭐

**Việc:** Viết `src/server/ai/*` theo §6.4. Bao gồm cả `buildFallbackNarrative()`.

**Nghiệm thu:**
- Gọi thật với `ANTHROPIC_API_KEY` → nhận JSON đúng schema
- Ngắt mạng / dùng key sai → vẫn tạo được kế hoạch, `aiError` được điền
- AI trả về `candidateId` không tồn tại → bị lọc bỏ, không lưu vào DB

---

### Bước 13 — Job cuối ngày & trang kế hoạch ⭐

**Việc:** `/api/cron/daily-plan` theo §6.5. Trang `/ke-hoach`, `/ke-hoach/[date]`
theo bố cục §8.3. Chức năng duyệt/từ chối/sửa đề xuất → tạo `Promotion`.

**Nghiệm thu:**
- Gọi cron thủ công bằng `curl` → tạo được `DailyPlan` với `suggestions`
- Gọi lần hai cùng ngày → trả `skipped: true`, không tạo bản trùng
- Duyệt một đề xuất → `Promotion` được tạo với `status = ACTIVE`
- Khuyến mãi vừa duyệt hiện lên banner trang chủ `/` và giá món bị gạch ngang

---

### Bước 14 — Báo cáo & hoàn thiện

**Việc:** `/dashboard`, `/bao-cao`, `/khuyen-mai`. Job `expiry-check`,
`refresh-availability`. Phân quyền theo §09. SEO cơ bản cho `/menu`.

**Nghiệm thu:** Tài khoản `STAFF` không vào được `/bao-cao` (cả UI và API).

---

## 14. Testing

### 14.1 Ưu tiên test

| Mức | Đối tượng | Công cụ | Bắt buộc |
|---|---|---|---|
| **P0** | `consumeFefo`, `confirmOrder`, `explodeOrderItem` | Vitest + Postgres test container | ✅ Không thương lượng |
| **P1** | `generatePromotionCandidates`, `analyzeWasteRisk` | Vitest (pure function) | ✅ |
| **P2** | Luồng đặt món end-to-end | Playwright | ✅ |
| **P3** | UI components | Vitest + Testing Library | Tùy |

### 14.2 Bộ test tối thiểu phải có

```typescript
// tests/inventory.test.ts
describe('consumeFefo', () => {
  it('tiêu thụ lô hết hạn sớm nhất trước');
  it('lô không có hạn dùng được xếp cuối');
  it('ném lỗi khi không đủ tồn và không thay đổi lô nào');
  it('idempotent — gọi hai lần chỉ trừ một lần');
  it('an toàn khi chạy song song');
  it('cập nhật status DEPLETED khi lô về 0');
});

describe('confirmOrder', () => {
  it('trừ đúng nguyên liệu theo công thức đã nhân hệ số size');
  it('cộng đúng nguyên liệu của topping');
  it('rollback toàn bộ khi một nguyên liệu thiếu');
  it('không trừ kho hai lần khi gọi lặp');
  it('hoàn kho đúng lô khi hủy đơn');
});

describe('generatePromotionCandidates', () => {
  it('không tạo phương án đẩy biên lợi nhuận dưới ngưỡng');
  it('netBenefit = wasteAvoided + extraMargin - marginGivenUp');
  it('loại phương án có netBenefit âm');
});
```

---

## 15. Mười lỗi hay gặp khi vibe coding phần này

| # | Lỗi | Hậu quả | Cách tránh |
|---|---|---|---|
| 1 | **Trừ kho ngoài transaction** | Đơn tạo thành công nhưng kho không trừ, hoặc ngược lại | Mọi thao tác kho nằm trong `db.$transaction` với `isolationLevel: 'Serializable'` |
| 2 | **Quên `FOR UPDATE` khi đọc lô** | Hai đơn cùng lấy lô cuối → tồn kho âm | Luôn dùng `$queryRaw` với `FOR UPDATE` trong `consumeFefo` |
| 3 | **Không có idempotency key** | Retry mạng → trừ kho hai lần | `StockMovement.idempotencyKey` unique + kiểm tra trước khi trừ |
| 4 | **Dùng `Float` cho tiền** | Sai số tích lũy, báo cáo lệch | Tiền luôn là `Int` (đồng). Chỉ chia khi hiển thị |
| 5 | **Nhân hệ số size vào topping** | Thêm trân châu size L bị trừ 42g thay vì 30g | Xem `explodeOrderItem` §5.2 — hệ số chỉ áp vào công thức gốc |
| 6 | **Tính ngày kinh doanh theo UTC** | Job cuối ngày chạy sai ngày, số liệu lệch | Luôn dùng `getBusinessDate()` với `Asia/Ho_Chi_Minh` |
| 7 | **Cron Vercel đặt giờ VN** | Job chạy lúc 23:00 UTC = 6 giờ sáng hôm sau | Vercel Cron dùng UTC — đặt `0 16 * * *` cho 23:00 VN |
| 8 | **Để LLM tính toán số tiền** | Báo cáo tài chính sai mà không ai phát hiện | LLM chỉ nhận số đã tính và diễn giải. Xem §6.1 |
| 9 | **Tin `candidateId` do AI trả về** | Lưu đề xuất trỏ tới món không tồn tại | Lọc bằng `validIds` sau khi parse — §6.4 |
| 10 | **`UPDATE` hoặc `DELETE` trên `StockMovement`** | Mất khả năng đối soát, không truy được nguồn gốc sai lệch | Sổ cái là append-only. Sai thì ghi bút toán đảo, không sửa |

---

## Phụ lục A — Sơ đồ luồng dữ liệu tổng thể

```
  KHÁCH ĐẶT MÓN                      NHÂN VIÊN XÁC NHẬN
       │                                     │
       ▼                                     ▼
  ┌─────────┐    kiểm tra khả dụng    ┌──────────────┐
  │ Order   │───────────────────────► │ confirmOrder │
  │ PENDING │                         └──────┬───────┘
  └─────────┘                                │ transaction
                                             ▼
                              ┌──────────────────────────┐
                              │  explodeOrderItem()      │
                              │  công thức × size + topping│
                              │  × (1+hao hụt) × số ly   │
                              └──────────┬───────────────┘
                                         ▼
                              ┌──────────────────────────┐
                              │  consumeFefo()           │
                              │  lô hết hạn sớm nhất trước│
                              └──────────┬───────────────┘
                                         │
                     ┌───────────────────┼───────────────────┐
                     ▼                   ▼                   ▼
            ┌────────────────┐  ┌────────────────┐  ┌────────────────┐
            │ InventoryLot   │  │ StockMovement  │  │ Order          │
            │ remainingQty ↓ │  │ SALE_OUT (âm)  │  │ costTotal      │
            └────────┬───────┘  └────────┬───────┘  │ stockDeducted  │
                     │                   │          └────────────────┘
                     │                   │
                     ▼                   ▼
            ┌────────────────┐  ┌──────────────────────┐
            │recomputeAvail. │  │ DailyConsumption     │
            │Product.isAvail.│  │ (tổng hợp cuối ngày) │
            └────────────────┘  └──────────┬───────────┘
                                           │
                        ═══════ 23:00 mỗi ngày ═══════
                                           ▼
                              ┌──────────────────────────┐
                              │  analyzeWasteRisk()      │  Lớp 2
                              │  EWMA → qty at risk      │  (code)
                              └──────────┬───────────────┘
                                         ▼
                              ┌──────────────────────────┐
                              │generatePromotionCandidates│ Lớp 2
                              │  co giãn giá → netBenefit│  (code)
                              └──────────┬───────────────┘
                                         ▼
                              ┌──────────────────────────┐
                              │   Claude API             │  Lớp 3
                              │   diễn giải + banner copy│  (LLM)
                              └──────────┬───────────────┘
                                         ▼
                              ┌──────────────────────────┐
                              │  DailyPlan + Suggestions │
                              └──────────┬───────────────┘
                                         ▼
                     ┌───────────────────┴───────────────────┐
                     ▼                                       ▼
          ┌────────────────────┐                  ┌────────────────────┐
          │  Trang /ke-hoach   │  quản lý duyệt   │   Email thông báo  │
          └─────────┬──────────┘                  └────────────────────┘
                    │ approveSuggestion()
                    ▼
          ┌────────────────────┐
          │   Promotion ACTIVE │────► banner + giá giảm trên trang bán hàng
          └────────────────────┘
```

---

## Phụ lục B — Checklist trước khi deploy production

- [ ] `pnpm build` không có lỗi TypeScript
- [ ] Tất cả test P0 và P1 pass
- [ ] `CRON_SECRET` đã đặt và endpoint cron trả 401 khi thiếu header
- [ ] Backup database tự động đã bật (Neon/Supabase PITR)
- [ ] `DEFAULT_STORE_ID` trỏ đúng store thật, không phải store seed
- [ ] Đã test job `daily-plan` trên staging với dữ liệu thật
- [ ] Rate limit cho `/api/orders` (chống spam đơn)
- [ ] Sentry đã gắn, có alert cho lỗi trong `consumeFefo` và `confirmOrder`
- [ ] Có runbook: phải làm gì khi tồn kho lệch so với thực tế
- [ ] Trang chính sách bảo mật (Nghị định 13/2023) — vì có lưu SĐT khách

---

*Tài liệu này là hợp đồng kỹ thuật. Khi thực tế khác tài liệu, sửa tài liệu trước rồi mới sửa code.*
