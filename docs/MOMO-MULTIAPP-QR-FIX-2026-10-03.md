# MoMo multi-app QR regression — 2026-10-03

## Root cause
The actual supplied MoMo image decodes successfully, including after JPEG normalization. Its NAPAS merchant template has AID A000000727, route 971025, service QRIBFTTA, and an opaque 19-character beneficiary identifier. The previous parser classified it as BANK. The frontend bank list did not recognize that route and collapsed the error into a generic unreadable-image dialog.

## Minimal fix
Recognize the observed route/AID/service profile as MOMO. Do not expose its opaque identifier as a phone or truncate it. Return no phone when unavailable. Keep user-entered phone/name for explicit confirmation and Admin review. This mapping is grounded in the supplied sample, not a claim of support for every MoMo format.

MUA upload continues into private financial storage, with owner authorization, OTP-bound attachment, and payout snapshots. Customer refund QR stays decode-only. Public avatar/portfolio/service/review purposes still reject the recognized financial QR. No schema or migration changes.

Both receive-account forms now place QR selection before bank/account/holder fields. Unsupported routing and private-storage-unavailable errors have distinct messages. Original financial media and payload are not copied into this report or regression fixtures; tests use synthetic values.

## Release
Local modifications only. Release backend parser before the new app. Confirm SUPABASE_FINANCIAL_BUCKET=financial-private on Render and that the bucket is private; this audit does not change production settings. Use the existing financial migration deployment process, with no new migration for this regression. Re-test the supplied QR in a new app build and verify the corresponding Admin payout preview. Manual phone/name must match the recipient shown by the payment app; decoding does not prove ownership.

Privacy/deletion pages already describe supported fields, private original MoMo QR, conditional manual entry, authorized Admin access, and ownership-based deletion; no policy change required here.

Test results are reported in the accompanying task response. Production execution was not performed.
