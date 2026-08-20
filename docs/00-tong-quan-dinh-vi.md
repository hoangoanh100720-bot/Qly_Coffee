# 00 — Tổng quan & Định vị sản phẩm

## 1. Bối cảnh thị trường Việt Nam

### 1.1 Các đối thủ hiện có

| Sản phẩm | Điểm mạnh | Điểm yếu (khoảng trống của ta) |
|---|---|---|
| **KiotViet** | Phổ biến nhất, hệ sinh thái rộng, giá rẻ | Là POS bán lẻ đa ngành → không hiểu quy trình pha chế. Không có khái niệm "trạm", "hàng đợi", "thời gian chờ". AI = 0. |
| **iPOS.vn** | Chuyên F&B, tích hợp sâu delivery | Báo cáo tĩnh, không dự báo. UI nặng, desktop-first. Không có AI thực. |
| **Sapo FnB** | Omnichannel, tích hợp sàn | Tương tự — thiên bán hàng, yếu vận hành bếp/bar. |
| **CukCuk (MISA)** | Kế toán/hóa đơn điện tử mạnh | Nặng nghiệp vụ kế toán, nhẹ trải nghiệm khách. |
| **PosApp / Ocha** | Rẻ, dễ dùng cho quán nhỏ | Chức năng cơ bản, không có analytics sâu. |
| **Square / Toast (nước ngoài)** | Sản phẩm chín, có wait-time ước lượng | Không có mặt tại VN, không hỗ trợ hóa đơn điện tử VN, không tích hợp Momo/ZaloPay/GrabFood. |

### 1.2 Ba khoảng trống lớn

**(1) Không ai giải bài toán "thời gian chờ".**
Tất cả đối thủ coi đơn hàng là một bản ghi kế toán. Không ai mô hình hóa **quán cà phê như một hệ thống hàng đợi có tài nguyên hữu hạn** (máy espresso, máy xay đá, số barista). Khách hàng chờ mù mờ, chủ quán không biết giờ nào nghẽn ở đâu.

**(2) Không ai biến dữ liệu vận hành thành quyết định.**
Đối thủ dừng ở "báo cáo doanh thu theo ngày". Không ai trả lời được:
- *"Thêm 1 barista ca sáng thứ 7 thì thời gian chờ giảm bao nhiêu, doanh thu tăng bao nhiêu?"*
- *"Món nào đang giết throughput của tôi vào giờ cao điểm?"*
- *"Tuần sau tôi cần nhập bao nhiêu kg cà phê?"*

**(3) Không ai có flywheel dữ liệu.**
Mỗi phần mềm là một silo. Không ai tận dụng dữ liệu tổng hợp từ hàng nghìn quán để làm mô hình tốt hơn cho quán mới. Đây là **lợi thế cạnh tranh không thể copy** nếu ta đi trước.

---

## 2. Định vị sản phẩm

> **Qly Coffee là "hệ điều hành vận hành" cho quán cà phê, không phải máy tính tiền.**

Ba trụ cột:

```
┌──────────────────────────────────────────────────────────────┐
│  TRỤ 1 — PREP-TIME INTELLIGENCE  (lõi khác biệt)             │
│  Dự đoán · Cam kết · Tối ưu thời gian hoàn thành món         │
│  → Khách biết chờ bao lâu. Quán biết cách chờ ít hơn.        │
├──────────────────────────────────────────────────────────────┤
│  TRỤ 2 — OPERATIONS BRAIN                                    │
│  Dự báo nhu cầu · Tự động đặt hàng · Xếp ca · Chống thất thoát│
├──────────────────────────────────────────────────────────────┤
│  TRỤ 3 — PLATFORM  (hạ tầng bắt buộc phải có)                │
│  POS offline-first · KDS · Menu QR · Loyalty · HĐĐT · Sàn    │
└──────────────────────────────────────────────────────────────┘
```

**Nguyên tắc thiết kế:** Trụ 3 phải *ngang bằng hoặc tốt hơn* đối thủ (nếu không, không ai đổi phần mềm). Trụ 1 và 2 là lý do họ đổi.

### 2.1 Thông điệp bán hàng theo đối tượng

| Đối tượng | Nỗi đau | Thông điệp |
|---|---|---|
| Chủ quán đơn lẻ | "Giờ cao điểm khách bỏ đi vì chờ lâu" | "Giảm 20% thời gian chờ giờ cao điểm mà không cần thuê thêm người" |
| Chuỗi 5–50 chi nhánh | "Không biết chi nhánh nào yếu ở đâu" | "So sánh hiệu suất pha chế giữa các chi nhánh theo giây, không theo doanh thu" |
| Nhà đầu tư/quản lý | "Quyết định mở rộng dựa trên cảm tính" | "Mô phỏng ROI trước khi mua máy hay thuê người" |
| Khách hàng cuối | "Không biết chờ bao lâu" | "Đặt trước, đến là có, cà phê còn nóng" |

