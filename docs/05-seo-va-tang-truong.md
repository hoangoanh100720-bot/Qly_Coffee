# 05 — Chiến lược SEO & Tăng trưởng

> **Insight nền tảng:** Ta có **hai mặt trận SEO**, không phải một. Hầu hết SaaS chỉ làm mặt trận 1 và bỏ lỡ hoàn toàn mặt trận 2 — nơi có lợi thế quy mô khổng lồ.

```
┌─── MẶT TRẬN 1 ────────────────┐   ┌─── MẶT TRẬN 2 ─────────────────────┐
│  qlycoffee.vn                  │   │  Trang công khai của TỪNG quán     │
│  Website SaaS                   │   │  (programmatic + local SEO)        │
│                                 │   │                                    │
│  Đối tượng: chủ quán cà phê     │   │  Đối tượng: khách uống cà phê      │
│  Từ khóa: "phần mềm quản lý     │   │  Từ khóa: "cà phê quận 3",         │
│            quán cà phê"         │   │            "menu {tên quán}"       │
│  Quy mô: ~200 trang             │   │  Quy mô: 1.000 quán × 60 trang     │
│                                 │   │           = 60.000 trang           │
│  Mục tiêu: thu hút khách hàng   │   │  Mục tiêu: tạo giá trị cho khách   │
│            SaaS                 │   │  hàng → giữ chân + lan truyền      │
└─────────────────────────────────┘   └────────────────────────────────────┘
```

**Mặt trận 2 là vòng lặp tăng trưởng:** Mỗi quán mới → thêm 60 trang chất lượng → tổng authority domain tăng → trang SaaS xếp hạng tốt hơn → thêm quán mới. Đối thủ dùng app native không có lợi thế này.

---

## PHẦN A — SEO KỸ THUẬT (nền móng)

### A1. Chiến lược render trong Next.js 15 App Router

Mỗi loại trang có chiến lược riêng. Chọn sai = mất index hoặc chậm.

| Loại trang | Chiến lược | Cấu hình | Lý do |
|---|---|---|---|
| Landing, pricing, about | **SSG** | `export const dynamic = 'force-static'` | Nội dung tĩnh, cần nhanh tối đa |
| Blog, hướng dẫn | **SSG + ISR** | `export const revalidate = 3600` | Cập nhật thỉnh thoảng |
| Trang menu công khai của quán | **ISR** | `revalidate = 300` + on-demand | Giá/món thay đổi, cần tươi |
| Trang chi nhánh (địa điểm) | **ISR** | `revalidate = 3600` | Giờ mở cửa ít đổi |
| Đặt món (giỏ hàng, ETA) | **CSR** | `'use client'` | Cá nhân hóa, không cần index |
| Admin dashboard | **CSR** | `noindex` | Không index |

**On-demand revalidation** khi chủ quán sửa menu:

```typescript
// apps/api/src/menu/menu.service.ts — sau khi cập nhật menu
await fetch(`${WEB_URL}/api/revalidate`, {
  method: 'POST',
  headers: { 'x-revalidate-secret': process.env.REVALIDATE_SECRET! },
  body: JSON.stringify({ tags: [`store:${storeId}`, `menu:${storeId}`] }),
});

// apps/web/app/api/revalidate/route.ts
import { revalidateTag } from 'next/cache';
export async function POST(req: Request) {
  // xác thực secret...
  for (const tag of tags) revalidateTag(tag);
  return Response.json({ revalidated: true });
}
```

> ⚠️ **Cạm bẫy:** Đừng dùng `force-dynamic` cho trang cần SEO. Googlebot có ngân sách render JS hạn chế; SSR/SSG luôn an toàn hơn.

---

### A2. Kiến trúc URL — quyết định quan trọng nhất

**Câu hỏi: subdomain (`quan-abc.qlycoffee.vn`) hay subfolder (`qlycoffee.vn/quan/quan-abc`)?**

