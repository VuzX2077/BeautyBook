-- OWNER-OPERATED TEMPLATE ONLY. Do not execute until target/read-only access is separately approved.
-- Connect with a dedicated SELECT-only role using a local psql service/secret store; no credentials in this file.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '2s';
SELECT current_database() AS database_name, current_user AS review_role,
       inet_server_addr() AS server_address, inet_server_port() AS server_port,
       current_setting('server_version') AS server_version;
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema='public' AND table_name IN
 ('MakeupStyles','MUAStyles','ServiceStyles','MakeupCatalogTerms','MakeupStyleTermLinks',
  'MakeupCatalogChanges','Portfolios','PortfolioStyleLinks','PortfolioTermLinks')
ORDER BY table_name, ordinal_position;
SELECT c.relname AS table_name, t.tgname AS trigger_name, t.tgenabled,
       t.tgdeferrable, t.tginitdeferred
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='public' AND NOT t.tgisinternal
AND c.relname IN ('MakeupStyles','MakeupCatalogTerms','Portfolios','PortfolioStyleLinks','PortfolioTermLinks')
ORDER BY c.relname,t.tgname;
COMMIT;
