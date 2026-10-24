-- OWNER TEMPLATE: run only after 01 confirms these legacy tables/columns exist.
-- Output contains catalog identity, not passwords, email, tokens or connection credentials.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '2s';
SELECT "StyleId", "Name", "IsActive" FROM "MakeupStyles" ORDER BY "StyleId";
SELECT "StyleId", count(DISTINCT "MUAId") AS mua_count
FROM "MUAStyles" GROUP BY "StyleId" ORDER BY "StyleId";
-- Collision hints only. Do not turn this query into automatic canonical mapping.
SELECT lower(regexp_replace(btrim("Name"), '\s+', ' ', 'g')) AS name_hint,
       array_agg("StyleId" ORDER BY "StyleId") AS style_ids, count(*) AS rows
FROM "MakeupStyles" WHERE "Name" IS NOT NULL
GROUP BY lower(regexp_replace(btrim("Name"), '\s+', ' ', 'g')) HAVING count(*)>1;
COMMIT;
