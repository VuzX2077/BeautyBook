using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public sealed class MuaScheduleService : IMuaScheduleService
    {
        private readonly ApplicationDbContext _db;
        private readonly BookingTimeService _bookingTime;
        public MuaScheduleService(ApplicationDbContext db, BookingTimeService bookingTime)
        {
            _db = db;
            _bookingTime = bookingTime;
        }

        public Task<bool> HasValidScheduleAsync(Guid muaId) => _db.MuaWorkingSchedules.AnyAsync(x =>
            x.MUAId == muaId && x.IsActive && x.StartTime >= TimeSpan.Zero && x.EndTime <= TimeSpan.FromDays(1) && x.StartTime < x.EndTime);

        public async Task<bool> IsAvailableAsync(Guid muaId, DateTime date, TimeSpan startTime, TimeSpan endTime)
        {
            if (date == default || startTime < TimeSpan.Zero || endTime <= startTime || endTime > TimeSpan.FromDays(1)) return false;
            var day = date.DayOfWeek;
            var insideWorkingHours = await _db.MuaWorkingSchedules.AnyAsync(x => x.MUAId == muaId
                && x.IsActive && x.DayOfWeek == day && x.StartTime <= startTime && x.EndTime >= endTime);
            if (!insideWorkingHours) return false;

            var startAt = _bookingTime.ToUtc(date, startTime);
            var endAt = _bookingTime.ToUtc(date, endTime);
            return !await _db.MuaTimeOffs.AnyAsync(x => x.MUAId == muaId && x.StartAt < endAt && x.EndAt > startAt);
        }

        public async Task<IReadOnlyList<TimeSpan>> GetAvailableStartsAsync(Guid muaId, DateTime date, int durationMinutes, int intervalMinutes = 30)
        {
            if (date == default || durationMinutes <= 0 || intervalMinutes <= 0) return Array.Empty<TimeSpan>();
            var schedules = await _db.MuaWorkingSchedules.AsNoTracking()
                .Where(x => x.MUAId == muaId && x.IsActive && x.DayOfWeek == date.DayOfWeek && x.StartTime < x.EndTime)
                .OrderBy(x => x.StartTime).ToListAsync();
            var dayStart = _bookingTime.ToUtc(date, TimeSpan.Zero);
            var dayEnd = _bookingTime.ToUtc(date.Date.AddDays(1), TimeSpan.Zero);
            var timeOffs = await _db.MuaTimeOffs.AsNoTracking()
                .Where(x => x.MUAId == muaId && x.StartAt < dayEnd && x.EndAt > dayStart).ToListAsync();
            var duration = TimeSpan.FromMinutes(durationMinutes);
            var interval = TimeSpan.FromMinutes(intervalMinutes);
            var result = new List<TimeSpan>();
            foreach (var schedule in schedules)
            {
                for (var start = schedule.StartTime; start.Add(duration) <= schedule.EndTime; start = start.Add(interval))
                {
                    var startAt = _bookingTime.ToUtc(date, start);
                    var endAt = _bookingTime.ToUtc(date, start.Add(duration));
                    if (!timeOffs.Any(x => x.StartAt < endAt && x.EndAt > startAt)) result.Add(start);
                }
            }
            return result.Distinct().OrderBy(x => x).ToList();
        }

        public async Task<IReadOnlyList<WorkingScheduleDto>> GetPublicScheduleAsync(Guid muaId) =>
            await _db.MuaWorkingSchedules.AsNoTracking().Where(x => x.MUAId == muaId && x.IsActive)
                .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
                .Select(x => new WorkingScheduleDto { Id = x.Id, DayOfWeek = x.DayOfWeek, StartTime = x.StartTime, EndTime = x.EndTime, IsActive = x.IsActive })
                .ToListAsync();

        public async Task<MuaScheduleManagementDto?> GetManagementScheduleAsync(Guid muaId)
        {
            if (!await _db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == muaId)) return null;
            return new MuaScheduleManagementDto
            {
                WorkingSchedules = await _db.MuaWorkingSchedules.AsNoTracking().Where(x => x.MUAId == muaId)
                    .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
                    .Select(x => new WorkingScheduleDto { Id = x.Id, DayOfWeek = x.DayOfWeek, StartTime = x.StartTime, EndTime = x.EndTime, IsActive = x.IsActive }).ToListAsync(),
                TimeOffs = await _db.MuaTimeOffs.AsNoTracking().Where(x => x.MUAId == muaId && x.EndAt >= DateTime.UtcNow)
                    .OrderBy(x => x.StartAt)
                    .Select(x => new MuaTimeOffDto { Id = x.Id, StartAt = x.StartAt, EndAt = x.EndAt, Reason = x.Reason }).ToListAsync()
            };
        }

        public async Task ReplaceWorkingScheduleAsync(Guid muaId, IReadOnlyList<WorkingScheduleRequest> schedules)
        {
            if (!await _db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == muaId)) throw new InvalidOperationException("MUA_PROFILE_NOT_FOUND");
            ValidateSchedules(schedules);
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var lockKey = $"mua-schedule:{muaId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var old = await _db.MuaWorkingSchedules.Where(x => x.MUAId == muaId).ToListAsync();
            _db.MuaWorkingSchedules.RemoveRange(old);
            _db.MuaWorkingSchedules.AddRange(schedules.Select(x => new MuaWorkingSchedule
            {
                Id = Guid.NewGuid(), MUAId = muaId, DayOfWeek = (DayOfWeek)x.DayOfWeek,
                StartTime = x.StartTime, EndTime = x.EndTime, IsActive = x.IsActive
            }));
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task<MuaTimeOffDto> AddTimeOffAsync(Guid muaId, CreateMuaTimeOffRequest request)
        {
            if (request.StartAt == default || request.EndAt <= request.StartAt) throw new InvalidOperationException("INVALID_TIME_OFF");
            if (!await _db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == muaId)) throw new InvalidOperationException("MUA_PROFILE_NOT_FOUND");
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var lockKey = $"mua-schedule:{muaId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var entity = new MuaTimeOff { Id = Guid.NewGuid(), MUAId = muaId, StartAt = _bookingTime.NormalizeInstantToUtc(request.StartAt), EndAt = _bookingTime.NormalizeInstantToUtc(request.EndAt), Reason = request.Reason?.Trim(), CreatedAt = DateTime.UtcNow };
            _db.MuaTimeOffs.Add(entity);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new MuaTimeOffDto { Id = entity.Id, StartAt = entity.StartAt, EndAt = entity.EndAt, Reason = entity.Reason };
        }

        public async Task<bool> DeleteTimeOffAsync(Guid muaId, Guid timeOffId)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var lockKey = $"mua-schedule:{muaId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var entity = await _db.MuaTimeOffs.FirstOrDefaultAsync(x => x.Id == timeOffId && x.MUAId == muaId);
            if (entity == null) return false;
            _db.MuaTimeOffs.Remove(entity);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        private static void ValidateSchedules(IReadOnlyList<WorkingScheduleRequest> schedules)
        {
            foreach (var item in schedules)
                if (item.DayOfWeek is < 0 or > 6 || item.StartTime < TimeSpan.Zero || item.EndTime > TimeSpan.FromDays(1) || item.StartTime >= item.EndTime)
                    throw new InvalidOperationException("INVALID_WORKING_SCHEDULE");

            foreach (var dayGroup in schedules.Where(x => x.IsActive).GroupBy(x => x.DayOfWeek))
            {
                var ordered = dayGroup.OrderBy(x => x.StartTime).ToList();
                for (var i = 1; i < ordered.Count; i++)
                    if (ordered[i].StartTime < ordered[i - 1].EndTime) throw new InvalidOperationException("OVERLAPPING_WORKING_SCHEDULE");
            }
        }
    }
}
