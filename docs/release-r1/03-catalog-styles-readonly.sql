-- OWNER TEMPLATE: only AFTER 01 confirms Catalog Core columns and ServiceStyles exist.
-- Do not run before migration merely to assume Version=1 or invent a Key.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '2s';
SELECT s."StyleId",s."Key",s."Name",s."Version",s."Source",s."ReviewStatus",
       s."IsActive",s."ArchivedAt",s."SortOrder",
       (s."SystemDefinition" IS NOT NULL) AS has_system_definition,
       (SELECT count(DISTINCT m."MUAId") FROM "MUAStyles" m WHERE m."StyleId"=s."StyleId") AS mua_count,
       (SELECT count(DISTINCT v."ServiceId") FROM "ServiceStyles" v WHERE v."StyleId"=s."StyleId") AS service_count
FROM "MakeupStyles" s ORDER BY s."StyleId";
SELECT "Key",count(*) FROM "MakeupStyles" WHERE "Key" IS NOT NULL GROUP BY "Key" HAVING count(*)>1;
SELECT "Kind",count(*) AS records,count(DISTINCT "Key") AS distinct_keys
FROM "MakeupCatalogTerms" GROUP BY "Kind" ORDER BY "Kind";
COMMIT;
