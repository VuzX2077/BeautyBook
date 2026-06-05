using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using MakeupService = BeautyBookBackend.Models.Service;

namespace BeautyBookBackend.Services
{
    public class MuaService : IMuaService
    {
        private readonly IMuaRepository _muaRepository;
        private readonly IUnitOfWork _unitOfWork;

        public MuaService(IMuaRepository muaRepository, IUnitOfWork unitOfWork)
        {
            _muaRepository = muaRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<List<MuaProfileDto>> GetMuasAsync(MuaFilterDto filter)
        {
            var profiles = await _muaRepository.GetProfilesAsync(filter);
            var result = new List<MuaProfileDto>();

            foreach (var profile in profiles)
            {
                var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(profile.MUAId);
                result.Add(ToMuaProfileDto(profile, styles));
            }

            return result;
        }

        public async Task<MuaProfileDto?> GetMuaByIdAsync(Guid muaId)
        {
            var profile = await _muaRepository.GetProfileWithUserByIdAsync(muaId);
            if (profile == null) return null;

            var styles = await _muaRepository.GetStyleNamesByMuaIdAsync(muaId);
            return ToMuaProfileDto(profile, styles);
        }

        public async Task<bool> UpdateMuaProfileAsync(Guid muaId, MuaUpdateDto updateDto)
        {
            var profile = await _muaRepository.GetProfileByIdAsync(muaId);
            if (profile == null) return false;

            profile.Bio = updateDto.Bio;
            profile.ExperienceYears = updateDto.ExperienceYears;
            profile.PortfolioCoverUrl = updateDto.PortfolioCoverUrl;

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<List<ServiceDto>> GetMuaServicesAsync(Guid muaId)
        {
            var services = await _muaRepository.GetServicesByMuaIdAsync(muaId);
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
                DurationMinutes = serviceDto.DurationMinutes
            };

            await _muaRepository.AddServiceAsync(service);
            await _unitOfWork.SaveChangesAsync();

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

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteMuaServiceAsync(Guid muaId, Guid serviceId)
        {
            var service = await _muaRepository.GetServiceByIdForMuaAsync(serviceId, muaId);
            if (service == null) return false;

            _muaRepository.RemoveService(service);
            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public Task<List<Portfolio>> GetMuaPortfolioAsync(Guid muaId)
        {
            return _muaRepository.GetPortfolioByMuaIdAsync(muaId);
        }

        public async Task<bool> AddPortfolioImageAsync(Guid muaId, string imageUrl, string description)
        {
            if (!await _muaRepository.ProfileExistsAsync(muaId)) return false;

            await _muaRepository.AddPortfolioAsync(new Portfolio
            {
                PortfolioId = Guid.NewGuid(),
                MUAId = muaId,
                ImageUrl = imageUrl,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeletePortfolioImageAsync(Guid muaId, Guid portfolioId)
        {
            var portfolio = await _muaRepository.GetPortfolioByIdForMuaAsync(portfolioId, muaId);
            if (portfolio == null) return false;

            _muaRepository.RemovePortfolio(portfolio);
            return await _unitOfWork.SaveChangesAsync() > 0;
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

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public Task<List<MakeupStyle>> GetAllStylesAsync()
        {
            return _muaRepository.GetAllStylesAsync();
        }

        private static MuaProfileDto ToMuaProfileDto(MakeupArtistProfile profile, List<string> styles)
        {
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
                Styles = styles
            };
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
                DurationMinutes = service.DurationMinutes
            };
        }
    }
}
