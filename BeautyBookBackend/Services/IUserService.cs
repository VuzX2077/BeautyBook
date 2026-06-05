using System;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IUserService
    {
        Task<UserDto?> GetProfileAsync(Guid userId);
        Task<UserDto?> UpdateProfileAsync(Guid userId, UserUpdateDto updateDto);
    }
}
