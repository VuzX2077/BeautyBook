using System;
using System.Threading.Tasks;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Repositories
{
    public interface IUserRepository
    {
        Task<bool> EmailExistsAsync(string email);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByIdAsync(Guid userId);
        Task AddAsync(User user);
    }
}
