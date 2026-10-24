# Advisor protected baseline — đề xuất chờ duyệt, chưa áp dụng

R1 không sửa ScanPhoto.tsx, hai protected assertions hoặc active checksum manifest. Các thay đổi Advisor có trước được giữ nguyên.

## Nguyên nhân đã xác minh bằng bytes

Git history local có commit liên quan `551d055` ngày 15/10/2026. Manifest và ScanPhoto cùng có trong commit này. Không suy tác giả/thời điểm của working-tree edits chưa commit.

| Bản | SHA256 |
|---|---|
| Manifest yêu cầu | `a1118308d8a5c9537c1f82cda490ba294b81963de263a1b25d555332269f32e0` |
| Git HEAD blob, LF | `8a2777404bf20ae489e0bf118c60c6150b77c4ff6397efbd94e002b25dc7f82d` |
| Cùng nội dung Git HEAD, CRLF | `a1118308d8a5c9537c1f82cda490ba294b81963de263a1b25d555332269f32e0` |
| Working file hiện tại, CRLF | `3fd11a0399ace8c675aef6e33c398b9a1a95e793dc44984b335c1fdc499fd461` |
| Nội dung working file chuẩn hóa LF | `3ed310c2c6373a5635b24889216a70393d6acc098c0ebaa5bf102fc130c9689e` |

Đây là bằng chứng cụ thể hơn nhận xét cũ “HEAD cũng khác manifest”: raw Git blob khác vì LF, nhưng **HEAD chuyển CRLF khớp manifest chính xác**. Working file đã dùng CRLF đúng kiểu cũ; phần làm nó lệch baseline là hai thuộc tính StyleSheet đã sửa trước R1:

- frame.borderRadius: 24 → 0.
- frame.backgroundColor: #FFD6E5 → #F5F2F3.

Phần source trước `const styles=StyleSheet.create` giống HEAD hoàn toàn sau chuẩn hóa newline. Trong 38 protected files, chỉ ScanPhoto lệch raw checksum hiện tại; nội dung canonical của 37 file còn lại giống HEAD. Không có thay đổi R1 vào các file đó.

## Hành vi cần bảo vệ và bằng chứng

Giữ: single-face validation, contour/468-point mesh bounds/index validation, loại duplicate edges/invalid triangles, không bịa landmarks, Image contain/aspect ratio, overlay tọa độ đúng, processing/success/error visibility, reduced-motion, sweep/success timing và animation cleanup; native module/normalizer/scan safety, capability/release gates, runtime gateway, scoring/Top 3/v1.2 không đổi.

Full Jest R1: **842 passed / 2 failed / 844 total**. Hai failure đúng raw checksum assertions. `ScanPhoto.test.tsx`, `scanPresentation.test.ts`, `nativeConfig.test.ts`, `buildFlag.test.ts`, các engine/ranking/runtime tests khác đều qua trong full run. Đây là component/logic/static-contract evidence; chưa đo animation/native memory trên Android thật và không thay thế QA thiết bị.

## Phương án cần chủ dự án duyệt

**Khuyến nghị phương án B cho baseline dùng trên Windows/Linux**, nhưng R1 dừng ở tài liệu, chưa code:

### A. Chỉ duyệt visual baseline trong checkout hiện tại

Một entry ScanPhoto của `docs/advisor/INTEGRATION-PROTECTED-HASHES.json` được đề xuất đổi sang current raw hash `3fd11a...`. Không sửa ScanPhoto hay assertion. Ưu điểm: hẹp nhất. Hạn chế: hash raw vẫn phụ thuộc LF/CRLF; chưa đủ cho release CI/fresh checkout khác hệ điều hành. Không tự chọn A chỉ để có local tests xanh.

### B. Reviewed visual baseline + policy checksum văn bản ổn định

Các file **dự kiến** thay sau khi được duyệt:

1. Mobile `docs/advisor/INTEGRATION-PROTECTED-HASHES.json`: review lại cùng 38 text files; SHA256 tính trên UTF-8 với CRLF → LF, giữ mọi byte nội dung khác, không trim/reformat. ScanPhoto canonical hash đề xuất `3ed310c2...`. Có **19 entry đổi** do 19 file CRLF, không phải 19 file source thay đổi; 18 entry trong số đó chỉ khác newline, một entry ScanPhoto đồng thời nhận hai style changes được review.
2. `features/makeupAdvisor/__tests__/engine20Simulation.test.ts`: chỉ canonicalize CRLF→LF trước hash của protected text; giữ toàn bộ identity/ranking/schema assertions.
3. `features/makeupAdvisor/__tests__/consolidationBoundary.test.ts`: cùng policy canonicalization; giữ boundary/runtime/scoring assertions.

Không đổi danh sách protected files, không bỏ hash check, không sửa source protected hoặc tăng whitelist. File binary nếu có trong tương lai vẫn phải raw hash, không decode tùy tiện. Danh sách hiện tại là .ts/.tsx/.kt/.json/.xml/.gradle, đều text. Cần kiểm thử LF/CRLF fixtures, chứng minh sửa một token vẫn làm checksum fail, full Jest và QA Android trước nghiệm thu. Không tự cập nhật manifest khác ở các phase cũ.

Chi tiết từng raw/canonical hash và so sánh HEAD: `D:/EXE/artifacts/phase-r1-20261018/advisor-hashes.json`, `advisor-normalization-review.json`.

**Chờ duyệt riêng A hoặc B. Không có thay đổi protected baseline trong R1.**
