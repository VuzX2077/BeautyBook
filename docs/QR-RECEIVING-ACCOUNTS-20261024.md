# QR receiving accounts and printed owner names

The MUA and customer forms share one scan flow. Bank QR images fill the bank, account number and owner. MoMo images select MoMo, upload to the existing private financial bucket and use that QR as the receiving destination. The number input is hidden while a private MoMo QR is attached. Removing the QR restores manual entry and requires a valid MoMo phone number.

The backend accepts an empty account number only for MoMo with a ready, owned private QR. It verifies the stored image checksum and recipient before issuing the OTP and before saving. The account number remains empty; an internal receiver hash is used only for duplicate detection and OTP binding. The opaque NAPAS MoMo recipient ID is never converted to a phone number. Existing manual bank/MoMo validation, email OTP and admin approval remain required.

When a QR lacks the owner name, `AccountNameReader` runs Tesseract locally using English/Vietnamese models. It reads confident text near the printed account or masked number, rejects bank/payment branding and ambiguous candidates, and marks the result as `IMAGE`. The name stays editable. Blurry/cropped images or unavailable OCR fall back to manual name entry; neither a name read from QR nor OCR proves ownership. Images travel through process pipes without temporary OCR files or external OCR services. Concurrency, image size and runtime are bounded.

Refunds and MUA payouts retain their original private QR snapshot. QR-only MoMo refunds remain manual and never enter the bank payout provider. Admin can review the image and complete the transfer through the existing reconciliation flow. Replacing/removing an account QR does not replace a QR already captured for an active transaction.

## Deployment

Deploy the backend first, rebuilding the repository Dockerfile so `tesseract-ocr`, `tesseract-ocr-eng` and `tesseract-ocr-vie` are present. Non-Docker hosts need the equivalent Tesseract executable and language packs on PATH. No schema migration, new storage bucket or external OCR key is needed. Then build the mobile AAB from the corresponding app changes. A previous backend still requires a MoMo phone number and cannot support the new QR-only form.

## Verification

Coverage includes QR parsing, source precedence for owner names, ambiguous/low-confidence OCR, HTTP model validation, owned media checks, OTP binding, add/edit/remove for both roles, duplicate receivers, private-image authorization, old QR snapshots, refund processing and MUA payout completion/idempotency. Integration tests use a dedicated localhost PostgreSQL cluster and synthetic accounts. No real transfer is performed.

The two user-provided reference images were decoded and tested locally: both QR payloads lack owner names, and local OCR plus the backend name extractor matched the owner printed below each QR. Personal images and payloads are excluded from the repository.

Final checks: 96 distinct backend cases passed (no skips), 38 distinct mobile cases passed, mobile TypeScript passed and lint on changed production files passed. The HTTP OTP case was re-run with a strict one-time/context-bound fake after correcting the test double; the final report combines the latest result for each distinct case.

References: [Tesseract CLI/TSV](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html), [MoMo multi-app receiving QR](https://www.momo.vn/qr-da-nang).
