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

        public MuaService(IMuaRepository muaRepository, IUserRepository userRepository, IUnitOfWork unitOfWork, BeautyBookBackend.Data.ApplicationDbContext dbContext, IMuaEligibilityService eligibilityService)
        {
            _muaRepository = muaRepository;
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _dbContext = dbContext;
            _eligibilityService = eligibilityService;
        }

        public async Task<List<MuaProfileDto>> GetMuasAsync(int page)
        {
            var pageSize = 20; // Hardcoded MVP page size
            var profiles = await _muaRepository.GetProfilesAsync(page, pageSize);
            var result = new List<MuaProfileDto>();

            foreach (var profile in profiles)
            {
                var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(profile.MUAId);
                result.Add(await ToMuaProfileDtoWithPriceAsync(profile, styles));
            }

            return result;
        }

        public async Task<MuaProfileDto?> ApplyMuaAsync(Guid muaId, MuaApplicationRequestDto request)
        {
            var user = await _userRepository.GetByIdAsync(muaId);
            if (user == null) return null;

            user.FullName = request.DisplayName;
            if (!string.Equals(user.PhoneNumber, request.PhoneNumber, StringComparison.Ordinal))
            {
                user.PhoneNumber = request.PhoneNumber;
                user.PhoneVerified = false;
            }
            
            var profile = await _muaRepository.GetProfileWithFullDetailsAsync(muaId);
            
            if (profile == null)
            {
                profile = new MakeupArtistProfile
                {
                    MUAId = muaId,
                    Bio = request.Bio,
                    ExperienceYears = request.ExperienceYears ?? 0,
                    City = request.City,
                    Specialization = request.Specialization,
                    SocialLinks = request.SocialLinks,
                    AverageRating = 0,
                    TotalBookings = 0,
                    Status = Models.Enums.MuaStatus.Draft
                };
                await _muaRepository.AddProfileAsync(profile);
            }
            else
            {
                profile.Bio = request.Bio;
                profile.ExperienceYears = request.ExperienceYears ?? profile.ExperienceYears;
                profile.City = request.City;
                profile.Specialization = request.Specialization;
                profile.SocialLinks = request.SocialLinks;
            }

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);

            var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(muaId);
            return ToMuaProfileDto(profile, styles);
        }

        public async Task<MuaDetailDto?> GetMuaByIdAsync(Guid muaId, Guid? currentUserId = null)
        {
            var profile = await _muaRepository.GetProfileWithFullDetailsAsync(muaId);
            if (profile == null) return null;
            var isOwner = currentUserId == muaId;
            if (!isOwner && (profile.Status != Models.Enums.MuaStatus.Listed || profile.User?.IsActive != true || profile.User.DeletedAt.HasValue)) return null;

            var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(muaId);
            var services = await _muaRepository.GetServicesByMuaIdAsync(muaId);
            var portfolio = await _muaRepository.GetPortfolioByMuaIdAsync(muaId);
            if (!isOwner)
            {
                services = services.Where(x => x.IsActive).ToList();
                portfolio = portfolio.Where(x => !x.IsHidden).ToList();
                foreach (var item in portfolio)
                    item.ImageUrls = item.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList();
                portfolio = portfolio.Where(x => x.ImageUrls.Count > 0).ToList();
            }
            return ToMuaDetailDto(profile, styles, services, portfolio);
        }

        public async Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto)
        {
            var profile = await _muaRepository.GetProfileWithFullDetailsAsync(muaId);
            if (profile == null) return false;

            if (updateDto.Bio != null) profile.Bio = updateDto.Bio;
            if (updateDto.ExperienceYears > 0) profile.ExperienceYears = updateDto.ExperienceYears;
            if (updateDto.PortfolioCoverUrl != null) profile.PortfolioCoverUrl = updateDto.PortfolioCoverUrl;
            if (updateDto.City != null) profile.City = updateDto.City;
            if (updateDto.Specialization != null) profile.Specialization = updateDto.Specialization;
            if (updateDto.SocialLinks != null) profile.SocialLinks = updateDto.SocialLinks;

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
                    && x.Status == Models.Enums.MuaStatus.Listed && x.User != null && x.User.IsActive && x.User.DeletedAt == null);
                if (!isPublic) return new List<ServiceDto>();
                services = services.Where(x => x.IsActive).ToList();
            }
            return services.Select(ToServiceDto).ToList();
        }

        public async Task<ServiceDto?> AddMuaServiceAsync(Guid muaId, ServiceCreateDto serviceDto)
        {
            if (!await _muaRepository.ProfileExistsAsync(muaId)) return null;

            var service = new MakeupService
            {
                ServiceId = Guid.NewGuid(),
                MUAId = muaId,
                ServiceName = serviceDto.ServiceName,
                Description = serviceDto.Description,
                Price = serviceDto.Price,
                DurationMinutes = serviceDto.DurationMinutes,
                ImageUrl = serviceDto.ImageUrl,
                Tags = serviceDto.Tags ?? new List<string>(),
                IsActive = serviceDto.IsActive
            };

            await _muaRepository.AddServiceAsync(service);
            var success = await _unitOfWork.SaveChangesAsync() > 0;
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return ToServiceDto(service);
        }

        public async Task<bool> UpdateMuaServiceAsync(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto)
        {
            var service = await _muaRepository.GetServiceByIdForMuaAsync(serviceId, muaId);
            if (service == null) return false;

            service.ServiceName = serviceDto.ServiceName;
            service.Description = serviceDto.Description;
            service.Price = serviceDto.Price;
            service.DurationMinutes = serviceDto.DurationMinutes;
            service.ImageUrl = serviceDto.ImageUrl;
            service.Tags = serviceDto.Tags ?? new List<string>();
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
            var portfolios = await _dbContext.Portfolios
                .Include(p => p.Likes)
                .Include(p => p.Saves)
                .Include(p => p.Comments)
                .Include(p => p.MakeupArtistProfile)
                .ThenInclude(m => m.User)
                .Include(p => p.Service)
                .Where(p => p.MUAId == muaId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var isOwner = currentUserId == muaId;
            if (!isOwner)
            {
                portfolios = portfolios
                    .Where(p => !p.IsHidden && p.MakeupArtistProfile?.Status == Models.Enums.MuaStatus.Listed && p.MakeupArtistProfile.User?.IsActive == true && !p.MakeupArtistProfile.User.DeletedAt.HasValue)
                    .ToList();
            }

            return portfolios.Select(p => new PortfolioDto
            {
                PortfolioId = p.PortfolioId,
                MUAId = p.MUAId,
                Title = p.Title,
                ImageUrls = isOwner ? p.ImageUrls : p.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(),
                Description = p.Description,
                Tags = p.Tags,
                IsHidden = p.IsHidden,
                IsPinned = p.IsPinned,
                CreatedAt = p.CreatedAt,
                LikesCount = p.Likes.Count,
                CommentsCount = p.Comments.Count,
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
            if (!await _muaRepository.ProfileExistsAsync(muaId)) return false;
            if (request.ServiceId.HasValue && !await _dbContext.Services.AnyAsync(s => s.ServiceId == request.ServiceId && s.MUAId == muaId)) return false;

            await _muaRepository.AddPortfolioAsync(new Portfolio
            {
                PortfolioId = Guid.NewGuid(),
                MUAId = muaId,
                Title = request.Title,
                ImageUrls = request.ImageUrls,
                Description = request.Description,
                Tags = request.Tags ?? new List<string>(),
                IsHidden = false,
                IsPinned = false,
                CreatedAt = DateTime.UtcNow
                ,ServiceId = request.ServiceId
            });

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> UpdatePortfolioImageAsync(Guid muaId, Guid portfolioId, PortfolioCreateRequest request)
        {
            var portfolio = await _muaRepository.GetPortfolioByIdForMuaAsync(portfolioId, muaId);
            if (portfolio == null) return false;
            if (request.ServiceId.HasValue && !await _dbContext.Services.AnyAsync(s => s.ServiceId == request.ServiceId && s.MUAId == muaId)) return false;

            portfolio.Title = request.Title;
            portfolio.ImageUrls = request.ImageUrls;
            portfolio.Description = request.Description;
            portfolio.Tags = request.Tags ?? new List<string>();
            portfolio.ServiceId = request.ServiceId;

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> SetPortfolioVisibilityAsync(Guid muaId, Guid portfolioId, bool isHidden)
        {
            var portfolio = await _muaRepository.GetPortfolioByIdForMuaAsync(portfolioId, muaId);
            if (portfolio == null) return false;

            portfolio.IsHidden = isHidden;
            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<bool> TogglePortfolioPinAsync(Guid muaId, Guid portfolioId)
        {
            var portfolio = await _muaRepository.GetPortfolioByIdForMuaAsync(portfolioId, muaId);
            if (portfolio == null) return false;

            portfolio.IsPinned = !portfolio.IsPinned;
            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeletePortfolioAsync(Guid muaId, Guid portfolioId)
        {
            var portfolio = await _muaRepository.GetPortfolioByIdForMuaAsync(portfolioId, muaId);
            if (portfolio == null) return false;

            _muaRepository.RemovePortfolio(portfolio);
            var success = await _unitOfWork.SaveChangesAsync() > 0;
            if (success) await _eligibilityService.EvaluateAsync(muaId);
            return success;
        }

        // Portfolio Interactions
        public async Task<bool> TogglePortfolioLikeAsync(Guid userId, Guid portfolioId)
        {
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

            return await _dbContext.SaveChangesAsync() > 0;
        }

        public async Task<bool> TogglePortfolioSaveAsync(Guid userId, Guid portfolioId)
        {
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

            return await _dbContext.SaveChangesAsync() > 0;
        }

        public async Task<PortfolioCommentDto?> AddPortfolioCommentAsync(Guid userId, Guid portfolioId, string content)
        {
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

        public async Task<List<PortfolioCommentDto>> GetPortfolioCommentsAsync(Guid portfolioId)
        {
            var comments = await _dbContext.PortfolioComments
                .Where(c => c.PortfolioId == portfolioId)
                .OrderByDescending(c => c.CreatedAt)
                .Join(_dbContext.Users,
                      c => c.UserId,
                      u => u.UserId,
                      (c, u) => new PortfolioCommentDto
                      {
                          Id = c.Id,
                          PortfolioId = c.PortfolioId,
                          UserId = c.UserId,
                          ParentCommentId = c.ParentCommentId,
                          UserName = u.FullName,
                          UserAvatarUrl = u.AvatarUrl,
                          Content = c.Content,
                          CreatedAt = c.CreatedAt
                      })
                .ToListAsync();

            var byId = comments.ToDictionary(c => c.Id);
            foreach (var reply in comments.Where(c => c.ParentCommentId.HasValue))
                if (byId.TryGetValue(reply.ParentCommentId!.Value, out var parent)) parent.Replies.Add(reply);
            return comments.Where(c => !c.ParentCommentId.HasValue).ToList();
        }

        public async Task<PortfolioCommentDto?> ReplyToPortfolioCommentAsync(Guid userId, Guid portfolioId, Guid parentCommentId, string content)
        {
            var parentExists = await _dbContext.PortfolioComments.AnyAsync(c => c.Id == parentCommentId && c.PortfolioId == portfolioId);
            if (!parentExists) return null;
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;
            var reply = new PortfolioComment { UserId = userId, PortfolioId = portfolioId, ParentCommentId = parentCommentId, Content = content };
            _dbContext.PortfolioComments.Add(reply);
            await _dbContext.SaveChangesAsync();
            return new PortfolioCommentDto { Id = reply.Id, PortfolioId = portfolioId, UserId = userId, ParentCommentId = parentCommentId, UserName = user.FullName, UserAvatarUrl = user.AvatarUrl, Content = content, CreatedAt = reply.CreatedAt };
        }

        public async Task<List<PortfolioDto>> GetFavoritePortfolioAsync(Guid userId, string type)
        {
            var query = _dbContext.Portfolios
                .Include(p => p.Likes).Include(p => p.Saves).Include(p => p.Comments)
                .Include(p => p.MakeupArtistProfile).ThenInclude(m => m.User)
                .Include(p => p.Service).AsQueryable();
            query = query.Where(p => !p.IsHidden
                && p.MakeupArtistProfile != null
                && p.MakeupArtistProfile.Status == Models.Enums.MuaStatus.Listed
                && p.MakeupArtistProfile.User != null
                && p.MakeupArtistProfile.User.IsActive
                && p.MakeupArtistProfile.User.DeletedAt == null);
            query = type == "liked" ? query.Where(p => p.Likes.Any(x => x.UserId == userId)) : query.Where(p => p.Saves.Any(x => x.UserId == userId));
            var posts = await query.OrderByDescending(p => type == "liked" ? p.Likes.First(x => x.UserId == userId).CreatedAt : p.Saves.First(x => x.UserId == userId).CreatedAt).ToListAsync();
            return posts.Select(p => new PortfolioDto { PortfolioId = p.PortfolioId, MUAId = p.MUAId, Title = p.Title, ImageUrls = p.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(), Description = p.Description, Tags = p.Tags, CreatedAt = p.CreatedAt, LikesCount = p.Likes.Count, CommentsCount = p.Comments.Count, SavesCount = p.Saves.Count, IsLiked = p.Likes.Any(x => x.UserId == userId), IsSaved = p.Saves.Any(x => x.UserId == userId), AuthorName = p.MakeupArtistProfile?.User?.FullName, AuthorAvatarUrl = p.MakeupArtistProfile?.User?.AvatarUrl, Service = p.Service == null ? null : ToServiceDto(p.Service) }).Where(p => p.ImageUrls.Count > 0).ToList();
        }

        public async Task<bool> UpdateStylesAsync(Guid muaId, List<int> styleIds)
        {
            if (!await _muaRepository.ProfileExistsAsync(muaId)) return false;

            var oldStyles = await _muaRepository.GetStyleLinksByMuaIdAsync(muaId);
            _muaRepository.RemoveStyleLinks(oldStyles);

            foreach (var styleId in styleIds)
            {
                if (await _muaRepository.StyleExistsAsync(styleId))
                {
                    await _muaRepository.AddMuaStyleAsync(new MUAStyle
                    {
                        MUAId = muaId,
                        StyleId = styleId
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await _eligibilityService.EvaluateAsync(muaId);
            return true;
        }

        public async Task<List<MakeupStyleDto>> GetAllStylesAsync()
        {
            var styles = await _muaRepository.GetAllStylesAsync();
            return styles.Select(ToMakeupStyleDto).ToList();
        }

        private static MuaProfileDto ToMuaProfileDto(MakeupArtistProfile profile, List<string> styles)
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
                Email = profile.User?.Email,
                AvatarUrl = profile.User?.AvatarUrl,
                PhoneNumber = profile.User?.PhoneNumber,
                PhoneVerified = profile.User?.PhoneVerified ?? false,
                City = profile.City,
                Specialization = profile.Specialization,
                SocialLinks = profile.SocialLinks,
                Styles = styles,
                Status = profile.Status.ToString(),
                RankScore = profile.RankScore,
                ListedAt = profile.ListedAt,
                LastActiveAt = profile.LastActiveAt,
                VerificationStatus = "NOT_SUBMITTED"
            };
        }

        private async Task<MuaProfileDto> ToMuaProfileDtoWithPriceAsync(MakeupArtistProfile profile, List<string> styles)
        {
            var dto = ToMuaProfileDto(profile, styles);
            dto.MinPrice = await _muaRepository.GetMinPriceByMuaIdAsync(profile.MUAId);
            return dto;
        }

        private static MuaDetailDto ToMuaDetailDto(
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
                Email = profile.User?.Email,
                AvatarUrl = profile.User?.AvatarUrl,
                PhoneNumber = profile.User?.PhoneNumber,
                PhoneVerified = profile.User?.PhoneVerified ?? false,
                City = profile.City,
                Specialization = profile.Specialization,
                SocialLinks = profile.SocialLinks,
                Styles = styles,
                MinPrice = services.Any() ? services.Min(s => s.Price) : null,
                Status = profile.Status.ToString(),
                RankScore = profile.RankScore,
                ListedAt = profile.ListedAt,
                LastActiveAt = profile.LastActiveAt,
                VerificationStatus = "NOT_SUBMITTED",
                Services = services.Select(ToServiceDto).ToList(),
                Portfolio = portfolio.Select(ToPortfolioDto).ToList()
            };

            return dto;
        }

        private static ServiceDto ToServiceDto(MakeupService service)
        {
            return new ServiceDto
            {
                ServiceId = service.ServiceId,
                MUAId = service.MUAId,
                ServiceName = service.ServiceName,
                Description = service.Description,
                Price = service.Price,
                DurationMinutes = service.DurationMinutes,
                ImageUrl = service.ImageUrl,
                Tags = service.Tags ?? new List<string>(),
                IsActive = service.IsActive
            };
        }

        private static PortfolioDto ToPortfolioDto(Portfolio portfolio)
        {
            return new PortfolioDto
            {
                PortfolioId = portfolio.PortfolioId,
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
            };
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
