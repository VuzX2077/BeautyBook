using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IMuaService
    {
        Task<List<MuaProfileDto>> GetMuasAsync(int page);
        Task<MuaDetailDto?> GetMuaByIdAsync(Guid muaId);
        Task<MuaProfileDto?> ApplyMuaAsync(Guid muaId, MuaApplicationRequestDto request);
        Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto);
        Task RecalculateProfileStateAsync(Guid muaId);
        Task RecalculateProfileQualityScoreAsync(Guid muaId);
        Task<bool> HasMuaProfileAsync(Guid muaId);
        
        // Services
        Task<List<ServiceDto>> GetMuaServicesAsync(Guid muaId);
        Task<ServiceDto?> AddMuaServiceAsync(Guid muaId, ServiceCreateDto serviceDto);
        Task<bool> UpdateMuaServiceAsync(Guid muaId, Guid serviceId, ServiceCreateDto serviceDto);
        Task<bool> DeleteMuaServiceAsync(Guid muaId, Guid serviceId);

        // Portfolio
        Task<List<PortfolioDto>> GetMuaPortfolioAsync(Guid muaId, Guid? currentUserId = null);
        Task<bool> AddPortfolioImageAsync(Guid muaId, PortfolioCreateRequest request);
        Task<bool> UpdatePortfolioImageAsync(Guid muaId, Guid portfolioId, PortfolioCreateRequest request);
        Task<bool> DeletePortfolioAsync(Guid muaId, Guid portfolioId);
        Task<bool> TogglePortfolioVisibilityAsync(Guid muaId, Guid portfolioId);
        Task<bool> TogglePortfolioPinAsync(Guid muaId, Guid portfolioId);

        // Portfolio Interactions
        Task<bool> TogglePortfolioLikeAsync(Guid userId, Guid portfolioId);
        Task<bool> TogglePortfolioSaveAsync(Guid userId, Guid portfolioId);
        Task<PortfolioCommentDto?> AddPortfolioCommentAsync(Guid userId, Guid portfolioId, string content);
        Task<List<PortfolioCommentDto>> GetPortfolioCommentsAsync(Guid portfolioId);

        // Styles
        Task<bool> UpdateStylesAsync(Guid muaId, List<int> styleIds);
        Task<List<MakeupStyleDto>> GetAllStylesAsync();
    }
}
