using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using MakeupService = BeautyBookBackend.Models.Service;

namespace BeautyBookBackend.Services
{
    public class MuaService : IMuaService
    {
        private readonly IMuaRepository _muaRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BeautyBookBackend.Data.ApplicationDbContext _dbContext;
        private readonly IMuaEligibilityService _eligibilityService;
        private readonly PlayReviewPolicy _playReview;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public MuaService(IMuaRepository muaRepository, IUserRepository userRepository, IUnitOfWork unitOfWork, BeautyBookBackend.Data.ApplicationDbContext dbContext, IMuaEligibilityService eligibilityService, IHttpContextAccessor? httpContextAccessor = null, PlayReviewPolicy? playReview = null)
        {
            _muaRepository = muaRepository;
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _dbContext = dbContext;
            _eligibilityService = eligibilityService;
            _playReview = playReview ?? new PlayReviewPolicy(dbContext);
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<List<MuaProfileDto>> GetMuasAsync(int page)
        {
            var pageSize = 20; // Hardcoded MVP page size
            var profiles = await _muaRepository.GetProfilesAsync(page, pageSize);
            var muaIds = profiles.Select(profile => profile.MUAId).ToList();
            if (muaIds.Count == 0) return new List<MuaProfileDto>();

            var styleRows = await _dbContext.MUAStyles
                .Where(link => muaIds.Contains(link.MUAId) && link.MakeupStyle != null && link.MakeupStyle.Name != null)
                .Select(link => new { link.MUAId, Name = link.MakeupStyle!.Name! })
                .ToListAsync();
            var stylesByMua = styleRows
                .GroupBy(row => row.MUAId)
                .ToDictionary(group => group.Key, group => group.Select(row => row.Name).ToList());

            var minPrices = await _dbContext.Services
                .Where(service => muaIds.Contains(service.MUAId) && service.IsActive)
                .GroupBy(service => service.MUAId)
                .Select(group => new { MuaId = group.Key, MinPrice = group.Min(service => service.Price) })
                .ToDictionaryAsync(row => row.MuaId, row => (decimal?)row.MinPrice);

            var reviewCounts = await _dbContext.Reviews
                .Where(review => muaIds.Contains(review.MUAId))
                .GroupBy(review => review.MUAId)
                .Select(group => new { MuaId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.MuaId, row => row.Count);

            return profiles.Select(profile =>
            {
                var dto = ToMuaProfileDto(
                    profile,
                    stylesByMua.GetValueOrDefault(profile.MUAId) ?? new List<string>());
                dto.MinPrice = minPrices.GetValueOrDefault(profile.MUAId);
                dto.ReviewCount = reviewCounts.GetValueOrDefault(profile.MUAId);
                return dto;
            }).ToList();
        }

        public async Task<MuaDetailDto?> GetMuaByIdAsync(Guid muaId, Guid? currentUserId = null)
        {
            var profile = await _muaRepository.GetProfileWithFullDetailsAsync(muaId);
            if (profile == null) return null;
            var isOwner = currentUserId == muaId;
            if (!isOwner && (profile.Status != Models.Enums.MuaStatus.Listed || profile.VerificationStatus != Models.Enums.MuaVerificationStatus.Approved || profile.User?.IsActive != true || profile.User.DeletedAt.HasValue))
            {
                if (!currentUserId.HasValue || muaId != _playReview.Simulation.CounterpartUserId) return null;
                try { await _playReview.EnsureDemoMuaCapabilityAsync(currentUserId.Value, currentUserId.Value, muaId, await _eligibilityService.EvaluateAsync(muaId, false)); }
                catch (PlayReviewOperationException) { return null; }
            }

            var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(muaId);
            var services = await _muaRepository.GetServicesByMuaIdAsync(muaId);
            var portfolio = await _muaRepository.GetPortfolioByMuaIdAsync(muaId);
            var visibleIds = await ModerationService.VisiblePortfolios(_dbContext, _dbContext.Portfolios.Where(p => p.MUAId == muaId), currentUserId).Select(p => p.PortfolioId).ToListAsync();
            portfolio = portfolio.Where(p => visibleIds.Contains(p.PortfolioId)).ToList();
            if (!isOwner)
            {
                services = services.Where(x => x.IsActive).ToList();
                portfolio = portfolio.Where(x => !x.IsHidden).ToList();
                foreach (var item in portfolio)
                    item.ImageUrls = item.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList();
                portfolio = portfolio.Where(x => x.ImageUrls.Count > 0).ToList();
            }
            var dto = ToMuaDetailDto(profile, styles, services, portfolio);
            if (isOwner) {
                // Only the authenticated owner path (also used by authorized Admin review)
                // receives account contact data; marketplace detail stays public-safe.
                dto.Email = profile.User?.Email;
                dto.PhoneNumber = profile.User?.PhoneNumber;
                dto.PhoneVerified = profile.User?.PhoneVerified ?? false;
                dto.Latitude = profile.Latitude; dto.Longitude = profile.Longitude;
                dto.OperatingLocationLabel = profile.OperatingLocationLabel;
                dto.WorkLocationName = profile.WorkLocationName;
                dto.WorkLocationAddress = profile.WorkLocationAddress;
                dto.AllowCustomerVisit = profile.AllowCustomerVisit;
            }
            dto.ReviewCount = await _dbContext.Reviews.CountAsync(review => review.MUAId == muaId);
            return dto;
        }

        public Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto) =>
            new UnifiedStyleService(_dbContext).Write(muaId, () => UpdateMuaProfileCore(muaId, updateDto), x => x);

        private async Task<bool> UpdateMuaProfileCore(Guid muaId, MuaUpdateDto updateDto)
        {
            await new UnifiedStyleService(_dbContext).EnsureWritable(muaId);
            var profile = await _muaRepository.GetProfileWithFullDetailsAsync(muaId);
            if (profile == null) return false;
            var updatingWorkLocation = updateDto.WorkLocationAddress != null || updateDto.WorkLocationName != null || updateDto.AllowCustomerVisit.HasValue;
            if (!WorkLocationPolicy.ValidCoordinates(updateDto.Latitude, updateDto.Longitude)) return false;
            if (!updateDto.ClearWorkLocation && updatingWorkLocation && !WorkLocationPolicy.Valid(updateDto.WorkLocationName, updateDto.WorkLocationAddress, updateDto.Latitude, updateDto.Longitude, updateDto.OperatingLocationConfirmed, updateDto.AllowCustomerVisit == true)) return false;

            if (updateDto.Bio != null) profile.Bio = updateDto.Bio;
            if (updateDto.ExperienceYears > 0) profile.ExperienceYears = updateDto.ExperienceYears;
            if (updateDto.PortfolioCoverUrl != null) profile.PortfolioCoverUrl = updateDto.PortfolioCoverUrl;
            if (updateDto.OperatingAreaIds == null && updateDto.ProvinceCode.HasValue && !MuaOperatingAreaCatalog.IsValid(updateDto.ProvinceCode.Value, updateDto.DistrictCode, updateDto.City ?? profile.City ?? "", updateDto.District ?? profile.District)) return false;
            if ((updateDto.City != null && updateDto.City != profile.City) || (updateDto.District != null && updateDto.District != profile.District))
            {
                profile.ProvinceCode = null;
                profile.DistrictCode = null;
            }
            if (updateDto.City != null) profile.City = updateDto.City;
            if (updateDto.District != null) profile.District = updateDto.District;
            if (updateDto.ProvinceCode.HasValue) profile.ProvinceCode = updateDto.ProvinceCode;
            if (updateDto.DistrictCode.HasValue) profile.DistrictCode = updateDto.DistrictCode;
            if (updateDto.OperatingAreaIds != null)
            {
                if (!OperatingAreas.IsValid(updateDto.OperatingProvinceCode, updateDto.OperatingAreaIds)) return false;
                if (profile.OperatingProvinceCode != updateDto.OperatingProvinceCode && profile.WorkLocationAddress == null) {
                    profile.OperatingLocationConfirmed = false; profile.PublicMeetingPoint = false;
                    profile.Latitude = null; profile.Longitude = null; profile.OperatingLocationLabel = null;
                }
                profile.OperatingProvinceCode = updateDto.OperatingProvinceCode;
                profile.City = OperatingAreas.Province(updateDto.OperatingProvinceCode)!.Name;
                var wanted = updateDto.OperatingAreaIds.ToHashSet();
                foreach (var old in profile.OperatingAreas.Where(a => !wanted.Contains(a.AreaId)).ToList()) profile.OperatingAreas.Remove(old);
                foreach (var id in wanted.Where(id => !profile.OperatingAreas.Any(a => a.AreaId == id)))
                    profile.OperatingAreas.Add(new MuaOperatingArea { MuaId = muaId, AreaId = id });
            }
            if (updateDto.ClearWorkLocation) {
                WorkLocationPolicy.Clear(profile);
            } else if (updatingWorkLocation) {
                WorkLocationPolicy.Set(profile, updateDto.WorkLocationName, updateDto.WorkLocationAddress, updateDto.Latitude, updateDto.Longitude, updateDto.OperatingLocationConfirmed, updateDto.AllowCustomerVisit == true);
            } else if (updateDto.ClearOperatingLocation) {
                profile.Latitude = null; profile.Longitude = null; profile.OperatingLocationConfirmed = false;
                profile.AllowCustomerVisit = false;
                profile.PublicMeetingPoint = false; profile.OperatingLocationLabel = null;
            } else if (updateDto.Latitude.HasValue && updateDto.Longitude.HasValue) {
                profile.Latitude = updateDto.Latitude; profile.Longitude = updateDto.Longitude;
                profile.AllowCustomerVisit = false;
                profile.OperatingLocationConfirmed = updateDto.OperatingLocationConfirmed;
                profile.PublicMeetingPoint = updateDto.PublicMeetingPoint;
                profile.OperatingLocationLabel = updateDto.OperatingLocationLabel?.Trim();
            }
            if (updateDto.ExperienceLevel != null) profile.ExperienceLevel = updateDto.ExperienceLevel;
            if (updateDto.Specialization != null) profile.Specialization = updateDto.Specialization;
            if (updateDto.SocialLinks != null) profile.SocialLinks = updateDto.SocialLinks;
            if (updateDto.InstagramUrl != null) profile.InstagramUrl = updateDto.InstagramUrl;
            if (updateDto.FacebookUrl != null) profile.FacebookUrl = updateDto.FacebookUrl;

            await new UnifiedStyleService(_dbContext).ReplaceSpecialties(muaId, updateDto.StyleIds, updateDto.NewStyleNames);

            if (profile.User != null)
            {
                if (updateDto.PhoneNumber != null)
                {
                    if (!string.Equals(profile.User.PhoneNumber, updateDto.PhoneNumber, StringComparison.Ordinal))
                    {
                        profile.User.PhoneNumber = updateDto.PhoneNumber;
                        profile.User.PhoneVerified = false;
                    }
                }
                if (updateDto.AvatarUrl != null)
                {
                    profile.User.AvatarUrl = updateDto.AvatarUrl;
                }
                if (updateDto.DisplayName != null)
                {
                    profile.User.FullName = updateDto.DisplayName;
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> HasMuaProfileAsync(Guid muaId)
        {
            return await _muaRepository.ProfileExistsAsync(muaId);
        }

        public async Task<List<ServiceDto>> GetMuaServicesAsync(Guid muaId, Guid? currentUserId = null)
        {
            var services = await _muaRepository.GetServicesByMuaIdAsync(muaId);
            if (currentUserId != muaId)
            {
                var isPublic = await _dbContext.MakeupArtistProfiles.AnyAsync(x => x.MUAId == muaId
                    && x.Status == Models.Enums.MuaStatus.Listed && x.VerificationStatus == Models.Enums.MuaVerificationStatus.Approved && x.User != null && x.User.IsActive && x.User.DeletedAt == null);
                if (!isPublic) return new List<ServiceDto>();
                services = services.Where(x => x.IsActive).ToList();
            }
            return services.Select(ToServiceDto).ToList();
        }

        public Task<ServiceDto?> AddMuaServiceAsync(Guid muaId, ServiceCreateDto serviceDto) =>
            new UnifiedStyleService(_dbContext).Write(muaId, () => AddMuaServiceCore(muaId, serviceDto), x => x != null);

        private async Task<ServiceDto?> AddMuaServiceCore(Guid muaId, ServiceCreateDto serviceDto)
        {
            await new UnifiedStyleService(_dbContext).EnsureWritable(muaId);
            if (!await _muaRepository.ProfileExistsAsync(muaId)) return null;

            if (serviceDto.RequestId == Guid.Empty) throw new StyleWriteException("STYLE_PAYLOAD_INVALID", "requestId không hợp lệ.");
            var requestHash = UnifiedStyleService.RequestHash(serviceDto);
            if (serviceDto.RequestId.HasValue)
            {
                var previous = await _dbContext.Services.Include(x => x.Styles).ThenInclude(x => x.MakeupStyle)
                    .SingleOrDefaultAsync(x => x.ServiceId == serviceDto.RequestId);
                if (previous != null)
                {
                    if (previous.MUAId != muaId || previous.CreateRequestHash != requestHash)
                        throw new StyleWriteException("SERVICE_REQUEST_CONFLICT", "requestId đã được sử dụng cho yêu cầu khác.", 409);
                    return ToServiceDto(previous);
                }
            }

            var service = new MakeupService
            {
                ServiceId = serviceDto.RequestId ?? Guid.NewGuid(),
                CreateRequestHash = requestHash,
                MUAId = muaId,
                ServiceName = serviceDto.ServiceName,
                Description = serviceDto.Description,
                Price = serviceDto.Price,
                DurationMinutes = serviceDto.DurationMinutes,
                ImageUrl = serviceDto.ImageUrls?.FirstOrDefault() ?? serviceDto.ImageUrl,
                ImageUrls = serviceDto.ImageUrls ?? (string.IsNullOrWhiteSpace(serviceDto.ImageUrl) ? new() : new() { serviceDto.ImageUrl }),
                Tags = serviceDto.Tags ?? new List<string>(),
                IsActive = serviceDto.IsActive
            };

            await _muaRepository.AddServiceAsync(service);
            await new UnifiedStyleService(_dbContext).ApplyService(muaId, service, serviceDto);
            var success = await _unitOfWork.SaveChangesAsync() > 0;
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return ToServiceDto(service);
        }

        public Task<bool> UpdateMuaServiceAsync(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto) =>
            new UnifiedStyleService(_dbContext).Write(muaId, () => UpdateMuaServiceCore(muaId, serviceId, serviceDto), x => x);

        private async Task<bool> UpdateMuaServiceCore(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto)
        {
            await new UnifiedStyleService(_dbContext).EnsureWritable(muaId);
            var service = await _muaRepository.GetServiceByIdForMuaAsync(serviceId, muaId);
            if (service == null) return false;

            service.ServiceName = serviceDto.ServiceName;
            service.Description = serviceDto.Description;
            service.Price = serviceDto.Price;
            service.DurationMinutes = serviceDto.DurationMinutes;
            service.ImageUrl = serviceDto.ImageUrls?.FirstOrDefault() ?? serviceDto.ImageUrl;
            service.ImageUrls = serviceDto.ImageUrls ?? (string.IsNullOrWhiteSpace(serviceDto.ImageUrl) ? new() : new() { serviceDto.ImageUrl });
            await new UnifiedStyleService(_dbContext).ApplyService(muaId, service, serviceDto);
            service.IsActive = serviceDto.IsActive;

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> DeleteMuaServiceAsync(Guid muaId, Guid serviceId)
        {
            var service = await _muaRepository.GetServiceByIdForMuaAsync(serviceId, muaId);
            if (service == null) return false;

            _muaRepository.RemoveService(service);
            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> SetMuaServiceActiveAsync(Guid muaId, Guid serviceId, bool isActive)
        {
            var service = await _muaRepository.GetServiceByIdForMuaAsync(serviceId, muaId);
            if (service == null) return false;
            service.IsActive = isActive;
            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<List<PortfolioDto>> GetMuaPortfolioAsync(Guid muaId, Guid? currentUserId = null)
        {
            var portfolios = await ModerationService.VisiblePortfolios(_dbContext, _dbContext.Portfolios.WithTagging(), currentUserId)
                .Include(p => p.Likes)
                .Include(p => p.Saves)
                .Include(p => p.MakeupArtistProfile)
                .ThenInclude(m => m.User)
                .Include(p => p.Service).ThenInclude(s => s!.Styles).ThenInclude(s => s.MakeupStyle)
                .Where(p => p.MUAId == muaId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var isOwner = currentUserId == muaId;
            if (!isOwner)
            {
                portfolios = portfolios
                    .Where(p => !p.IsHidden && p.MakeupArtistProfile?.Status == Models.Enums.MuaStatus.Listed && p.MakeupArtistProfile.VerificationStatus == Models.Enums.MuaVerificationStatus.Approved && p.MakeupArtistProfile.User?.IsActive == true && !p.MakeupArtistProfile.User.DeletedAt.HasValue)
                    .ToList();
            }

            var commentCounts = await PortfolioCommentVisibility.CountsAsync(_dbContext, portfolios.Select(p => p.PortfolioId), currentUserId);
            return portfolios.Select(p => new PortfolioDto
            {
                PortfolioId = p.PortfolioId, ServiceId = p.ServiceId, Version = p.Version, Tagging = PortfolioTaggingProjection.Tagging(p),
                MUAId = p.MUAId,
                Title = p.Title,
                ImageUrls = isOwner ? p.ImageUrls : p.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(),
                Description = p.Description,
                Tags = p.Tags,
                IsHidden = p.IsHidden,
                IsPinned = p.IsPinned,
                CreatedAt = p.CreatedAt,
                LikesCount = p.Likes.Count,
                CommentsCount = commentCounts.GetValueOrDefault(p.PortfolioId),
                SavesCount = p.Saves.Count,
                IsLiked = currentUserId.HasValue && p.Likes.Any(l => l.UserId == currentUserId.Value),
                IsSaved = currentUserId.HasValue && p.Saves.Any(s => s.UserId == currentUserId.Value),
                AuthorName = p.MakeupArtistProfile?.User?.FullName,
                AuthorAvatarUrl = p.MakeupArtistProfile?.User?.AvatarUrl
                ,Service = p.Service == null ? null : ToServiceDto(p.Service)
            }).Where(p => isOwner || p.ImageUrls.Count > 0).ToList();
        }

        public async Task<bool> AddPortfolioImageAsync(Guid muaId, PortfolioCreateRequest request)
        {
            var success = await new PortfolioTaggingService(_dbContext).Save(muaId, null, request);
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return success;
        }

        public async Task<bool> UpdatePortfolioImageAsync(Guid muaId, Guid portfolioId, PortfolioCreateRequest request)
        {
            var success = await new PortfolioTaggingService(_dbContext).Save(muaId, portfolioId, request);
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return success;
        }

        public async Task<bool> SetPortfolioVisibilityAsync(Guid muaId, Guid portfolioId, bool isHidden)
        {
            var success = await new PortfolioTaggingService(_dbContext).ChangeState(muaId, portfolioId, p => p.IsHidden = isHidden, !isHidden);
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return success;
        }

        public Task<bool> TogglePortfolioPinAsync(Guid muaId, Guid portfolioId) =>
            new PortfolioTaggingService(_dbContext).ChangeState(muaId, portfolioId, p => p.IsPinned = !p.IsPinned);

        public async Task<bool> DeletePortfolioAsync(Guid muaId, Guid portfolioId)
        {
            var success = await new PortfolioTaggingService(_dbContext).ChangeState(muaId, portfolioId, p => _dbContext.Portfolios.Remove(p), false);
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return success;
        }
        // Portfolio Interactions
        public async Task<bool> TogglePortfolioLikeAsync(Guid userId, Guid portfolioId)
        {
            await using var scope = await ModerationWriteScope.Start(_dbContext, userId);
        await new PlayReviewPolicy(_dbContext).EnsurePortfolioDomainAsync(userId, portfolioId);
            await new ModerationService(_dbContext).EnsureContent("Portfolio", portfolioId);
            var owner = await _dbContext.Portfolios.Where(p => p.PortfolioId == portfolioId).Select(p => p.MUAId).SingleAsync();
            await new ModerationService(_dbContext).EnsureInteraction(userId, owner);
            if (!await _dbContext.Portfolios.AnyAsync(p => p.PortfolioId == portfolioId)) return false;
            var existingLike = await _dbContext.PortfolioLikes
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PortfolioId == portfolioId);

            if (existingLike != null)
            {
                _dbContext.PortfolioLikes.Remove(existingLike);
            }
            else
            {
                _dbContext.PortfolioLikes.Add(new PortfolioLike
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PortfolioId = portfolioId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var saved = await _dbContext.SaveChangesAsync();
            await scope.Commit();
            return saved > 0;
        }

        public async Task<bool> TogglePortfolioSaveAsync(Guid userId, Guid portfolioId)
        {
            await using var scope = await ModerationWriteScope.Start(_dbContext, userId);
        await new PlayReviewPolicy(_dbContext).EnsurePortfolioDomainAsync(userId, portfolioId);
            await new ModerationService(_dbContext).EnsureContent("Portfolio", portfolioId);
            var owner = await _dbContext.Portfolios.Where(p => p.PortfolioId == portfolioId).Select(p => p.MUAId).SingleAsync();
            await new ModerationService(_dbContext).EnsureInteraction(userId, owner);
            if (!await _dbContext.Portfolios.AnyAsync(p => p.PortfolioId == portfolioId)) return false;
            var existingSave = await _dbContext.PortfolioSaves
                .FirstOrDefaultAsync(s => s.UserId == userId && s.PortfolioId == portfolioId);

            if (existingSave != null)
            {
                _dbContext.PortfolioSaves.Remove(existingSave);
            }
            else
            {
                _dbContext.PortfolioSaves.Add(new PortfolioSave
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PortfolioId = portfolioId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var saved = await _dbContext.SaveChangesAsync();
            await scope.Commit();
            return saved > 0;
        }

        public async Task<PortfolioCommentDto?> AddPortfolioCommentAsync(Guid userId, Guid portfolioId, string content)
        {
            await using var scope = await ModerationWriteScope.Start(_dbContext, userId);
        await new PlayReviewPolicy(_dbContext).EnsurePortfolioDomainAsync(userId, portfolioId);
            await new ModerationService(_dbContext).EnsureContent("Portfolio", portfolioId);
            var owner = await _dbContext.Portfolios.Where(p => p.PortfolioId == portfolioId).Select(p => p.MUAId).SingleAsync();
            await new ModerationService(_dbContext).EnsureInteraction(userId, owner);
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !await _dbContext.Portfolios.AnyAsync(p => p.PortfolioId == portfolioId)) return null;

            var comment = new PortfolioComment
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PortfolioId = portfolioId,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PortfolioComments.Add(comment);
            await _dbContext.SaveChangesAsync();
            await scope.Commit();

            return new PortfolioCommentDto
            {
                Id = comment.Id,
                PortfolioId = comment.PortfolioId,
                UserId = comment.UserId,
                UserName = user.FullName,
                UserAvatarUrl = user.AvatarUrl,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt
            };
        }

        public async Task<List<PortfolioCommentDto>> GetPortfolioCommentsAsync(Guid portfolioId, Guid? viewer = null)
        {
            if (!await ModerationService.VisiblePortfolios(_dbContext, _dbContext.Portfolios.Where(p => p.PortfolioId == portfolioId), viewer).AnyAsync()) return new();
            var comments = await PortfolioCommentVisibility.ReadAsync(_dbContext, [portfolioId], viewer);
            return comments.Where(c => !c.ParentCommentId.HasValue).ToList();
        }

        public async Task<PortfolioCommentDto?> ReplyToPortfolioCommentAsync(Guid userId, Guid portfolioId, Guid parentCommentId, string content)
        {
            await using var scope = await ModerationWriteScope.Start(_dbContext, userId);
        await new PlayReviewPolicy(_dbContext).EnsurePortfolioDomainAsync(userId, portfolioId);
            await new ModerationService(_dbContext).EnsureContent("Portfolio", portfolioId);
            var owner = await _dbContext.Portfolios.Where(p => p.PortfolioId == portfolioId).Select(p => p.MUAId).SingleAsync();
            await new ModerationService(_dbContext).EnsureInteraction(userId, owner);
            var parent = await _dbContext.PortfolioComments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentCommentId && c.PortfolioId == portfolioId);
            if (parent == null) return null;
            await new ModerationService(_dbContext).EnsureContent("Comment", parentCommentId);
            await new ModerationService(_dbContext).EnsureInteraction(userId, parent.UserId);
            await new PlayReviewPolicy(_dbContext).EnsureSameDomainAsync(userId, parent.UserId);
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;
            var reply = new PortfolioComment { UserId = userId, PortfolioId = portfolioId, ParentCommentId = parentCommentId, Content = content };
            _dbContext.PortfolioComments.Add(reply);
            await _dbContext.SaveChangesAsync();
            await scope.Commit();
            return new PortfolioCommentDto { Id = reply.Id, PortfolioId = portfolioId, UserId = userId, ParentCommentId = parentCommentId, UserName = user.FullName, UserAvatarUrl = user.AvatarUrl, Content = content, CreatedAt = reply.CreatedAt };
        }

        public async Task<List<PortfolioDto>> GetFavoritePortfolioAsync(Guid userId, string type)
        {
            var query = _dbContext.Portfolios.WithTagging()
                .Include(p => p.Likes).Include(p => p.Saves)
                .Include(p => p.MakeupArtistProfile).ThenInclude(m => m.User)
                .Include(p => p.Service).ThenInclude(s => s!.Styles).ThenInclude(s => s.MakeupStyle).AsQueryable();
            query = ModerationService.VisiblePortfolios(_dbContext, query, userId);
            query = query.Where(p => !p.IsHidden
                && p.MakeupArtistProfile != null
                && p.MakeupArtistProfile.Status == Models.Enums.MuaStatus.Listed
                && p.MakeupArtistProfile.VerificationStatus == Models.Enums.MuaVerificationStatus.Approved
                && p.MakeupArtistProfile.User != null
                && p.MakeupArtistProfile.User.IsActive
                && p.MakeupArtistProfile.User.DeletedAt == null);
            query = type == "liked" ? query.Where(p => p.Likes.Any(x => x.UserId == userId)) : query.Where(p => p.Saves.Any(x => x.UserId == userId));
            var posts = await query.OrderByDescending(p => type == "liked" ? p.Likes.First(x => x.UserId == userId).CreatedAt : p.Saves.First(x => x.UserId == userId).CreatedAt).ToListAsync();
            var commentCounts = await PortfolioCommentVisibility.CountsAsync(_dbContext, posts.Select(p => p.PortfolioId), userId);
            return posts.Select(p => new PortfolioDto { PortfolioId = p.PortfolioId, ServiceId = p.ServiceId, Version = p.Version, Tagging = PortfolioTaggingProjection.Tagging(p), MUAId = p.MUAId, Title = p.Title, ImageUrls = p.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(), Description = p.Description, Tags = p.Tags, CreatedAt = p.CreatedAt, LikesCount = p.Likes.Count, CommentsCount = commentCounts.GetValueOrDefault(p.PortfolioId), SavesCount = p.Saves.Count, IsLiked = p.Likes.Any(x => x.UserId == userId), IsSaved = p.Saves.Any(x => x.UserId == userId), AuthorName = p.MakeupArtistProfile?.User?.FullName, AuthorAvatarUrl = p.MakeupArtistProfile?.User?.AvatarUrl, Service = p.Service == null ? null : ToServiceDto(p.Service) }).Where(p => p.ImageUrls.Count > 0).ToList();
        }

        public Task<bool> UpdateStylesAsync(Guid muaId, List<int> styleIds) =>
            new UnifiedStyleService(_dbContext).Write(muaId, async () => {
                var styles = new UnifiedStyleService(_dbContext);
                await styles.EnsureWritable(muaId);
                await styles.ReplaceSpecialties(muaId, styleIds, null);
                await _unitOfWork.SaveChangesAsync();
                await _eligibilityService.EvaluateAsync(muaId);
                return true;
            });

        public async Task<List<MakeupStyleDto>> GetAllStylesAsync()
        {
            var styles = await _muaRepository.GetAllStylesAsync();
            return styles.Select(ToMakeupStyleDto).ToList();
        }

        public async Task<MakeupStyleDto?> CreateStyleAsync(CreateMakeupStyleRequest request)
        {
            var actorClaim = _httpContextAccessor?.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(actorClaim, out var actor)) throw new StyleWriteException("STYLE_AUTH_REQUIRED", "Vui lòng đăng nhập.", 401);
            var styles = new UnifiedStyleService(_dbContext);
            return await styles.Write(actor, async () => {
                await styles.EnsureWritable(actor, requireProfile: false);
                var style = await styles.ResolveNewName(actor, request.Name, request.Description);
                return ToMakeupStyleDto(style);
            });
        }

        private MuaProfileDto ToMuaProfileDto(MakeupArtistProfile profile, List<string> styles)
        {
            return new MuaProfileDto
            {
                MUAId = profile.MUAId,
                Bio = profile.Bio,
                ExperienceYears = profile.ExperienceYears,
                AverageRating = profile.AverageRating,
                TotalBookings = profile.TotalBookings,
                PortfolioCoverUrl = profile.PortfolioCoverUrl,
                FullName = profile.User?.FullName,
                AvatarUrl = profile.User?.AvatarUrl,
                City = profile.City,
                District = profile.District,
                ProvinceCode = profile.ProvinceCode,
                DistrictCode = profile.DistrictCode,
                OperatingProvinceCode = profile.OperatingProvinceCode ?? OperatingAreas.FromLegacyProvince(profile.ProvinceCode),
                OperatingAreaIds = profile.OperatingAreas.Count > 0 ? profile.OperatingAreas.Select(a => a.AreaId).ToList() : profile.DistrictCode.HasValue ? new List<string> { $"legacy-district:{profile.DistrictCode}" } : new(),
                OperatingLocationConfirmed = profile.OperatingLocationConfirmed,
                PublicMeetingPoint = WorkLocationPolicy.CanVisit(profile),
                AllowCustomerVisit = WorkLocationPolicy.CanVisit(profile),
                WorkLocationName = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationName : null,
                WorkLocationAddress = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationAddress : null,
                OperatingLocationLabel = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationName : null,
                Latitude = WorkLocationPolicy.CanVisit(profile) && profile.OperatingLocationConfirmed ? profile.Latitude : null,
                Longitude = WorkLocationPolicy.CanVisit(profile) && profile.OperatingLocationConfirmed ? profile.Longitude : null,
                ExperienceLevel = profile.ExperienceLevel,
                Specialization = profile.Specialization,
                SocialLinks = profile.SocialLinks,
                InstagramUrl = profile.InstagramUrl,
                FacebookUrl = profile.FacebookUrl,
                Styles = styles,
                Specialties = GetSpecialtyDtos(profile.MUAId),
                Status = profile.Status.ToString(),
                RankScore = profile.RankScore,
                ListedAt = profile.ListedAt,
                LastActiveAt = profile.LastActiveAt,
                VerificationStatus = profile.VerificationStatus.ToString()
            };
        }

        private MuaDetailDto ToMuaDetailDto(
            MakeupArtistProfile profile,
            List<string> styles,
            List<MakeupService> services,
            List<Portfolio> portfolio)
        {
            var dto = new MuaDetailDto
            {
                MUAId = profile.MUAId,
                Bio = profile.Bio,
                ExperienceYears = profile.ExperienceYears,
                AverageRating = profile.AverageRating,
                TotalBookings = profile.TotalBookings,
                PortfolioCoverUrl = profile.PortfolioCoverUrl,
                FullName = profile.User?.FullName,
                AvatarUrl = profile.User?.AvatarUrl,
                City = profile.City,
                District = profile.District,
                ProvinceCode = profile.ProvinceCode,
                DistrictCode = profile.DistrictCode,
                OperatingProvinceCode = profile.OperatingProvinceCode ?? OperatingAreas.FromLegacyProvince(profile.ProvinceCode),
                OperatingAreaIds = profile.OperatingAreas.Count > 0 ? profile.OperatingAreas.Select(a => a.AreaId).ToList() : profile.DistrictCode.HasValue ? new List<string> { $"legacy-district:{profile.DistrictCode}" } : new(),
                OperatingLocationConfirmed = profile.OperatingLocationConfirmed,
                PublicMeetingPoint = WorkLocationPolicy.CanVisit(profile),
                AllowCustomerVisit = WorkLocationPolicy.CanVisit(profile),
                WorkLocationName = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationName : null,
                WorkLocationAddress = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationAddress : null,
                OperatingLocationLabel = WorkLocationPolicy.CanVisit(profile) ? profile.WorkLocationName : null,
                Latitude = WorkLocationPolicy.CanVisit(profile) && profile.OperatingLocationConfirmed ? profile.Latitude : null,
                Longitude = WorkLocationPolicy.CanVisit(profile) && profile.OperatingLocationConfirmed ? profile.Longitude : null,
                ExperienceLevel = profile.ExperienceLevel,
                Specialization = profile.Specialization,
                SocialLinks = profile.SocialLinks,
                InstagramUrl = profile.InstagramUrl,
                FacebookUrl = profile.FacebookUrl,
                Styles = styles,
                Specialties = GetSpecialtyDtos(profile.MUAId),
                MinPrice = services.Any() ? services.Min(s => s.Price) : null,
                Status = profile.Status.ToString(),
                RankScore = profile.RankScore,
                ListedAt = profile.ListedAt,
                LastActiveAt = profile.LastActiveAt,
                VerificationStatus = profile.VerificationStatus.ToString(),
                Services = services.Select(ToServiceDto).ToList(),
                Portfolio = portfolio.Select(ToPortfolioDto).ToList()
            };

            return dto;
        }

        private ServiceDto ToServiceDto(MakeupService service)
        {
            if (_dbContext.Entry(service).State != EntityState.Detached && !_dbContext.Entry(service).Collection(x => x.Styles).IsLoaded)
                _dbContext.Entry(service).Collection(x => x.Styles).Query().Include(x => x.MakeupStyle).Load();
            return new ServiceDto
            {
                ServiceId = service.ServiceId,
                MUAId = service.MUAId,
                ServiceName = service.ServiceName,
                Description = service.Description,
                Price = service.Price,
                DurationMinutes = service.DurationMinutes,
                ImageUrl = service.ImageUrl,
                ImageUrls = service.ImageUrls.Count > 0 ? service.ImageUrls : (string.IsNullOrWhiteSpace(service.ImageUrl) ? new() : new() { service.ImageUrl }),
                Tags = UnifiedStyleService.LegacyTags(service),
                StyleIds = service.Styles.Select(x => x.StyleId).OrderBy(x => x).ToList(),
                StyleTags = UnifiedStyleService.StyleTags(service),
                IsActive = service.IsActive
            };
        }

        private static PortfolioDto ToPortfolioDto(Portfolio portfolio)
        {
            return new PortfolioDto
            {
                PortfolioId = portfolio.PortfolioId, ServiceId = portfolio.ServiceId, Version = portfolio.Version, Tagging = PortfolioTaggingProjection.Tagging(portfolio),
                MUAId = portfolio.MUAId,
                Title = portfolio.Title,
                ImageUrls = portfolio.ImageUrls,
                Description = portfolio.Description,
                Tags = portfolio.Tags ?? new List<string>(),
                IsHidden = portfolio.IsHidden,
                IsPinned = portfolio.IsPinned,
                CreatedAt = portfolio.CreatedAt
            };
        }

        private static MakeupStyleDto ToMakeupStyleDto(MakeupStyle style)
        {
            return new MakeupStyleDto
            {
                StyleId = style.StyleId,
                Name = style.Name,
                Description = style.Description
                ,IsActive = style.IsActive
            };
        }

        private List<MakeupStyleDto> GetSpecialtyDtos(Guid muaId)
        {
            return _dbContext.MUAStyles
                .Where(link => link.MUAId == muaId && link.MakeupStyle != null)
                .Select(link => new MakeupStyleDto
                {
                    StyleId = link.StyleId,
                    Name = link.MakeupStyle!.Name,
                    Description = link.MakeupStyle.Description,
                    IsActive = link.MakeupStyle.IsActive
                })
                .OrderBy(style => style.Name)
                .ToList();
        }

        public async Task RecalculateProfileStateAsync(Guid muaId)
        {
            await _eligibilityService.EvaluateAsync(muaId);
        }

        public async Task RecalculateProfileQualityScoreAsync(Guid muaId)
        {
            var profile = await _dbContext.Set<MakeupArtistProfile>()
                .Include(p => p.User)
                .Include(p => p.Services)
                .Include(p => p.Portfolios)
                .FirstOrDefaultAsync(p => p.MUAId == muaId);

            if (profile == null) return;

            await _eligibilityService.EvaluateAsync(muaId);
        }
    }
}
