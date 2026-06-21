# BEAUTYBOOK AI ENGINEERING RULES & GOVERNANCE

THIS IS THE SUPREME LAW OF THE PROJECT. ALL FUTURE AI ASSISTANTS MUST READ AND ADHERE TO THIS FILE BEFORE MAKING ANY CHANGES.

## 1. Architecture Rules

*   **Layer Separation**: We follow a Clean but Lightweight Architecture.
    *   **Controller Layer**: Handles HTTP requests, input validation, and returns HTTP responses. MUST NOT contain business logic.
    *   **Service Layer**: Contains all core business logic. Validates rules, coordinates between repositories.
    *   **Repository Layer**: Handles all database operations using Entity Framework Core. Returns domain models, not DTOs.
    *   **DTO Layer**: Used for data transfer between Client and Controller, and Controller and Service.
*   **Dependency Injection**: Use standard ASP.NET Core DI. Controllers depend on Interfaces (Services), Services depend on Interfaces (Repositories).
*   **SOLID Principles**: Code must follow SOLID principles. Favor composition over inheritance. Keep classes small and focused.
*   **No Over-engineering**: Build for the MVP first. Do not add complex CQRS, Event Sourcing, or Microservices unless explicitly approved. Keep it a majestic monolith.

## 2. The Core Business Model: Single User - Dual Mode System

THIS IS THE MOST CRITICAL RULE:
*   **ONLY ONE User entity**. There are NO separate `MUA` accounts.
*   Every user starts as a `CUSTOMER` by default.
*   Users can "upgrade" their capabilities to become an MUA. This means they are a Customer with an MUA capability extension (`MakeupArtistProfile`).
*   **Authentication is Unified**: A user logs in once. They can switch between "Customer Mode" and "MUA Mode" on the frontend.
*   DO NOT duplicate authentication, identity, or create multiple user tables.
*   *(Note: The current `UserRole` enum implementation needs to be refactored to support `CurrentMode` and `IsMuaEnabled` instead of overwriting the role. See Refactoring Roadmap).*

## 3. Coding Standards

*   **Naming Conventions**:
    *   Classes, Methods, Properties: `PascalCase`
    *   Parameters, Local Variables: `camelCase`
    *   Private Fields: `_camelCase`
    *   Interfaces: Prefix with `I` (e.g., `IUserService`)
    *   Async Methods: Suffix with `Async` (e.g., `GetUserAsync`)
*   **DTO Rules**:
    *   Suffix all DTOs with `Dto` (e.g., `UserDto`, `BookingCreateDto`).
    *   Never expose internal Entity Framework models directly to the API response.
*   **File Structure**:
    *   One class/interface per file.
    *   Maintain the existing folder structure (`Controllers`, `Services`, `Repositories`, `Models`, `DTOs`, `Data`).

## 4. Database Standards

*   **Audit Fields**: All main entities should track creation and updates (e.g., `CreatedAt`, `UpdatedAt`).
*   **Soft Delete**: For critical data (Users, Bookings, Wallet Transactions), use soft delete logic (e.g., `IsActive`, `IsDeleted`) instead of hard deletion.
*   **Index Strategy**: Ensure foreign keys and frequently queried fields (like `Email`, `UserId`) are indexed.
*   **Relationship Rules**: Be explicit about cascade deletes to avoid circular dependencies (e.g., Use `DeleteBehavior.Restrict` where a user has multiple paths to an entity).

## 5. API Standards

*   **Response Format**: Use standard HTTP status codes.
    *   `200 OK` for successful GET, PUT, POST (if returning the object).
    *   `201 Created` for resource creation.
    *   `400 BadRequest` for validation errors.
    *   `401 Unauthorized` / `403 Forbidden` for security.
    *   `404 NotFound` when resource doesn't exist.
*   **REST Conventions**: Use plural nouns for endpoints (`/api/users`, `/api/bookings`).
*   **Error Handling**: Do not expose raw exception stack traces to the client.

## 6. Security Standards

*   **JWT Authentication**: All secure endpoints must require a valid JWT Bearer token.
*   **Role/Mode Validation**: Endpoints meant for MUAs must validate that the User actually has MUA capabilities enabled.
*   **Input Validation**: Use Data Annotations on DTOs and check `ModelState` in controllers (or use FluentValidation).
*   **Secret Management**: Never hardcode secrets. Use environment variables (Supabase connection string, JWT keys).

## 7. Booking System Rules

*   **Booking Lifecycle**: `Pending` -> `Approved` -> `Completed` (or `Cancelled`).
*   **State Transitions**: MUA can Approve/Cancel. Customer can Cancel (if allowed). System marks as Completed.
*   **Conflict Prevention**: Implement logic to prevent overlapping bookings for the same MUA.

## 8. Wallet Rules

*   **Transaction Consistency**: All balance changes must be accompanied by an immutable `WalletTransaction` record.
*   **Audit Logging**: The transaction `Description` must clearly state the reason for the change.

## 9. AI Collaboration Rules

*   **Read First**: Always read this `AI_ENGINEERING_RULES.md` and `PROJECT_ARCHITECTURE.md` before writing code.
*   **No Duplication**: Never duplicate existing code or patterns. Reuse existing services.
*   **Explain Impact**: Before modifying core models (User, Booking, ApplicationDbContext), explain the impact on the database and existing flows.
*   **Extend, Don't Rewrite**: Extend the existing architecture. Do not rewrite working modules unless instructed for a specific refactor.
