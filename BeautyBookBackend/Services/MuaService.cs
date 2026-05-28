using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services
{
    public class MuaService : IMuaService
    {
        private readonly ApplicationDbContext _context;

        public MuaService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<MuaProfileDto>> GetMuasAsync(MuaFilterDto filter)
        {
            var query = _context.MakeupArtistProfiles
                .Include(m => m.User)
                .AsQueryable();

            // 1. Lọc theo Style/Tone
            if (filter.StyleId.HasValue)
            {
                var muaIdsWithStyle = await _context.MUAStyles
                    .Where(ms => ms.StyleId == filter.StyleId.Value)
                    .Select(ms => ms.MUAId)
                    .ToListAsync();
                query = query.Where(m => muaIdsWithStyle.Contains(m.MUAId));
            }

            // 2. Lọc theo ngân sách (Dựa trên giá các dịch vụ của MUA)
            if (filter.PriceMin.HasValue)
            {
                query = query.Where(m => _context.Services.Any(s => s.MUAId == m.MUAId && s.Price >= filter.PriceMin.Value));
            }
            if (filter.PriceMax.HasValue)
            {
                query = query.Where(m => _context.Services.Any(s => s.MUAId == m.MUAId && s.Price <= filter.PriceMax.Value));
            }

            // 3. Tìm kiếm theo từ khóa tên hoặc bio
            if (!string.IsNullOrEmpty(filter.SearchKeyword))
            {
                var keyword = filter.SearchKeyword.ToLower();
                query = query.Where(m => (m.User != null && m.User.FullName != null && m.User.FullName.ToLower().Contains(keyword)) ||
                                         (m.Bio != null && m.Bio.ToLower().Contains(keyword)));
            }

            // 4. Sắp xếp kết quả
            if (!string.IsNullOrEmpty(filter.SortBy))
            {
                switch (filter.SortBy.ToLower())
                {
                    case "rating":
                        query = query.OrderByDescending(m => m.RatingAverage);
                        break;
                    case "bookings":
                        query = query.OrderByDescending(m => m.TotalBookings);
                        break;
                    case "price_asc":
                        // Sắp xếp theo giá dịch vụ thấp nhất tăng dần
                        query = query.OrderBy(m => _context.Services.Where(s => s.MUAId == m.MUAId).Min(s => (decimal?)s.Price) ?? 0);
                        break;
                    case "price_desc":
                        // Sắp xếp theo giá dịch vụ cao nhất giảm dần
                        query = query.OrderByDescending(m => _context.Services.Where(s => s.MUAId == m.MUAId).Max(s => (decimal?)s.Price) ?? 0);
                        break;
                }
            }

            var profiles = await query.ToListAsync();
            var result = new List<MuaProfileDto>();

            foreach (var profile in profiles)
            {
                // Lấy danh sách tên phong cách trang điểm
                var styles = await _context.MUAStyles
                    .Where(ms => ms.MUAId == profile.MUAId)
                    .Include(ms => ms.MakeupStyle)
                    .Select(ms => ms.MakeupStyle != null ? ms.MakeupStyle.Name : "")
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToListAsync();

                result.Add(new MuaProfileDto
                {
                    MUAId = profile.MUAId,
                    Bio = profile.Bio,
                    ExperienceYears = profile.ExperienceYears,
                    RatingAverage = profile.RatingAverage,
                    TotalBookings = profile.TotalBookings,
                    PortfolioCoverUrl = profile.PortfolioCoverUrl,
                    FullName = profile.User?.FullName,
                    Email = profile.User?.Email,
                    AvatarUrl = profile.User?.AvatarUrl,
                    PhoneNumber = profile.User?.PhoneNumber,
                    Styles = styles!
                });
            }

            return result;
        }

        public async Task<MuaProfileDto?> GetMuaByIdAsync(Guid muaId)
        {
            var profile = await _context.MakeupArtistProfiles
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.MUAId == muaId);

            if (profile == null) return null;

            var styles = await _context.MUAStyles
                .Where(ms => ms.MUAId == muaId)
                .Include(ms => ms.MakeupStyle)
                .Select(ms => ms.MakeupStyle != null ? ms.MakeupStyle.Name : "")
                .Where(name => !string.IsNullOrEmpty(name))
                .ToListAsync();

            return new MuaProfileDto
            {
                MUAId = profile.MUAId,
                Bio = profile.Bio,
                ExperienceYears = profile.ExperienceYears,
                RatingAverage = profile.RatingAverage,
                TotalBookings = profile.TotalBookings,
                PortfolioCoverUrl = profile.PortfolioCoverUrl,
                FullName = profile.User?.FullName,
                Email = profile.User?.Email,
                AvatarUrl = profile.User?.AvatarUrl,
                PhoneNumber = profile.User?.PhoneNumber,
                Styles = styles!
            };
        }

        public async Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto)
        {
            var profile = await _context.MakeupArtistProfiles.FirstOrDefaultAsync(m => m.MUAId == muaId);
            if (profile == null) return false;

            profile.Bio = updateDto.Bio;
            profile.ExperienceYears = updateDto.ExperienceYears;
            profile.PortfolioCoverUrl = updateDto.PortfolioCoverUrl;

            return await _context.SaveChangesAsync() > 0;
        }

        // ================= SERVICES MANAGEMENT =================
        public async Task<List<ServiceDto>> GetMuaServicesAsync(Guid muaId)
        {
            return await _context.Services
                .Where(s => s.MUAId == muaId)
                .Select(s => new ServiceDto
                {
                    ServiceId = s.ServiceId,
                    MUAId = s.MUAId,
                    ServiceName = s.ServiceName,
                    Description = s.Description,
                    Price = s.Price,
                    DurationMinutes = s.DurationMinutes
                })
                .ToListAsync();
        }

        public async Task<ServiceDto?> AddMuaServiceAsync(Guid muaId, ServiceCreateDto serviceDto)
        {
            var profileExists = await _context.MakeupArtistProfiles.AnyAsync(m => m.MUAId == muaId);
            if (!profileExists) return null;

            var service = new Service
            {
                ServiceId = Guid.NewGuid(),
                MUAId = muaId,
                ServiceName = serviceDto.ServiceName,
                Description = serviceDto.Description,
                Price = serviceDto.Price,
                DurationMinutes = serviceDto.DurationMinutes
            };

            await _context.Services.AddAsync(service);
            await _context.SaveChangesAsync();

            return new ServiceDto
            {
                ServiceId = service.ServiceId,
                MUAId = service.MUAId,
                ServiceName = service.ServiceName,
                Description = service.Description,
                Price = service.Price,
                DurationMinutes = service.DurationMinutes
            };
        }

        public async Task<bool> UpdateMuaServiceAsync(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto)
        {
            var service = await _context.Services.FirstOrDefaultAsync(s => s.ServiceId == serviceId && s.MUAId == muaId);
            if (service == null) return false;

            service.ServiceName = serviceDto.ServiceName;
            service.Description = serviceDto.Description;
            service.Price = serviceDto.Price;
            service.DurationMinutes = serviceDto.DurationMinutes;

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteMuaServiceAsync(Guid muaId, Guid serviceId)
        {
            var service = await _context.Services.FirstOrDefaultAsync(s => s.ServiceId == serviceId && s.MUAId == muaId);
            if (service == null) return false;

            _context.Services.Remove(service);
            return await _context.SaveChangesAsync() > 0;
        }

        // ================= PORTFOLIO MANAGEMENT =================
        public async Task<List<Portfolio>> GetMuaPortfolioAsync(Guid muaId)
        {
            return await _context.Portfolios
                .Where(p => p.MUAId == muaId)
                .ToListAsync();
        }

        public async Task<bool> AddPortfolioImageAsync(Guid muaId, string imageUrl, string description)
        {
            var profileExists = await _context.MakeupArtistProfiles.AnyAsync(m => m.MUAId == muaId);
            if (!profileExists) return false;

            var portfolio = new Portfolio
            {
                PortfolioId = Guid.NewGuid(),
                MUAId = muaId,
                ImageUrl = imageUrl,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Portfolios.AddAsync(portfolio);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeletePortfolioImageAsync(Guid muaId, Guid portfolioId)
        {
            var portfolio = await _context.Portfolios.FirstOrDefaultAsync(p => p.PortfolioId == portfolioId && p.MUAId == muaId);
            if (portfolio == null) return false;

            _context.Portfolios.Remove(portfolio);
            return await _context.SaveChangesAsync() > 0;
        }

        // ================= STYLES RELATIONSHIP =================
        public async Task<bool> UpdateStylesAsync(Guid muaId, List<int> styleIds)
        {
            var profileExists = await _context.MakeupArtistProfiles.AnyAsync(m => m.MUAId == muaId);
            if (!profileExists) return false;

            // Xóa các style cũ
            var oldStyles = await _context.MUAStyles.Where(ms => ms.MUAId == muaId).ToListAsync();
            _context.MUAStyles.RemoveRange(oldStyles);

            // Thêm các style mới
            foreach (var styleId in styleIds)
            {
                var styleExists = await _context.MakeupStyles.AnyAsync(s => s.StyleId == styleId);
                if (styleExists)
                {
                    await _context.MUAStyles.AddAsync(new MUAStyle
                    {
                        MUAId = muaId,
                        StyleId = styleId
                    });
                }
            }

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<List<MakeupStyle>> GetAllStylesAsync()
        {
            return await _context.MakeupStyles.ToListAsync();
        }
    }
}
