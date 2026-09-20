# Supabase Storage deployment

The API uploads images to a public Supabase Storage bucket. Create a public
bucket named `images`, then configure these server-side environment variables
on Render:

- `SUPABASE_URL=https://<project-ref>.supabase.co`
- `SUPABASE_SERVICE_ROLE_KEY=<service-role-key>`
- `SUPABASE_STORAGE_BUCKET=images` (optional; defaults to `images`)

Never expose `SUPABASE_SERVICE_ROLE_KEY` through an `EXPO_PUBLIC_*` variable.
The mobile app uploads through the authenticated `/api/Upload/image` endpoint.

Set `ApplyMigrations=true` for the first deployment containing migration
`20260920183000_UpgradeLegacyImageUrlsToHttps`. It upgrades existing Render
image URLs from HTTP to HTTPS. It does not copy the old image bytes; old images
remain served by Render while all new images are stored in Supabase.

After deployment, upload an image and verify that `/api/Feed` returns an URL
starting with:

`https://<project-ref>.supabase.co/storage/v1/object/public/images/`
