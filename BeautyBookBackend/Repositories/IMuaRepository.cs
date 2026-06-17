using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using MakeupService = BeautyBookBackend.Models.Service;

namespace BeautyBookBackend.Repositories
{
    public interface IMuaRepository
    {
        Task AddProfileAsync(MakeupArtistProfile profile);
        Task<bool> ProfileExistsAsync(Guid muaId);
        Task<List<MakeupArtistProfile>> GetProfilesAsync(MuaFilterDto filter);
        Task<MakeupArtistProfile?> GetProfileByIdAsync(Guid muaId);
        Task<MakeupArtistProfile?> GetProfileWithUserByIdAsync(Guid muaId);
        Task<List<string>> GetStyleNamesByMuaIdAsync(Guid muaId);
        Task<List<MakeupService>> GetServicesByMuaIdAsync(Guid muaId);
        Task<decimal?> GetMinPriceByMuaIdAsync(Guid muaId);
        Task<MakeupService?> GetServiceByIdForMuaAsync(Guid serviceId, Guid muaId);
        Task AddServiceAsync(MakeupService service);
        void RemoveService(MakeupService service);
        Task<List<Portfolio>> GetPortfolioByMuaIdAsync(Guid muaId);
        Task<Portfolio?> GetPortfolioByIdForMuaAsync(Guid portfolioId, Guid muaId);
        Task AddPortfolioAsync(Portfolio portfolio);
        void RemovePortfolio(Portfolio portfolio);
        Task<List<MUAStyle>> GetStyleLinksByMuaIdAsync(Guid muaId);
        void RemoveStyleLinks(IEnumerable<MUAStyle> styleLinks);
        Task<bool> StyleExistsAsync(int styleId);
        Task AddMuaStyleAsync(MUAStyle style);
        Task<List<MakeupStyle>> GetAllStylesAsync();
    }
}