| | Subdomain | **Subfolder** ✅ |
|---|---|---|
| Chia sẻ authority với domain chính | ❌ Google coi gần như site riêng | ✅ Toàn bộ authority dồn về 1 domain |
| Quản lý sitemap | Phức tạp (mỗi sub 1 sitemap) | Đơn giản (sitemap index) |
| Branding cho quán | ✅ Đẹp hơn | ⚠️ Kém hơn |
| SSL, hạ tầng | Cần wildcard cert | Đơn giản |

**Quyết định: dùng subfolder cho mặc định; cho phép custom domain riêng ở gói Chain.**

```
qlycoffee.vn/
├── /                                    Trang chủ SaaS
├── /tinh-nang/du-doan-thoi-gian-cho     Landing từng tính năng
├── /bang-gia
├── /giai-phap/quan-ca-phe-nho
├── /giai-phap/chuoi-ca-phe
├── /so-sanh/qlycoffee-vs-kiotviet       ⭐ trang so sánh — traffic cao
├── /blog/{slug}
├── /huong-dan/{slug}                    Trung tâm trợ giúp
├── /tu-dien/{slug}                      Từ điển thuật ngữ F&B
│
├── /quan/{store-slug}                   ⭐ Trang quán (mặt trận 2)
│   ├── /menu
│   ├── /menu/{product-slug}             Trang từng món
│   ├── /dat-mon                         (noindex — có state)
│   └── /gio-mo-cua
│
├── /ca-phe/{tinh-thanh}                 Trang tổng hợp theo địa phương
│   └── /{quan-huyen}                    "Quán cà phê Quận 3"
│
├── /sitemap.xml                         Sitemap index
├── /robots.txt
└── /llms.txt                            ⭐ cho AI crawler
```

**Quy tắc slug:**
- Không dấu, gạch nối, chữ thường: `ca-phe-sua-da`
- Bao gồm từ khóa, không nhồi nhét
- **Bất biến** sau khi publish — đổi slug phải có redirect 301
- Có tiền tố phân biệt để tránh xung đột: `/quan/`, `/ca-phe/`

---

### A3. Metadata — dùng Metadata API, không dùng thẻ thủ công

