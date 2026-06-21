# BEAUTYBOOK PROJECT ARCHITECTURE

## System Overview
BeautyBook Backend is built on **ASP.NET Core 8 Web API** using **Entity Framework Core** with **PostgreSQL** (targeting Supabase). The application acts as a marketplace connecting Customers and Makeup Artists (MUAs).

## Folder Structure
```text
BeautyBookBackend/
├── Controllers/       # API Endpoints (HTTP mapping, basic validation)
├── Services/          # Business Logic (Coordinates repositories, applies rules)
├── Repositories/      # Data Access Layer (EF Core queries and persistence)
├── Models/            # Entity Framework Domain Models
│   └── Enums/         # Shared Enums (UserRole, BookingStatus)
├── DTOs/              # Data Transfer Objects (Input/Output shapes)
├── Data/              # ApplicationDbContext (EF Core configuration)
├── Migrations/        # EF Core Database Migrations
└── Hubs/              # SignalR Hubs (for future real-time chat/notifications)
```

## Database Schema Overview
The database uses PostgreSQL. Key tables include:
*   **Users**: The single identity table.
*   **MakeupArtistProfiles**: 1-to-1 extension of the Users table for those with MUA capabilities.
*   **Services**: Services offered by MUAs.
*   **Bookings**: Transactional table linking a Customer (`UserId`) and an MUA (`MUAId` -> `UserId`), and a `Service`.
*   **Wallets** & **WalletTransactions**: Financial ledger for the internal currency/balance system.
*   **Reviews**: Feedback linked to a completed Booking.
*   **Portfolios**, **ChatRooms**, **Messages**: Supporting features.

## Dual-Mode User System (Customer/MUA)
### Current Implementation (To Be Refactored)
Currently, identity is managed via the `User` table, and the role is an enum (`Admin=0, Customer=1, MUA=2`). Becoming an MUA overwrites the `Role` field to `MUA` and creates a `MakeupArtistProfile`.

### Target Architecture (The CTO Vision)
To achieve the true "Single User - Dual Mode" system:
1.  **Identity**: Every authenticated entity is a `User`.
2.  **Base Role**: All standard users possess the base `CUSTOMER` permissions.
3.  **Extension**: The presence of an active `MakeupArtistProfile` linked to the `User` dictates if they have MUA capabilities (`IsMuaEnabled = true`).
4.  **Mode Switching**: The frontend manages the "Mode" (Customer vs. MUA view). The backend authorizes actions based on whether the action requires standard Customer rights or MUA capability rights (verifying the profile exists).
5.  **No Split Identity**: A user uses the same email and password regardless of whether they are booking a service or providing one.

## Authentication Flow
1.  User registers/logs in via Email/Password or Google OAuth (`AuthController`).
2.  Backend generates a JWT (JSON Web Token) containing `UserId`, `Email`, and Roles/Claims.
3.  Client includes the JWT in the `Authorization: Bearer <token>` header for subsequent requests.
4.  Controllers use `[Authorize]` attributes to secure endpoints.

## Booking Flow
1.  **Creation**: Customer selects an MUA's service and proposes a date/time (`POST /api/booking`).
2.  **Payment**: If wallet balance is sufficient, funds are deducted and held in escrow (recorded via `WalletTransaction`).
3.  **Approval**: The MUA sees the `Pending` booking and can `Approve` or `Cancel` it.
4.  **Completion**: Upon successful service delivery, the booking is marked `Completed`. Funds (minus commission) are released to the MUA's wallet.
5.  **Review**: Customer can leave a review and rating for the MUA.

## API Structure
APIs are strictly RESTful:
*   `/api/auth/*`: Authentication and registration.
*   `/api/user/*`: Profile management.
*   `/api/mua/*`: MUA specific operations (services, portfolio).
*   `/api/booking/*`: Booking lifecycle management.
*   `/api/wallet/*`: Balance and transaction history.
*   `/api/review/*`: Ratings and comments.
