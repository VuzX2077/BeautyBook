using System;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;

namespace BeautyBookBackend.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMuaRepository _muaRepository;
        private readonly IMuaService _muaService;
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IUserRepository userRepository, IMuaRepository muaRepository, IMuaService muaService, IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _muaRepository = muaRepository;
            _muaService = muaService;
            _unitOfWork = unitOfWork;
        }

        public async Task<UserDto?> GetProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;
            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            return ToDto(user, hasMuaProfile);
        }

        public async Task<UserProfileDto?> GetFullUserProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;

            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            var dto = ToDto(user, hasMuaProfile);

            var profileDto = new UserProfileDto
            {
                UserId = dto.UserId,
                FullName = dto.FullName,
                Email = dto.Email,
                AvatarUrl = dto.AvatarUrl,
                PhoneNumber = dto.PhoneNumber,
                Role = dto.Role,
                CreatedAt = dto.CreatedAt,
                IsActive = dto.IsActive,
                HasMuaProfile = dto.HasMuaProfile
            };

            if (hasMuaProfile)
            {
                profileDto.MuaProfile = await _muaService.GetMuaByIdAsync(user.UserId);
            }

            return profileDto;
        }

        public async Task<UserDto?> UpdateProfileAsync(Guid userId, UserUpdateDto updateDto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;

            if (!string.IsNullOrEmpty(updateDto.FullName)) user.FullName = updateDto.FullName;
            if (!string.IsNullOrEmpty(updateDto.AvatarUrl)) user.AvatarUrl = updateDto.AvatarUrl;
            if (!string.IsNullOrEmpty(updateDto.PhoneNumber)) user.PhoneNumber = updateDto.PhoneNumber;

            await _unitOfWork.SaveChangesAsync();
            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            return ToDto(user, hasMuaProfile);
        }

        private static UserDto ToDto(User user, bool hasMuaProfile)
        {
            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                IsActive = user.IsActive,
                HasMuaProfile = hasMuaProfile
            };
        }
    }
}
