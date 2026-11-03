# Native app payOS commerce

Customer AI: 20,000 VND once, lifetime. MUA visibility: 75,000 VND per 30 days,
stacking after the existing period, plus one Facebook task per cycle. MUA registration remains free.

## Compatibility and rollout

This release adds endpoints and database columns. Existing app auth, refresh, booking,
booking payments and consumption-only `/api/me/plans` responses are preserved.
No rollout flag or enforcement setting is enabled by this commit. No account allowlist is used.
Existing website authentication and browser CORS are unchanged by this release.

Apply migration `20261103083108_NativeAppCommerce` before serving the new binary.
Production startup already requires migrations; coordinate normal deployment downtime.
The migration preserves existing orders, adds nullable payment details and permits NativeApp
as an order origin. Do not roll this migration down after native orders exist.
Rollback application code with the expanded schema retained.

Native-only enablement, after deployment and operator verification:

```dotenv
AppCommerce__Enabled=true
CustomerAi__AppCheckoutEnabled=true
CustomerAi__ReconciliationEnabled=true
MuaMonetization__Enabled=true
MuaMonetization__AppCheckoutEnabled=true
MuaMonetization__PaymentLinksEnabled=true
MuaMonetization__WorkersEnabled=true
MuaMonetization__LivePaymentsEnabled=true
MuaMonetization__SponsoredDeliveryEnabled=true
CustomerAi__EnforcementEnabled=false
```

Do not alter existing web checkout flags or booking's `PayOS__*` credentials.
Dedicated `CommercePayOS__ClientId`, `ApiKey`, `ChecksumKey`, `Channel`, `ReturnUrl`,
`CancelUrl` and `MuaWeb__Origin` must remain configured. The provider still requires
HTTPS return/cancel URLs to create a link; native QR payment does not navigate to them.
Use the existing commerce webhook `/api/mua/commerce/payos/webhook`.

Set only `AppCommerce__Enabled=false` to stop new native checkout. Keep reconciliation,
workers and credentials enabled for outstanding paid orders. Do not turn every commerce
flag off: the legacy phased rollout still gates some receipt reads by those flags.

## Contract

All endpoints use the existing app JWT (not the portal session), active real Customer/MUA
identity, server-side ownership and role checks. Writes are rate limited per account.

- `GET /api/me/commerce/catalog`: role, terms, server prices and readiness.
- `POST /api/me/commerce/orders`: productId, termsVersion, acceptedTerms,
  portfolioConsent; `Idempotency-Key` required. Returns the reserved order ID.
- `GET /api/me/commerce/orders/{id}`: owner-only read; no provider network call.
- `POST /api/me/commerce/orders/{id}/sync`: owner-only provider reconciliation.

QR/account instructions are captured only from verified payOS create responses. A recovered
legacy link may lack QR details: the app does not invent bank instructions or redirect to web.
Wait for expiry/reconciliation, then retry with a new request key. MUA link creation and
fulfillment use the existing worker. No URL parameter or client status grants access.

## Verification before release

Automated contract/regression checks are not evidence of a real payment. Operator device
acceptance must include Customer 20k, MUA 75k and another 75k renewal; verify provider status,
ledger, entitlement, sponsored delivery, Facebook tasks and account sync on another device.
Also check the currently released app's login/refresh and booking payment flow.
No live transfer, app-store upload or device acceptance is claimed by this commit.
Build AAB manually from the merged frontend commit after device testing.
