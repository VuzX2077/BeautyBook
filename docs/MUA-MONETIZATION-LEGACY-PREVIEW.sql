-- Read-only. Run against the intended staging/snapshot first; no configuration secrets.
SELECT p."Status", p."VerificationStatus", count(*) AS "ProfilesWithoutEnrollment"
FROM "MakeupArtistProfiles" p
JOIN "Users" u ON u."UserId" = p."MUAId"
WHERE u."DeletedAt" IS NULL AND NOT u."IsDemoAccount"
  AND NOT EXISTS (SELECT 1 FROM "MuaMonetizationAccounts" a WHERE a."MuaId" = p."MUAId")
GROUP BY p."Status", p."VerificationStatus"
ORDER BY p."Status", p."VerificationStatus";
