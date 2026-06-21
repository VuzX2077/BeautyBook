# BEAUTYBOOK PRODUCT VISION

## Product Mission
To create the most seamless, reliable, and premium marketplace connecting clients with professional Makeup Artists (MUAs). BeautyBook simplifies discovery, booking, secure payments, and reputation management in the beauty services industry.

## MVP Scope
The Minimum Viable Product focuses on the core loop: 
Discovery -> Booking -> Secure Payment -> Fulfillment -> Review.
We prioritize stability, core security, and a flawless Dual-Mode User experience over complex secondary features.

## User Roles
*   **Customer (Default)**: Every account starts here. Customers can browse MUAs, book services, pay via internal wallet, and leave reviews.
*   **MUA (Upgraded Mode)**: An extension of a Customer account. By toggling to "MUA Mode," the user can manage their professional profile, list services, accept/reject bookings, and withdraw wallet earnings.
*   **Admin**: Internal staff role to monitor platform health, resolve disputes, and manage platform configurations.

## Core Journeys

### The Customer Journey
1.  **Onboarding**: Sign up via Email or Google.
2.  **Discovery**: Browse MUAs by style, rating, or search.
3.  **Booking**: Select a service, choose a time, provide an address/notes.
4.  **Payment**: Top up the internal wallet (mocked for MVP) and pay for the booking.
5.  **Service & Review**: Receive the makeup service and rate the MUA to build platform trust.

### The MUA Journey
1.  **Upgrade**: From their existing Customer account, opt-in to become an MUA.
2.  **Profile Setup**: Set up bio, upload portfolio images, and define service packages with pricing.
3.  **Booking Management**: Receive notifications for new bookings. Approve or decline based on availability.
4.  **Fulfillment**: Deliver the service and mark the booking as completed.
5.  **Earnings**: Receive payment directly into their digital wallet (minus platform commission) and track transaction history.

## MVP Roadmap

### Phase 1: MVP Launch (Foundation)
*   User Authentication (JWT, Google OAuth).
*   Single User Dual-Mode architecture enforcement.
*   MUA Profiles and Service listings.
*   Basic Booking flow (Pending -> Approved -> Completed).
*   Wallet system (Mock top-up, escrow, payout).

### Phase 2: Stability & Usability Improvements
*   Real-time chat between Customer and MUA (`ChatRoom` & `Messages` via SignalR).
*   Advanced MUA availability calendar (preventing overlaps).
*   Advanced filtering and search functionality.

### Phase 3: Scale
*   Integration with real payment gateways (Stripe/VNPay) for Wallet Top-ups and Withdrawals.
*   Automated email/SMS notifications for booking state changes.
*   Performance optimization (Redis caching for MUA searches).

### Phase 4: Monetization & Expansion
*   Premium placement for MUAs.
*   Product marketplace (selling physical makeup products alongside services).
*   Analytics dashboard for MUAs.