```typescript
// apps/web/app/quan/[slug]/page.tsx
import type { Metadata } from 'next';

export async function generateMetadata(
  { params }: { params: Promise<{ slug: string }> }
): Promise<Metadata> {
  const { slug } = await params;
  const store = await getStoreBySlug(slug);
  if (!store) return { title: 'Không tìm thấy quán' };

  const title = `${store.name} — Menu & Đặt món online | ${store.district}, ${store.province}`;
  const description =
    `Xem menu ${store.productCount} món của ${store.name} tại ${store.address}. ` +
    `Đặt món online, biết trước thời gian chờ. Mở cửa ${store.openingHoursText}.`;

  return {
    title,                                        // ≤ 60 ký tự phần thương hiệu
    description,                                  // 150–160 ký tự
    alternates: {
      canonical: `https://qlycoffee.vn/quan/${slug}`,
      languages: {
        'vi-VN': `https://qlycoffee.vn/quan/${slug}`,
        'en-US': `https://qlycoffee.vn/en/quan/${slug}`,
      },
    },
    openGraph: {
      type: 'website',
      locale: 'vi_VN',
      url: `https://qlycoffee.vn/quan/${slug}`,
      siteName: 'Qly Coffee',
      title,
      description,
      images: [{
        url: store.ogImageUrl ?? `https://qlycoffee.vn/api/og?store=${slug}`,
        width: 1200, height: 630, alt: `Menu ${store.name}`,
      }],
    },
    twitter: { card: 'summary_large_image', title, description },
    robots: {
      index: store.isPublic && store.productCount >= 5,   // ⚠️ chống thin content
      follow: true,
      googleBot: { index: store.isPublic, 'max-image-preview': 'large',
                   'max-snippet': -1, 'max-video-preview': -1 },
    },
  };
}
```

**Ảnh OG động** — sinh bằng `next/og` (Edge runtime), cache CDN:

```typescript
// apps/web/app/api/og/route.tsx
import { ImageResponse } from 'next/og';
export const runtime = 'edge';
// → ảnh 1200×630 có logo quán, tên, số món, đánh giá
```

---

### A4. Structured Data (JSON-LD) — nơi tạo khác biệt lớn nhất

Đây là phần **hầu hết đối thủ bỏ qua hoàn toàn**. Structured data đúng chuẩn cho phép hiển thị rich result trên Google (sao đánh giá, giờ mở cửa, giá món, bản đồ) → CTR cao hơn 20–40%.

#### Trang chủ SaaS

```typescript
const softwareSchema = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: 'Qly Coffee',
  applicationCategory: 'BusinessApplication',
  applicationSubCategory: 'Point of Sale Software',
  operatingSystem: 'Web, iOS, Android',
  offers: {
    '@type': 'AggregateOffer',
    priceCurrency: 'VND',
    lowPrice: '0', highPrice: '899000',
    offerCount: '4',
  },
  aggregateRating: {              // ⚠️ CHỈ khi có đánh giá THẬT
    '@type': 'AggregateRating',
    ratingValue: '4.8', reviewCount: '127',
  },
  featureList: [
    'Dự đoán thời gian chờ bằng AI',
    'Quản lý bán hàng offline',
    'Hóa đơn điện tử',
  ],
};
```

#### Trang quán — schema phức hợp (đây là phần mạnh nhất)

```typescript
const storeSchema = {
  '@context': 'https://schema.org',
  '@type': 'CafeOrCoffeeShop',
  '@id': `https://qlycoffee.vn/quan/${store.slug}#business`,
  name: store.name,
  image: store.images,
  url: `https://qlycoffee.vn/quan/${store.slug}`,
  telephone: store.phone,
  priceRange: '₫₫',
  currenciesAccepted: 'VND',
  paymentAccepted: 'Tiền mặt, Thẻ, Momo, ZaloPay, VietQR',
  address: {
    '@type': 'PostalAddress',
    streetAddress: store.address,
    addressLocality: store.ward,
    addressRegion: store.district,
    addressCountry: 'VN',
  },
  geo: { '@type': 'GeoCoordinates', latitude: store.lat, longitude: store.lng },
  openingHoursSpecification: store.openingHours.map((h) => ({
    '@type': 'OpeningHoursSpecification',
    dayOfWeek: h.days,                    // ["Monday","Tuesday",...]
    opens: h.opens, closes: h.closes,     // "07:00", "22:00"
  })),
  hasMenu: {
    '@type': 'Menu',
    '@id': `https://qlycoffee.vn/quan/${store.slug}/menu#menu`,
    hasMenuSection: categories.map((cat) => ({
      '@type': 'MenuSection',
      name: cat.name,
      hasMenuItem: cat.products.map((p) => ({
        '@type': 'MenuItem',
        name: p.name,
        description: p.description,
        image: p.imageUrl,
        offers: { '@type': 'Offer', price: p.price, priceCurrency: 'VND' },
        suitableForDiet: p.dietTags,      // vd: VeganDiet
      })),
    })),
  },
  acceptsReservations: store.acceptsReservations ? 'True' : 'False',
  potentialAction: {
    '@type': 'OrderAction',
    target: {
      '@type': 'EntryPoint',
      urlTemplate: `https://qlycoffee.vn/quan/${store.slug}/dat-mon`,
      actionPlatform: [
        'http://schema.org/DesktopWebPlatform',
        'http://schema.org/MobileWebPlatform',
      ],
    },
    deliveryMethod: ['http://purl.org/goodrelations/v1#DeliveryModePickUp'],
  },
};
```

#### Bảng schema theo loại trang

| Trang | Schema types |
|---|---|
| Trang chủ SaaS | `Organization`, `SoftwareApplication`, `WebSite` (+ `SearchAction`) |
| Bảng giá | `Product` + `AggregateOffer` |
| Blog | `Article` / `BlogPosting`, `BreadcrumbList`, `Person` (tác giả) |
| Hướng dẫn | `HowTo`, `BreadcrumbList` |
| FAQ | `FAQPage` |
| Trang quán | `CafeOrCoffeeShop`, `Menu`, `BreadcrumbList`, `AggregateRating` |
| Trang món | `MenuItem`, `Offer`, `NutritionInformation` (nếu có) |
| Trang so sánh | `Article` + `FAQPage` |
| Trang địa phương | `CollectionPage` + `ItemList` |

**Kiểm định bắt buộc trong CI:**
```bash
# Test tự động mọi loại trang qua Schema Markup Validator
pnpm test:schema     # so sánh JSON-LD với schema.org spec
```

> ⚠️ **Cảnh báo nghiêm trọng về `aggregateRating`:** Chỉ xuất khi có đánh giá thật từ khách. Bịa đánh giá là vi phạm chính sách spam của Google và có thể bị phạt thủ công toàn site. Không đáng.

---

### A5. Sitemap động phân mảnh

Với 60.000+ URL, phải dùng sitemap index (mỗi file tối đa 50.000 URL / 50MB).

```typescript
// apps/web/app/sitemap.ts  — sitemap index
import type { MetadataRoute } from 'next';

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const storeCount = await countPublicStores();
  const shards = Math.ceil(storeCount / 5000);
  return [
    { url: 'https://qlycoffee.vn/sitemap/static.xml', lastModified: new Date() },
    { url: 'https://qlycoffee.vn/sitemap/blog.xml', lastModified: new Date() },
    { url: 'https://qlycoffee.vn/sitemap/locations.xml', lastModified: new Date() },
    ...Array.from({ length: shards }, (_, i) => ({
      url: `https://qlycoffee.vn/sitemap/stores-${i}.xml`,
      lastModified: new Date(),
    })),
  ];
}
```

```typescript
// apps/web/app/sitemap/stores-[shard]/route.ts
export const revalidate = 3600;

