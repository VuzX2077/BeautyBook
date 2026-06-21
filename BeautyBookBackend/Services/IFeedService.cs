using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IFeedService
    {
        Task<List<FeedItemDto>> GetFeedAsync(int page = 1, int limit = 20, Guid? currentUserId = null);
    }
}
