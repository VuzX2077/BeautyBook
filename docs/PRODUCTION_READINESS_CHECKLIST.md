# PRODUCTION READINESS CHECKLIST

Before deploying BeautyBook Backend to production (Render / Azure / VPS targeting Supabase), the following checklist MUST be satisfied to ensure stability, security, and scalability.

## 1. Security
- [ ] **HTTPS Enforced**: All traffic must route through HTTPS.
- [ ] **CORS Configuration**: `AllowAll` in development must be replaced with strict allowed origins (the production frontend domains).
- [ ] **Secrets Management**: No hardcoded secrets in `appsettings.json`. All DB credentials, JWT keys, and OAuth Client IDs must be loaded via Environment Variables.
- [ ] **Rate Limiting**: Implement API rate limiting to prevent DDoS and brute-force login attempts.
- [ ] **Input Sanitization**: Ensure EF Core parameters protect against SQL Injection (already largely handled by EF, but verify raw SQL queries if any).
- [ ] **Authorization Enforcement**: Verify all endpoints that require MUA permissions strictly check for MUA capability, not just authentication.

## 2. Authentication
- [ ] **Strong JWT Keys**: Ensure `Jwt:Key` is a cryptographically secure string (at least 256-bit/32 characters).
- [ ] **Token Expiration**: Set reasonable JWT expiration (e.g., 1 hour) and implement a Refresh Token mechanism.
- [ ] **OAuth Validation**: Ensure Google OAuth strictly validates the audience against our specific Client IDs.

## 3. Database Safety (Supabase / PostgreSQL)
- [ ] **Migrations Applied**: Ensure EF Core migrations are safely applied via CI/CD, not automatically on app startup in multiple containers.
- [ ] **Connection Pooling**: Use connection pooling (PgBouncer in Supabase) to prevent connection exhaustion under load.
- [ ] **Soft Deletes**: Verify critical tables (Users, Bookings, Wallets) do not allow hard `DELETE` operations via the API.
- [ ] **Indexing**: Add indexes to highly queried columns (`Email`, `CustomerId`, `MUAId`, `Status`).

## 4. Performance
- [ ] **N+1 Query Problem**: Review Repositories to ensure eager loading (`.Include()`) is used correctly to prevent N+1 queries.
- [ ] **Pagination**: Ensure all list endpoints (Bookings, Reviews, MUA search) implement pagination. Never return `ToListAsync()` on unbound datasets.
- [ ] **Async All The Way**: Verify no synchronous blocking calls (`.Result`, `.Wait()`) exist in the request pipeline.

## 5. Logging & Monitoring
- [ ] **Structured Logging**: Implement Serilog or similar for structured JSON logging.
- [ ] **Error Tracking**: Integrate a crash reporting tool (e.g., Sentry or Application Insights) to catch unhandled exceptions.
- [ ] **Health Checks**: Enhance the `/health` endpoint to check Database connectivity, not just API responsiveness.

## 6. Deployment
- [ ] **Dockerfile Optimization**: Use multi-stage Docker builds to keep the production image small and secure (exclude SDKs in the final image).
- [ ] **Environment Variables**: Document all required environment variables for the deployment target.
- [ ] **Reverse Proxy**: If deploying to a VPS, ensure Nginx or Caddy is configured correctly as a reverse proxy.

## 7. Backup Strategy
- [ ] **Database Backups**: Verify Supabase automated backups are enabled and PITR (Point-In-Time Recovery) is active.
- [ ] **Disaster Recovery**: Document the steps to restore the database in case of catastrophic failure.
