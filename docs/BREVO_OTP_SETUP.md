# Brevo OTP

Backend uses HTTPS `POST https://api.brevo.com/v3/smtp/email` with the Brevo API key (not SMTP key). No new NuGet package or database migration is needed.

Render Environment:

| Key | Value |
| --- | --- |
| `Email__BrevoApiKey` | Private Brevo API key |
| `Email__FromEmail` | `bbooksupport@gmail.com` (verified sender) |
| `Email__FromName` | `BBook` |

Deploy the commit containing BrevoEmailSender. The old SMTP variables are no longer used. Local development uses User Secrets with keys `Email:BrevoApiKey`, `Email:FromEmail`, `Email:FromName`. Never commit API keys.

Test registration OTP with an unregistered recipient. HTTP 200 means Brevo accepted the request, not guaranteed inbox delivery: check Brevo Transactional Logs and recipient spam folder. A custom authenticated sending domain is recommended for production.

Email failures return HTTP 503 `EMAIL_UNAVAILABLE`. Backend logs contain HTTP status and Brevo error code, without API keys, OTP values or recipient addresses. Cooldown returns 429 `OTP_COOLDOWN`. Failed-send OTP records are removed; previous codes stay invalidated. If a timeout occurs after Brevo accepted an email, the emailed code is invalidated because the API reported failure; request a new one.
