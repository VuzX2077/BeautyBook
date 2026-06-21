using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;
using MakeupService = BeautyBookBackend.Models.Service;

namespace BeautyBookBackend.Repositories
{
    public class MuaRepository : IMuaRepository
    {
        private readonly ApplicationDbContext _context;

        public MuaRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task AddProfileAsync(MakeupArtistProfile profile)
        {
            return _context.MakeupArtistProfiles.AddAsync(profile).AsTask();
        }

        public Task<bool> ProfileExistsAsync(Guid muaId)
        {
            return _context.MakeupArtistProfiles.AnyAsync(m => m.MUAId == muaId);
        }

        public async Task<List<MakeupArtistProfile>> GetProfilesAsync(int page, int pageSize)
        {
            return await _context.MakeupArtistProfiles
                .Include(m => m.User)
                .Where(m => m.Status == Models.Enums.MuaStatus.Listed)
                .OrderByDescending(m => m.RankScore)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public Task<MakeupArtistProfile?> GetProfileByIdAsync(Guid muaId)
        {
            return _context.MakeupArtistProfiles.FirstOrDefaultAsync(m => m.MUAId == muaId);
        }

        public Task<MakeupArtistProfile?> GetProfileWithFullDetailsAsync(Guid muaId)
        {
            return _context.MakeupArtistProfiles
                .Include(m => m.User)
                .Include(m => m.Services)
                .Include(m => m.Portfolios)
                .FirstOrDefaultAsync(m => m.MUAId == muaId);
        }

        public Task<List<string>> GetStyleNamesByMuaIdAsync(Guid muaId)
        {
            return _context.MUAStyles
                .Where(ms => ms.MUAId == muaId)
                .Include(ms => ms.MakeupStyle)
                .Select(ms => ms.MakeupStyle != null ? ms.MakeupStyle.Name : "")
                .Where(name => !string.IsNullOrEmpty(name))
                .ToListAsync()!;
        }

        public Task<List<MakeupService>> GetServicesByMuaIdAsync(Guid muaId)
        {
            return _context.Services
                .Where(s => s.MUAId == muaId)
                .OrderBy(s => s.Price)
                .ThenBy(s => s.ServiceName)
                .ToListAsync();
        }

        public Task<decimal?> GetMinPriceByMuaIdAsync(Guid muaId)
        {
            return _context.Services
                .Where(s => s.MUAId == muaId)
                .Select(s => (decimal?)s.Price)
                .MinAsync();
        }

        public Task<MakeupService?> GetServiceByIdForMuaAsync(Guid serviceId, Guid muaId)
        {
            return _context.Services.FirstOrDefaultAsync(s => s.ServiceId == serviceId && s.MUAId == muaId);
        }

        public Task AddServiceAsync(MakeupService service)
        {
            return _context.Services.AddAsync(service).AsTask();
        }

        public void RemoveService(MakeupService service)
        {
            _context.Services.Remove(service);
        }

        public Task<List<Portfolio>> GetPortfolioByMuaIdAsync(Guid muaId)
        {
            return _context.Portfolios
                .Where(p => p.MUAId == muaId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public Task<Portfolio?> GetPortfolioByIdForMuaAsync(Guid portfolioId, Guid muaId)
        {
            return _context.Portfolios.FirstOrDefaultAsync(p => p.PortfolioId == portfolioId && p.MUAId == muaId);
        }

        public Task AddPortfolioAsync(Portfolio portfolio)
        {
            return _context.Portfolios.AddAsync(portfolio).AsTask();
        }

        public void RemovePortfolio(Portfolio portfolio)
        {
            _context.Portfolios.Remove(portfolio);
        }

        public Task<List<MUAStyle>> GetStyleLinksByMuaIdAsync(Guid muaId)
        {
            return _context.MUAStyles.Where(ms => ms.MUAId == muaId).ToListAsync();
        }

        public void RemoveStyleLinks(IEnumerable<MUAStyle> styleLinks)
        {
            _context.MUAStyles.RemoveRange(styleLinks);
        }

        public Task<bool> StyleExistsAsync(int styleId)
        {
            return _context.MakeupStyles.AnyAsync(s => s.StyleId == styleId);
        }

        public Task AddMuaStyleAsync(MUAStyle style)
        {
            return _context.MUAStyles.AddAsync(style).AsTask();
        }

        public Task<List<MakeupStyle>> GetAllStylesAsync()
        {
            return _context.MakeupStyles.ToListAsync();
        }
    }
}