export async function GET(_: Request, { params }: { params: { shard: string } }) {
  const stores = await getPublicStores({
    offset: Number(params.shard) * 5000, limit: 5000,
  });
  const urls = stores.flatMap((s) => [
    { loc: `/quan/${s.slug}`, lastmod: s.updatedAt, changefreq: 'weekly', priority: 0.8 },
    { loc: `/quan/${s.slug}/menu`, lastmod: s.menuUpdatedAt, changefreq: 'daily', priority: 0.7 },
    ...s.featuredProducts.map((p) => ({
      loc: `/quan/${s.slug}/menu/${p.slug}`, lastmod: p.updatedAt, priority: 0.5,
    })),
  ]);
  return new Response(buildXml(urls), {
    headers: { 'Content-Type': 'application/xml' },
  });
}
```

**Quy tắc quan trọng:** Chỉ đưa vào sitemap các URL **đang trả 200 và được index**. Sitemap chứa URL 404/noindex/redirect làm giảm tin cậy — Google sẽ crawl sitemap thưa hơn.

---

### A6. robots.txt & llms.txt

```
# apps/web/public/robots.txt (hoặc app/robots.ts)
User-agent: *
Allow: /
Disallow: /admin
Disallow: /api/
Disallow: /*/dat-mon
Disallow: /*?utm_
Disallow: /*?ref=
Disallow: /search

# Cho AI crawler đọc (chiến lược GEO — Generative Engine Optimization)
User-agent: GPTBot
Allow: /
User-agent: ClaudeBot
Allow: /
User-agent: PerplexityBot
Allow: /

Sitemap: https://qlycoffee.vn/sitemap.xml
```

**`/llms.txt`** — chuẩn mới cho AI, giúp ChatGPT/Claude/Perplexity trích dẫn đúng khi người dùng hỏi "phần mềm quản lý quán cà phê nào tốt":

```markdown
# Qly Coffee

> Nền tảng SaaS quản lý quán cà phê tại Việt Nam, tích hợp AI dự đoán thời
> gian hoàn thành món và tối ưu hàng đợi pha chế.

## Sản phẩm
- [Dự đoán thời gian chờ](/tinh-nang/du-doan-thoi-gian-cho): AI kết hợp mô
  phỏng hàng đợi và học máy, sai số trung bình dưới 90 giây.
- [Bảng giá](/bang-gia): từ miễn phí đến 899.000đ/tháng/chi nhánh.

## Tài liệu
- [Hướng dẫn bắt đầu](/huong-dan/bat-dau)
- [API docs](/docs/api)
```

---

### A7. Core Web Vitals — ngân sách hiệu năng nghiêm ngặt

| Chỉ số | Ngưỡng "Good" | **Mục tiêu của ta** | Cách đạt |
|---|---|---|---|
| **LCP** | < 2.5s | **< 1.8s** | SSG/ISR, `priority` cho ảnh hero, preconnect CDN, font `display: swap` + preload |
| **INP** | < 200ms | **< 120ms** | Giảm JS, `useTransition`, tránh long task, virtualize danh sách dài |
| **CLS** | < 0.1 | **< 0.05** | Luôn set `width`/`height` cho ảnh, reserve space cho ad/banner, không chèn DOM trên viewport |
| **TTFB** | < 800ms | **< 300ms** | Edge cache, ISR, DB gần user |

**Cụ thể trong Next.js:**

```tsx
// Ảnh — luôn dùng next/image với sizes chính xác
<Image
  src={store.heroImage}
  alt={`Không gian quán ${store.name}`}
  width={1200} height={630}
  priority                                    // chỉ cho ảnh LCP
  sizes="(max-width: 768px) 100vw, 1200px"
  placeholder="blur" blurDataURL={store.blurHash}
/>

// Font — self-host qua next/font, tránh request bên thứ 3
import { Be_Vietnam_Pro } from 'next/font/google';
const font = Be_Vietnam_Pro({
  subsets: ['vietnamese', 'latin'],
  weight: ['400', '600', '700'],
  display: 'swap',
  variable: '--font-sans',
});

// Component nặng — dynamic import, tắt SSR nếu không cần SEO
const OrderWidget = dynamic(() => import('@/components/order-widget'), {
  ssr: false, loading: () => <OrderWidgetSkeleton />,
});
```

**Ngân sách bundle (thực thi trong CI):**
```json
// .lighthouserc.json
{
  "ci": {
    "assert": {
      "assertions": {
        "categories:performance": ["error", { "minScore": 0.9 }],
        "categories:seo": ["error", { "minScore": 1.0 }],
        "categories:accessibility": ["error", { "minScore": 0.95 }],
        "largest-contentful-paint": ["error", { "maxNumericValue": 1800 }],
        "cumulative-layout-shift": ["error", { "maxNumericValue": 0.05 }],
        "total-byte-weight": ["error", { "maxNumericValue": 500000 }]
      }
    }
  }
}
```

> **Đo bằng dữ liệu thật, không chỉ lab:** Gắn `web-vitals` gửi về PostHog. Google xếp hạng theo CrUX (dữ liệu người dùng thật), không theo điểm Lighthouse.

---

### A8. Đa ngôn ngữ (vi / en)

```
qlycoffee.vn/...        → tiếng Việt (mặc định, x-default)
qlycoffee.vn/en/...     → tiếng Anh
```

```html
<link rel="alternate" hreflang="vi-VN" href="https://qlycoffee.vn/bang-gia" />
<link rel="alternate" hreflang="en"    href="https://qlycoffee.vn/en/pricing" />
<link rel="alternate" hreflang="x-default" href="https://qlycoffee.vn/bang-gia" />
```

**Quy tắc:** hreflang phải **đối xứng hai chiều** (trang EN cũng phải trỏ ngược về VI). Chỉ tạo bản EN cho trang thực sự có nội dung dịch — không auto-translate rồi index (nội dung kém chất lượng).

---

## PHẦN B — LOCAL SEO (mặt trận 2)

### B1. Tích hợp Google Business Profile

**Đây là tính năng sản phẩm, không chỉ là marketing.** Chủ quán kết nối GMB một lần, hệ thống tự đồng bộ:

| Đồng bộ | Hướng | Tần suất |
|---|---|---|
| Giờ mở cửa (kể cả giờ đặc biệt ngày lễ) | Qly → GMB | Khi thay đổi |
| Menu & giá | Qly → GMB | Hằng ngày |
| Ảnh món mới | Qly → GMB | Khi upload |
| Bài đăng (khuyến mãi) | Qly → GMB | Khi tạo |
| Đánh giá khách | GMB → Qly | Mỗi giờ |
| Câu hỏi từ khách | GMB → Qly (+ gợi ý trả lời bằng AI) | Mỗi giờ |

**Giá trị:** Chủ quán VN gần như không ai cập nhật GMB. Tự động hóa việc này là tính năng bán được tiền, đồng thời tăng lượng truy cập vào trang quán trên domain của ta.

### B2. NAP Consistency

Name–Address–Phone phải **giống hệt nhau** ở mọi nơi: website ta, GMB, Facebook, Foody, GrabFood. Sai lệch làm giảm local ranking.

Xây tính năng **NAP Audit**: quét các nền tảng, báo cáo chỗ không khớp, gợi ý sửa.

### B3. Trang tổng hợp theo địa phương

```
/ca-phe/ho-chi-minh                    → "Quán cà phê TP.HCM"
/ca-phe/ho-chi-minh/quan-3             → "Quán cà phê Quận 3"
/ca-phe/ho-chi-minh/quan-3/co-wifi     → lọc theo tiện ích
```

**Điều kiện chống thin content:** Chỉ tạo trang khi có **≥5 quán** trong khu vực đó. Mỗi trang phải có nội dung riêng: mô tả khu vực, danh sách quán kèm ETA trung bình, giờ đông khách, bản đồ.

---

## PHẦN C — CHIẾN LƯỢC NỘI DUNG

### C1. Nghiên cứu từ khóa — thị trường Việt Nam

| Cụm chủ đề | Từ khóa chính | Volume ước tính/tháng | Ý định |
|---|---|---:|---|
| Phần mềm quản lý | phần mềm quản lý quán cà phê | 2.400 | Thương mại |
| | phần mềm bán hàng quán cafe | 1.900 | Thương mại |
| | máy tính tiền quán cà phê | 1.600 | Thương mại |
| So sánh | kiotviet vs sapo | 480 | Thương mại (⭐ CTR cao) |
| Vận hành | cách quản lý quán cà phê hiệu quả | 1.300 | Thông tin |
| | mở quán cà phê cần bao nhiêu vốn | 3.600 | Thông tin (top funnel) |
| | định mức nguyên liệu pha chế | 720 | Thông tin |
| Nghiệp vụ | công thức pha chế cà phê | 8.100 | Thông tin (volume lớn) |
| Pháp lý | hóa đơn điện tử quán ăn | 590 | Thông tin |
| Từ khóa của ta ⭐ | giảm thời gian chờ quán cà phê | thấp | **Ta tự tạo nhu cầu** |

**Chiến lược:** Chiếm lĩnh nhóm "vận hành/nghiệp vụ" trước (cạnh tranh thấp, ý định rõ), rồi mới đánh nhóm "phần mềm" (cạnh tranh cao, đối thủ chi tiền quảng cáo lớn).

### C2. Kiến trúc Topic Cluster

```
                 ┌──────────────────────────────────┐
                 │  PILLAR PAGE (3.000+ từ)         │
                 │  "Quản lý quán cà phê từ A đến Z"│
                 └──────────────┬───────────────────┘
        ┌────────────┬──────────┼──────────┬────────────┐
        ▼            ▼          ▼          ▼            ▼
  Quản lý kho   Quản lý     Định giá   Tối ưu       Marketing
                nhân sự      menu      vận hành      cho quán
        │            │          │          │            │
    ┌───┴───┐    ┌──┴──┐   ┌───┴──┐   ┌──┴───┐    ┌───┴───┐
   3-5 bài  3-5 bài   3-5 bài   3-5 bài    3-5 bài
   chuyên sâu, link nội bộ 2 chiều với pillar
```

**Nguyên tắc link nội bộ:**
- Mỗi bài cluster link về pillar bằng anchor text mô tả
- Pillar link ra tất cả cluster
- Cluster link chéo nhau khi liên quan
- Mọi bài đều có CTA link tới trang tính năng/bảng giá (link "tiền" phải có)

### C3. Nội dung độc quyền từ dữ liệu — không ai copy được

Đây là con át chủ bài. Ta có dữ liệu vận hành thật của hàng nghìn quán → xuất bản **báo cáo ngành**:

| Nội dung | Tần suất | Giá trị SEO |
|---|---|---|
| **Báo cáo Ngành cà phê Việt Nam** (thời gian chờ trung bình, giờ cao điểm, món bán chạy theo mùa) | Hằng quý | Thu hút backlink từ báo chí ⭐⭐⭐ |
| Bảng xếp hạng "Quán phục vụ nhanh nhất TP.HCM" | Hằng tháng | Viral, backlink địa phương |
| Chỉ số giá cà phê VN | Hằng tháng | Được trích dẫn |
| Nghiên cứu: "Khách bỏ đơn sau bao nhiêu phút chờ?" | 1 lần, cập nhật | Được trích dẫn học thuật |

**Yêu cầu:** Dữ liệu tổng hợp, ẩn danh hoàn toàn, có ghi rõ phương pháp luận. Đây vừa là SEO vừa là PR.

### C4. Nội dung do người dùng tạo (mặt trận 2)

Mỗi trang quán tự sinh nội dung có giá trị:
- Menu đầy đủ với ảnh và mô tả (do chủ quán nhập)
- **Thời gian chờ trung bình theo giờ** ⭐ (dữ liệu chỉ ta có)
- "Giờ đông khách nhất: 8:00–9:30" (biểu đồ)
- Đánh giá thật từ khách đã đặt món
- Món bán chạy nhất

> Trang "Menu quán X + biết trước phải chờ bao lâu" là loại nội dung **không đối thủ nào có thể tạo ra** — vì họ không đo thời gian pha chế.

### C5. E-E-A-T (Experience, Expertise, Authoritativeness, Trust)

| Yếu tố | Thực thi |
|---|---|
| Experience | Case study thật có số liệu, có tên quán, có ảnh, có trích dẫn chủ quán |
| Expertise | Trang tác giả đầy đủ (`Person` schema, LinkedIn, tiểu sử chuyên môn) |
| Authoritativeness | Backlink từ báo ngành, hiệp hội cà phê, được trích dẫn trong báo cáo |
| Trust | Địa chỉ công ty, MST, số điện thoại thật, chính sách bảo mật, trang trạng thái hệ thống (status page), HTTPS, đánh giá thật |

---

## PHẦN D — ĐO LƯỜNG & CHỐNG SAI LẦM

### D1. Bộ chỉ số theo dõi

| Chỉ số | Công cụ | Mục tiêu tháng 12 |
|---|---|---|
| Organic sessions | GA4 / PostHog | 25.000/tháng |
| Từ khóa top 10 | Ahrefs / GSC | 150 |
| Trang được index | GSC Coverage | > 90% URL trong sitemap |
| CTR trung bình | GSC | > 4% |
| Core Web Vitals pass | CrUX / GSC | > 90% URL |
| Referring domains | Ahrefs | 200 |
| Organic → Trial | GA4 | 3.5% |
| Trial → Paid | Nội bộ | 25% |

### D2. Giám sát tự động (SEO Guardrails trong CI)

```yaml
# .github/workflows/seo-check.yml
- name: SEO regression tests
  run: |
    pnpm test:seo:metadata     # mọi trang có title/description/canonical duy nhất
    pnpm test:seo:schema       # JSON-LD hợp lệ theo schema.org
    pnpm test:seo:links        # không có internal link 404
    pnpm test:seo:sitemap      # mọi URL trong sitemap trả 200
    pnpm lhci autorun          # Lighthouse budget
```

**Cảnh báo hằng tuần (job nền):**
- URL bị rớt khỏi index > 5%
- Trang có title/description trùng lặp
- Trang mồ côi (không có internal link trỏ tới)
- Chuỗi redirect > 1 bước
- Trang có < 300 từ nhưng đang được index

### D3. ⚠️ Bảy sai lầm chết người phải tránh

| # | Sai lầm | Hậu quả | Phòng ngừa |
|---|---|---|---|
| 1 | **Index bloat** — index 60.000 trang quán mỏng | Google giảm crawl budget toàn site, hạ chất lượng domain | `noindex` quán có < 5 món hoặc chưa hoạt động 30 ngày |
| 2 | **Duplicate content** — mô tả món giống nhau giữa các quán | Cannibalization | Chỉ index trang món khi mô tả là duy nhất (≥ 80 từ riêng) |
| 3 | **Bịa `aggregateRating`** | Phạt thủ công toàn site | Chỉ xuất khi có đánh giá thật, có nguồn |
| 4 | **Đổi slug không redirect** | Mất toàn bộ ranking đã có | Bảng `url_redirects`, tự động 301 |
| 5 | **Trang đặt món bị index** | Nội dung trùng lặp, trải nghiệm tìm kiếm tệ | `noindex` mọi trang có state |
| 6 | **Tham số URL tạo vô hạn URL** | Crawl budget bị đốt | Canonical về URL sạch, `Disallow: /*?` |
| 7 | **Bỏ mobile** | 80% traffic VN là mobile | Mobile-first design, test trên 3G |

### D4. Kế hoạch chống rủi ro Google Update

- Không bao giờ dựa vào một chiến thuật đơn lẻ
- Đa dạng nguồn traffic: SEO 40%, direct 25%, referral 15%, paid 20%
- Xây email list và cộng đồng Zalo/Facebook (kênh sở hữu)
- Theo dõi bản cập nhật thuật toán, ghi log thay đổi để đối chiếu khi traffic biến động

---

## PHẦN E — LỘ TRÌNH SEO

| Tháng | Việc làm | Kết quả kỳ vọng |
|---|---|---|
| 1–2 | Nền móng kỹ thuật: Next.js SEO setup, schema, sitemap, CWV, GSC/GA4 | Lighthouse SEO 100, 20 trang được index |
| 3–4 | 20 bài blog cụm "vận hành", 4 pillar page, trang so sánh | 50 từ khóa top 100 |
| 5–6 | Bật mặt trận 2: trang quán công khai, local pages, GMB sync | 500+ trang index, traffic local đầu tiên |
| 7–9 | Báo cáo ngành lần 1, PR, link building, case study | 50 referring domains, 30 từ khóa top 10 |
| 10–12 | Mở rộng nội dung, tối ưu conversion, bản EN | 25.000 organic sessions/tháng |

---

**Tiếp theo:** [06 — Roadmap & Vận hành](06-roadmap-van-hanh.md)
