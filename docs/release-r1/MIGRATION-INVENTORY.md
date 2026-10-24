# Migration inventory — offline R1

72 IDs, đúng thứ tự từ EF assembly; 133 source files (gồm designer/snapshot) đã được đọc và hash. `migration-inventory.json` có đầy đủ Up/Down operations, raw SQL và hash. Metadata/SQL generation không mở connection. Model drift: false. Đây không phải actual history/pending set của target thật.

| # | Migration ID | Up operations | Bảng / phạm vi | Cần review |
|---|---|---|---|---|
| 1 | 20260614170003_InitialCreatePostgres | CreateTable×14, CreateIndex×18 | MakeupStyles, Products, Users, MakeupArtistProfiles, ProductReviews, Wallets, ChatRooms, MUAStyles, Portfolios, Services, WalletTransactions, Messages, Bookings, Reviews | index build/write blocking |
| 2 | 20260620060411_UpdateMUAAndUserFields | DropColumn×3, RenameColumn×2, AddColumn×9 | Bookings, MakeupArtistProfiles, Users | destructive/type change; ALTER/constraint lock |
| 3 | 20260620083001_AddServiceAndPortfolioTags | AddColumn×4 | Services, Portfolios | ALTER/constraint lock |
| 4 | 20260620112329_AddPortfolioInteractions | CreateTable×3, CreateIndex×3 | PortfolioComments, PortfolioLikes, PortfolioSaves | index build/write blocking |
| 5 | 20260620145716_AddProfileQualityScore | AddColumn×1 | MakeupArtistProfiles | ALTER/constraint lock |
| 6 | 20260620151126_FixPortfolioForeignKey | DropForeignKey×1, DropIndex×1, DropColumn×1, CreateIndex×1, AddForeignKey×1 | Portfolios | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 7 | 20260620162808_AddPortfolioOptionsAndMultipleImages | DropColumn×1, AddColumn×3 | Portfolios | destructive/type change; ALTER/constraint lock |
| 8 | 20260620212811_UpdateBookingCart | DropForeignKey×1, DropIndex×1, DropColumn×3, AddColumn×7, CreateTable×1, CreateIndex×2 | Bookings, BookingServices | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 9 | 20260622145057_AddImageUrlToReview | AddColumn×1 | Reviews | ALTER/constraint lock |
| 10 | 20260622154137_AddMuaReplyToReview | AddColumn×2 | Reviews | ALTER/constraint lock |
| 11 | 20260913051644_AddPaymentStatusToBooking | AddColumn×1, Sql×2 | Bookings | ALTER/constraint lock; data/backfill |
| 12 | 20260913065236_ConfigureMuaProfileRelationships | DropForeignKey×3, DropIndex×3, DropColumn×3, CreateIndex×2, AddForeignKey×3 | MUAStyles, Services | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 13 | 20260914030857_AddWalletTopUps | CreateTable×1, CreateIndex×4 | WalletTopUps | index build/write blocking |
| 14 | 20260914033338_AddWalletTransactionReferences | AddColumn×2, CreateIndex×1 | WalletTransactions | ALTER/constraint lock; index build/write blocking |
| 15 | 20260917111859_AddBookingEscrowFlow | AddColumn×16, Sql×1 | Bookings | ALTER/constraint lock; data/backfill |
| 16 | 20260918033000_AddPortfolioServiceAndRichChat | AddColumn×3, CreateIndex×3, AddForeignKey×2, CreateTable×1 | Portfolios, Messages, MessageReactions | ALTER/constraint lock; index build/write blocking |
| 17 | 20260918050000_AddPushNotifications | CreateTable×2, CreateIndex×5 | DevicePushTokens, AppNotifications | index build/write blocking |
| 18 | 20260918180000_AddPortfolioCommentRepliesAndInteractionConstraints | AddColumn×1, Sql×1, CreateIndex×3, AddForeignKey×1 | PortfolioComments, PortfolioLikes, PortfolioSaves | ALTER/constraint lock; index build/write blocking; data/backfill |
| 19 | 20260919023524_AddDirectBookingPayments | AddColumn×1, CreateTable×1, CreateIndex×4 | Bookings, BookingPayments | ALTER/constraint lock; index build/write blocking |
| 20 | 20260919025237_AddAccountDeletionLifecycle | AddColumn×1 | Users | ALTER/constraint lock |
| 21 | 20260919032118_HardenDirectBookingDepositPhase1 | DropIndex×1, CreateIndex×1 | WalletTransactions | destructive/type change; index build/write blocking |
| 22 | 20260919033928_AddRefundLifecyclePhase2 | CreateTable×1, CreateIndex×4, Sql×1 | Refunds | index build/write blocking; data/backfill |
| 23 | 20260919035300_AddMuaReceivableLedgerPhase3 | CreateTable×1, CreateIndex×2, Sql×1 | MuaReceivables | index build/write blocking; data/backfill |
| 24 | 20260919041205_AddMuaPayoutPhase4 | CreateTable×3, CreateIndex×8 | MuaBankAccounts, Payouts, PayoutItems | index build/write blocking |
| 25 | 20260919043007_RetireCustomerWalletPhase5 | DropForeignKey×2, CreateIndex×1, AddForeignKey×2 | Wallets, WalletTransactions | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 26 | 20260919093306_AddMuaEligibilityAndActiveServices | AddColumn×1 | Services | ALTER/constraint lock |
| 27 | 20260919094243_AddServiceLocationToBookings | AddColumn×3 | Bookings | ALTER/constraint lock |
| 28 | 20260919095308_AddMuaScheduleAndBookingIdempotency | DropIndex×1, AddColumn×1, CreateTable×2, CreateIndex×3 | Bookings, MuaTimeOffs, MuaWorkingSchedules | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 29 | 20260919133025_AddBookingCancellationRefundPolicy | AddColumn×7, AddCheckConstraint×1, CreateIndex×1, AddForeignKey×1 | Bookings, Refunds | ALTER/constraint lock; index build/write blocking |
| 30 | 20260920090000_ExtendPortfolioDescription | AlterColumn×1 | Portfolios | destructive/type change; ALTER/constraint lock |
| 31 | 20260920120000_AddCustomerRefundDestinationAndPayout | AddColumn×11, CreateTable×1, CreateIndex×3 | Refunds, CustomerBankAccounts | ALTER/constraint lock; index build/write blocking |
| 32 | 20260920143000_BackfillAwaitingRefundDestinations | Sql×1 | Raw SQL / metadata | data/backfill |
| 33 | 20260920153000_AddNotificationReadState | AddColumn×1, CreateIndex×1 | AppNotifications | ALTER/constraint lock; index build/write blocking |
| 34 | 20260920170000_AddAdminNotificationCampaigns | CreateTable×1, AddColumn×1, CreateIndex×4, AddForeignKey×1 | NotificationCampaigns, AppNotifications | ALTER/constraint lock; index build/write blocking |
| 35 | 20260920183000_UpgradeLegacyImageUrlsToHttps | Sql×6 | Raw SQL / metadata | data/backfill |
| 36 | 20260920190334_AddMuaSpecialtiesAndSocialProfiles | AddColumn×4, CreateIndex×1, Sql×1 | MakeupStyles, MakeupArtistProfiles | ALTER/constraint lock; index build/write blocking; data/backfill |
| 37 | 20260922220914_AddMuaApplicationApproval | AddColumn×5, Sql×1, CreateIndex×2, AddForeignKey×1 | MakeupArtistProfiles | ALTER/constraint lock; index build/write blocking; data/backfill |
| 38 | 20260922225421_SecureBankAccountsAndTotalFee | AddColumn×9 | Refunds, Payouts, MuaBankAccounts, CustomerBankAccounts, Bookings | ALTER/constraint lock |
| 39 | 20260923003257_AddBankAccountAdminReview | AddColumn×6 | MuaBankAccounts, CustomerBankAccounts | ALTER/constraint lock |
| 40 | 20260929054155_ExpandMuaApplicationFlow | AddColumn×6, Sql×1, AlterColumn×1 | MakeupArtistProfiles | destructive/type change; ALTER/constraint lock; data/backfill |
| 41 | 20260929064440_HardenBankAccountEligibility | AddColumn×1, AlterColumn×4, CreateIndex×1, AddForeignKey×1 | Refunds, MuaBankAccounts, CustomerBankAccounts | destructive/type change; ALTER/constraint lock; index build/write blocking |
| 42 | 20260929201739_CleanupLegacyInvalidBankDefaults | Sql×1 | Raw SQL / metadata | data/backfill |
| 43 | 20260929233118_AddEmailOtpAuthentication | CreateTable×1, CreateIndex×2 | EmailOtps | index build/write blocking |
| 44 | 20260930020019_HardenRealtimeChat | Sql×1, DropIndex×2, AlterColumn×1, AddColumn×1, CreateIndex×2 | Messages, ChatRooms | destructive/type change; ALTER/constraint lock; index build/write blocking; data/backfill |
| 45 | 20260930021414_AddChatPushNotifications | No-op | Raw SQL / metadata | review normal DDL |
| 46 | 20260930051339_UnifyUserBankAccounts | DropForeignKey×1, AddColumn×3, CreateTable×1, Sql×4, DropTable×2, CreateIndex×4, AddForeignKey×2 | Refunds, Payouts, BankAccounts, CustomerBankAccounts, MuaBankAccounts | destructive/type change; ALTER/constraint lock; index build/write blocking; data/backfill |
| 47 | 20260930070000_RepairChatPushNotificationSchema | Sql×1 | AppNotifications | index build/write blocking; data/backfill |
| 48 | 20260930145141_AddMuaFollows | CreateTable×1, CreateIndex×2 | MuaFollows | index build/write blocking |
| 49 | 20260930180426_AddMuaOnboardingAreaAndExperience | AddColumn×6 | MakeupArtistProfiles | ALTER/constraint lock |
| 50 | 20260930184659_AddServiceIllustrationImages | AddColumn×1, Sql×1 | Services | ALTER/constraint lock; data/backfill |
| 51 | 20260930213632_AddEmailOtpContextHash | AddColumn×1 | EmailOtps | ALTER/constraint lock |
| 52 | 20260930223055_AddMuaOperatingAreasAndConfirmedLocation | AddColumn×4, CreateTable×1, CreateIndex×2, Sql×1 | MakeupArtistProfiles, MuaOperatingAreas | ALTER/constraint lock; index build/write blocking; data/backfill |
| 53 | 20260930235122_AddExplorePagingIndexes | CreateIndex×3 | Services, Portfolios, MakeupArtistProfiles | index build/write blocking |
| 54 | 20261001011443_AddBookingComplaints | CreateTable×2, CreateIndex×3, Sql×1 | BookingComplaints, ComplaintMessages | index build/write blocking; data/backfill |
| 55 | 20261002072400_AddPrivateVerificationMedia | CreateTable×1, CreateIndex×2 | VerificationMedia | index build/write blocking |
| 56 | 20261002072602_BindPrivateAttachmentsToContext | AddColumn×1 | VerificationMedia | ALTER/constraint lock |
| 57 | 20261002072646_AddPrivateMediaMaintenanceJobs | CreateTable×1, CreateIndex×2, Sql×1 | PrivateMediaJobs | index build/write blocking; data/backfill; trigger/function/guard |
| 58 | 20261002114018_AddAccountDeletionStorageLifecycle | AddColumn×4, CreateTable×2, CreateIndex×2, Sql×1 | Users, VerificationMedia, AccountDeletionRequests, OwnedPublicMedia | ALTER/constraint lock; index build/write blocking; data/backfill; trigger/function/guard |
| 59 | 20261002161522_AllowRetainedReceivableLifecycleAfterCustomerDeletion | Sql×1 | Raw SQL / metadata | data/backfill; trigger/function/guard |
| 60 | 20261002182347_AddPrivateFinancialQrReferences | AddColumn×3, CreateIndex×3, AddForeignKey×3, Sql×1 | Refunds, Payouts, BankAccounts | ALTER/constraint lock; index build/write blocking; data/backfill; trigger/function/guard; Down BLOCKED: reviewed roll-forward |
| 61 | 20261003010637_SharedReceivingAccountReview | Sql×2, AddColumn×3 | Refunds, BankAccounts | ALTER/constraint lock; data/backfill; trigger/function/guard; Down BLOCKED: reviewed roll-forward |
| 62 | 20261003080809_AddPlayReviewDomainFoundation | AddColumn×2 | Users, Bookings | ALTER/constraint lock |
| 63 | 20261004130144_AddUgcModeration | CreateTable×2, CreateIndex×6, Sql×1 | ContentReports, UserBlocks | index build/write blocking; data/backfill; trigger/function/guard |
| 64 | 20261005071302_AddWorkLocationAndBookingDestination | AddColumn×5 | MakeupArtistProfiles, Bookings | ALTER/constraint lock |
| 65 | 20261006092242_AddUserFeedback | CreateTable×2, CreateIndex×4 | UserFeedbacks, FeedbackEvents | index build/write blocking |
| 66 | 20261008090000_AddFeedbackAccountDeletionGuards | Sql×1 | UserFeedbacks, FeedbackEvents | data/backfill; trigger/function/guard |
| 67 | 20261010000000_AddAiMakeupDailyUsage | Sql×1 | AiMakeupDailyUsage | data/backfill; trigger/function/guard |
| 68 | 20261010001000_AddFeedbackPrivateMediaGuards | Sql×1 | Raw SQL / metadata | data/backfill; trigger/function/guard |
| 69 | 20261014140542_UnifiedServiceStyles | AddColumn×2, CreateTable×1, CreateIndex×2, Sql×1 | Services, MakeupStyles, ServiceStyles | ALTER/constraint lock; index build/write blocking; data/backfill; trigger/function/guard |
| 70 | 20261015000000_AddWebsiteApkCounters | Sql×1 | WebsiteApkClicks, WebsiteCounterSources | index build/write blocking; data/backfill; trigger/function/guard |
| 71 | 20261018003245_MakeupCatalogCore | AddColumn×9, CreateTable×3, CreateIndex×11, Sql×1 | MakeupStyles, MakeupCatalogChanges, MakeupCatalogTerms, MakeupStyleTermLinks | ALTER/constraint lock; index build/write blocking; data/backfill; trigger/function/guard |
| 72 | 20261018024829_PortfolioCatalogTagging | AddColumn×1, CreateTable×2, CreateIndex×5, Sql×1 | Portfolios, PortfolioStyleLinks, PortfolioTermLinks | ALTER/constraint lock; index build/write blocking; data/backfill; trigger/function/guard |

## Review toàn chain

- Phân loại lock là conservative từ operations, không phải phép đo duration trên production. ALTER TABLE/FK/CHECK thường cần lock mạnh; CREATE INDEX không concurrent có thể chặn ghi. Thời gian/lock budget phải đo trên restore cô lập có kích thước gần target.
- Chỉ áp dụng pending set sau khi chủ dự án cung cấp actual history được phép thu thập. Không coi cả 72 là pending, cũng không coi chỉ hai migration B/E1 là pending.
- Các SQL backfill/delete/rename/type changes, financial/media/account guards và unique indexes cần review theo thứ tự, không tách/chạy thủ công tùy ý.
- Production startup tự apply migrations trước traffic: cần maintenance window/lock timeout policy và backup trước deployment được phê duyệt.
- Một số Down cố ý throw NotSupportedException; tuyệt đối không vô hiệu guard. Backend E1 phải giữ account-deletion cleanup khi rollback consumer.
