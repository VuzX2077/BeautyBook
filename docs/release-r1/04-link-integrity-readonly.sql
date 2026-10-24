-- OWNER TEMPLATE: only after 01 confirms MUAStyles/ServiceStyles/MakeupStyles are present.
-- Keep detailed pair exports private. These aggregate fingerprints reveal no user identities.
-- MD5 here is a change-detection diagnostic, not an authorization/security signature.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '2s';
SELECT 'MUAStyles' AS relation,count(*) AS pairs,
 md5(coalesce(string_agg("MUAId"::text||':'||"StyleId"::text,',' ORDER BY "MUAId","StyleId"),'')) AS pair_fingerprint
FROM "MUAStyles";
SELECT 'ServiceStyles' AS relation,count(*) AS pairs,
 md5(coalesce(string_agg("ServiceId"::text||':'||"StyleId"::text,',' ORDER BY "ServiceId","StyleId"),'')) AS pair_fingerprint
FROM "ServiceStyles";
SELECT 'MUAStyles orphan styles' AS check_name,count(*) AS invalid
FROM "MUAStyles" l LEFT JOIN "MakeupStyles" s ON s."StyleId"=l."StyleId" WHERE s."StyleId" IS NULL;
SELECT 'ServiceStyles orphan styles' AS check_name,count(*) AS invalid
FROM "ServiceStyles" l LEFT JOIN "MakeupStyles" s ON s."StyleId"=l."StyleId" WHERE s."StyleId" IS NULL;
COMMIT;