---

## 3. Mô hình kinh doanh (SaaS multi-tenant)

### 3.1 Bảng giá đề xuất

| Gói | Giá/tháng/chi nhánh | Đối tượng | Bao gồm |
|---|---|---|---|
| **Starter** | Miễn phí (giới hạn 300 đơn/tháng) | Quán mới, thử nghiệm | POS, KDS, menu QR, báo cáo cơ bản |
| **Pro** | 399.000đ | Quán đơn lẻ | + ETA dự đoán, loyalty, tồn kho, HĐĐT, tích hợp sàn |
| **Intelligence** | 899.000đ | Quán quy mô, chuỗi nhỏ | + Smart sequencing, dự báo nhu cầu, xếp ca AI, digital twin |
| **Chain** | Báo giá (từ 5tr) | Chuỗi ≥10 chi nhánh | + Quản lý tập trung, API, SSO, SLA, CSM riêng |

**Add-on:** AI Voice/Chat ordering (+199k), Computer Vision (+499k), Kênh Zalo OA riêng.

### 3.2 Chiến lược Free tier — có chủ đích

Gói Free **không phải chiêu marketing**, nó là **máy thu thập dữ liệu**. Mỗi quán Free đóng góp dữ liệu (đã ẩn danh) làm mô hình global tốt hơn → quán trả tiền được lợi. Ghi rõ điều khoản này trong ToS.

### 3.3 Đơn vị kinh tế mục tiêu (Year 2)

| Chỉ số | Mục tiêu |
|---|---|
| ARPU | 550.000đ/tháng/chi nhánh |
| CAC | < 1.500.000đ |
| Payback | < 4 tháng |
| Gross margin | > 75% |
| Net revenue retention | > 110% |
| Churn tháng | < 3% |

---

## 4. Chỉ số thành công của sản phẩm (không phải chỉ số doanh thu)

Đây là các chỉ số quyết định sản phẩm có *thực sự* khác biệt hay không:

### 4.1 Chỉ số AI (bắt buộc đo, công khai trong dashboard chủ quán)

| Chỉ số | Định nghĩa | Mục tiêu |
|---|---|---|
| **On-time rate** | % đơn hoàn thành ≤ ETA đã hứa với khách | ≥ 85% |
| **MAE** | Sai số tuyệt đối trung bình của ETA | < 90 giây |
| **P90 error** | Sai số ở phân vị 90 | < 3 phút |
| **ETA stability** | % đơn có ETA tăng > 2 phút sau khi đã hứa | < 10% |
| **Label coverage** | % đơn có đủ mốc thời gian để train | ≥ 90% |

### 4.2 Chỉ số tác động vận hành (chứng minh giá trị)

| Chỉ số | Cách đo | Mục tiêu sau 3 tháng dùng |
|---|---|---|
| Thời gian chờ trung bình giờ cao điểm | So với baseline 2 tuần đầu | Giảm ≥ 15% |
| Throughput giờ cao điểm (đơn/giờ) | Đếm | Tăng ≥ 10% |
| Tỷ lệ hủy/bỏ đơn | Đếm | Giảm ≥ 30% |
| Hao hụt nguyên liệu | Lý thuyết vs thực tế | Giảm ≥ 20% |

> ⚠️ **Quy tắc vàng:** Nếu không đo được thì không được quảng cáo. Dashboard "Tác động" hiển thị các con số này cho chính chủ quán là công cụ bán hàng mạnh nhất — và là rào cản churn.

---

## 5. Giả định & rủi ro cốt lõi

| Giả định | Rủi ro nếu sai | Cách kiểm chứng sớm |
|---|---|---|
| Barista sẽ bấm nút trên KDS đúng lúc | Không có nhãn → không train được model | Pilot 2 quán ở Phase 2, đo label coverage trước khi build ML |
| Khách quan tâm đến ETA | Tính năng lõi không tạo giá trị | Khảo sát + A/B hiển thị ETA vs không hiển thị, đo tỷ lệ bỏ đơn |
| Chủ quán chịu đổi phần mềm | CAC cao, tăng trưởng chậm | Xây công cụ import dữ liệu từ KiotViet/iPOS ngay Phase 1 |
| Quán VN có mạng đủ ổn định | Trải nghiệm tệ | Offline-first ngay từ đầu — không thương lượng |

---

**Tiếp theo:** [01 — Kiến trúc hệ thống](01-kien-truc-he-thong.md)
