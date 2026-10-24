# Reviews and comments after account deletion

Account deletion retains review stars and the MUA's existing average. The deletion transaction clears the author's review text and image references. Review responses additionally redact deleted authors on read, expose `isAuthorDeleted`, and retain existing MUA replies. New review replies to a deleted author are rejected.

Portfolio comments retain their identifiers and parent links internally. Deleted authors' text is cleared during deletion and replaced on reads with `Bình luận đã được xóa`, the name `Người dùng đã xóa`, no avatar, and `isAuthorDeleted: true`. Deleted leaves are hidden. Deleted ancestors remain only when a surviving conversation needs them; this works across multiple reply levels and also for accounts deleted before this release.

The comment endpoint and feed, MUA portfolio, and saved/liked portfolio counters share one visibility calculation. Counts include visible placeholders and surviving replies. Moderated or blocked comments remain excluded; replies whose parent was excluded are not exposed as orphaned conversations. Reads are batched across the portfolios being returned, without querying separately for each post.

The mobile app removes reply and user-specific safety actions for deleted authors. Retained review ratings can still be reported as content. Migration `20261024090000_AllowRetainedReviewModeration` narrowly permits reports on an existing review whose customer matches the retained author. It preserves the deletion lock and rejects deleted reporters/reviewers, user blocks, user/comment reports, and fabricated review references involving deleted accounts. It does not restore account data or allow new comments/review replies from deleted users.

## Deployment

Deploy the backend first. Production startup already applies EF migrations under the existing migration lock; confirm the new migration completes before publishing the mobile build. No data backfill or destructive deletion is required. Existing deleted-account comments use the new visibility rules immediately on API reads. Deploying and building the mobile application remain manual.

## Verification

`DeletedAccountContentTests` exercises actual PostgreSQL migrations and account deletion, review ratings, preserved MUA replies, nested conversations, matching counters across post lists, active-author interactions, moderation/block filtering, retained-review reporting/admin decisions, and database rejection of unauthorized writes involving deleted users.

The mobile component tests cover anonymous rendering, suppressed private text/photos, preserved stars/averages, nested replies, active-author reply submission and cache invalidation, the alternate review tab, and content-only reporting versus normal safety actions.
