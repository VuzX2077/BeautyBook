-- READ ONLY. Not executed against production in this task.
-- Results contain sensitive references: keep within operator-controlled access.
WITH refs AS (
  SELECT 'BankAccounts' AS source, "Id" AS row_id, "UserId" AS owner_id, "QrCodeUrl" AS url
  FROM "BankAccounts" WHERE NULLIF("QrCodeUrl", '') IS NOT NULL
  UNION ALL
  SELECT 'Payouts', "Id", "MuaId", "QrCodeUrlSnapshot"
  FROM "Payouts" WHERE NULLIF("QrCodeUrlSnapshot", '') IS NOT NULL
  UNION ALL
  SELECT 'Refunds', r."RefundId", b."CustomerId", r."DestinationQrCodeUrl"
  FROM "Refunds" r JOIN "Bookings" b ON b."BookingId" = r."BookingId"
  WHERE NULLIF(r."DestinationQrCodeUrl", '') IS NOT NULL
), owners AS (
  SELECT url, COUNT(DISTINCT owner_id) AS owner_count, COUNT(*) AS reference_count
  FROM refs GROUP BY url
)
SELECT r.*, o.owner_count, o.reference_count,
  CASE WHEN r.url LIKE '%/storage/v1/object/public/%' THEN 'PUBLIC_STORAGE_CANDIDATE_VERIFY_HOST_BUCKET'
       WHEN r.url LIKE 'https://img.vietqr.io/%' THEN 'EXTERNAL_VIETQR_NOT_OWNED_STORAGE'
       ELSE 'OTHER_EXTERNAL_OR_UNKNOWN' END AS reference_type,
  m."Id" AS owned_media_id, m."OwnerId" AS recorded_owner,
  m."ObjectKey", m."ReadyAt", m."DeletedAt", m."StorageDeletedAt",
  (m."OwnerId" = r.owner_id AND o.owner_count = 1) AS ledger_owner_matches
FROM refs r JOIN owners o ON o.url = r.url
LEFT JOIN "OwnedPublicMedia" m ON m."Url" = r.url
ORDER BY r.source, r.row_id;
-- No DELETE/UPDATE. Exact matching can miss historical URL encoding variants.
-- No match is not evidence of safe deletion; it means ownership needs review.
