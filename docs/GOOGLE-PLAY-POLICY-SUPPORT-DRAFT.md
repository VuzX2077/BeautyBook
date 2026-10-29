# Draft — do not send without the owner's authorization

Subject: Request for Payments policy guidance for BBook (com.bbook.app), independent website purchases and existing physical-service bookings in Vietnam

Hello Google Play Policy Support,

We operate BBook, an Android application distributed through Google Play under package `com.bbook.app`, connecting customers with makeup artists (MUAs) for in-person makeup services in Vietnam. Our website is https://bbookmakeup.com. We request guidance before introducing new paid MUA features. No new paid feature is currently enabled by this implementation.

The existing Android app allows customers to book in-person makeup services and pay a deposit through PayOS. Our current local source calculates a 30% customer deposit and an 8% platform fee; we will provide the verified production behavior and screenshots with this request. These are payments for actual in-person services, distinct from the proposed MUA fees below.

Proposed MUA products:

1. Activation: VND 100,000, paid once after profile approval. It unlocks long-term public listing and the ability to receive new bookings. Registration, profile preparation and submission are free. Existing grandfathered MUAs would not pay. The first eligible publication after activation includes a one-time 30-day in-app visibility promotion.
2. BBook Visibility: VND 75,000 per purchase, providing 30 days of increased in-app exposure. Repeat purchases extend the time; there is no automatic renewal. Expiry does not remove ordinary booking rights, profile data or payout rights. We do not guarantee customers or revenue. Any additional social promotion would be disclosed separately and would not remove the in-app promotion component.

We are considering selling these products only on our independently accessed website, using authenticated BBook accounts and PayOS hosted checkout. Our server would verify payments and synchronize entitlements. The Android app would show review status, activation status and promotion expiry and use entitlements already acquired outside the app. It would not offer digital checkout, QR codes, WebViews, purchase buttons, purchase links, payment deep links or payment-steering notifications. We do not intend to conceal functionality from reviewers.

Please clarify:

1. Are activation and Visibility classified as in-app digital features/services under Payments policy section 2? Does activation's relationship to receiving bookings for in-person makeup, or the bundled promotion, change the classification?
2. May the same Play-distributed app retain its existing in-person-service booking/deposit payment flow under section 3(a), while using externally purchased digital MUA entitlements without offering their purchase in Android?
3. The consumption-only FAQ states that no products or services, digital or physical, can be purchased within the app. Does this prevent the mixed model in question 2? If it does, would a separate MUA app with no purchases of any kind be an appropriate consumption-only design, with the customer booking app remaining separate?
4. If MUAs register, prepare profiles and submit for approval within Android, can an approved profile remain inactive until an independently initiated website purchase? What fee disclosure and neutral status wording are permissible without creating a prohibited account-creation/sign-up payment flow under section 4?
5. Would an automatically generated email containing a PayOS purchase link immediately after approval be considered part of that in-app sign-up/payment flow? We will keep this trigger disabled unless its permissibility is confirmed. Moving the checkout to Gmail would not change the originating workflow.
6. Separately, may we send independently scheduled, opt-in marketing emails outside the app linking to our website offers, with no in-app purchase CTA, email-trigger CTA or mandatory email payment step? What separation from app onboarding is required?
7. Which non-linked website/purchase information, if any, may appear in the proposed mixed app versus a true consumption-only MUA app? Are administrative/help links permissible if their reachable navigation includes a commerce area, or must these destinations be isolated?
8. Are there any alternative billing or external link/offer programs applicable to Vietnamese users that could authorize this implementation? We did not find Vietnam in the published eligibility lists reviewed on 28 October 2026. Please identify any current applicable program and enrollment/API/fee requirements.

If the proposed model is not permitted, please identify which workflow must change. We can keep digital monetization disabled, redesign the product or consider Google Play Billing. We are not seeking a way to bypass the policy.

We can provide a screen-by-screen flow, reviewer access, website routes, the verified release versionCode, approval/email triggers and product terms. Please assess the complete design including the existing physical-service deposit flow.

Thank you,
BBook team

Before sending: fill verified production version and attach screenshots; do not attach customer data, bank account details or API secrets. This draft has not been submitted.
