using System.Threading.Tasks;

namespace BeautyBookBackend.Repositories
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync();
    }
}
