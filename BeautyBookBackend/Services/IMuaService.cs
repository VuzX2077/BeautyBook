using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services
{
    public interface IMuaService
    {
        Task<List<MuaProfileDto>> GetMuasAsync(MuaFilterDto filter);
        Task<MuaProfileDto?> GetMuaByIdAsync(Guid muaId);
        Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto);
        
        // Services
        Task<List<ServiceDto>> GetMuaServicesAsync(Guid muaId);
        Task<ServiceDto?> AddMuaServiceAsync(Guid muaId, ServiceCreateDto serviceDto);
        Task<bool> UpdateMuaServiceAsync(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto);
        Task<bool> DeleteMuaServiceAsync(Guid muaId, Guid serviceId);

        // Portfolio
        Task<List<Portfolio>> GetMuaPortfolioAsync(Guid muaId);
        Task<bool> AddPortfolioImageAsync(Guid muaId, string imageUrl, string description);
        Task<bool> DeletePortfolioImageAsync(Guid muaId, Guid portfolioId);

        // Styles
        Task<bool> UpdateStylesAsync(Guid muaId, List<int> styleIds);
        Task<List<MakeupStyle>> GetAllStylesAsync();
    }
}
