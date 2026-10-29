using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BeautyBookBackend.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMuaRepository _muaRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly IEmailOtpService _emailOtpService;

        public AuthService(
            IUserRepository userRepository,
            IMuaRepository muaRepository,
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            ApplicationDbContext dbContext,
            IEmailOtpService emailOtpService)
        {
            _userRepository = userRepository;
            _muaRepository = muaRepository;
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _dbContext = dbContext;
            _emailOtpService = emailOtpService;
        }

        public async Task<string?> SendRegistrationOtpAsync(string email)
        {
            email = NormalizeEmail(email);
            if (await _userRepository.EmailExistsAsync(email)) return "EMAIL_EXISTS";
            await _emailOtpService.IssueAsync(email, "REGISTER", resendCooldownSeconds: 45);
            return null;
        }

        public async Task SendPasswordResetOtpAsync(string email)
        {
            email = NormalizeEmail(email);
            var user = await _userRepository.GetByEmailAsync(email);
            if (user != null && !user.IsDemoAccount && user.IsActive && !user.DeletedAt.HasValue && !string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                await _emailOtpService.IssueAsync(email, "RESET_PASSWORD");
                await _dbContext.EmailOtps.Where(x => x.Email == email && x.Purpose == "RESET_PASSWORD_GRANT" && x.UsedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, DateTime.UtcNow));
            }
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            registerDto.Email = NormalizeEmail(registerDto.Email);
            if (await _userRepository.EmailExistsAsync(registerDto.Email))
            {
                return null;
            }

            var otp = await _emailOtpService.ConsumeAsync(registerDto.Email, "REGISTER", null, registerDto.Otp);
            if (!otp) return null;

            var user = new User
            {
                UserId = Guid.NewGuid(),
                FullName = registerDto.FullName,
                Email = registerDto.Email,
                VerifiedCommerceEmail = registerDto.Email,
                PasswordHash = HashPassword(registerDto.Password),
                PhoneNumber = registerDto.PhoneNumber,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _userRepository.AddAsync(user);

            await _unitOfWork.SaveChangesAsync();

            return ToUserDto(user, false);
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto request)
        {
            var email = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(email);
            if (user == null || user.IsDemoAccount || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return false;
            if (!await _emailOtpService.ConsumeAsync(email, "RESET_PASSWORD", null, request.Otp)) return false;
            user.PasswordHash = HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        // Reuse the existing OTP table: no schema change, and grants work across server instances.
        public async Task<string?> VerifyPasswordResetOtpAsync(VerifyPasswordResetOtpDto request)
        {
            var email = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(email);
            if (user == null || user.IsDemoAccount || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return null;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            if (!await _emailOtpService.ConsumeAsync(email, "RESET_PASSWORD", null, request.Otp))
            {
                // Commit failed-attempt counters written by ConsumeAsync.
                await transaction.CommitAsync();
                return null;
            }
            var now = DateTime.UtcNow;
            await _dbContext.EmailOtps.Where(x => x.Email == email && x.Purpose == "RESET_PASSWORD_GRANT" && x.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now));
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _dbContext.EmailOtps.Add(new EmailOtp {
                Id = Guid.NewGuid(), Email = email, Purpose = "RESET_PASSWORD_GRANT",
                CodeHash = HashResetValue(token), ContextHash = HashResetValue(user.PasswordHash),
                CreatedAt = now, ExpiresAt = now.AddMinutes(5)
            });
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return token;
        }

        public async Task<bool> CompletePasswordResetAsync(CompletePasswordResetDto request)
        {
            var email = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(email);
            if (user == null || user.IsDemoAccount || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return false;
            var tokenHash = HashResetValue(request.ResetToken);
            var passwordContext = HashResetValue(user.PasswordHash);
            var now = DateTime.UtcNow;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var consumed = await _dbContext.EmailOtps
                .Where(x => x.Email == email && x.Purpose == "RESET_PASSWORD_GRANT" && x.CodeHash == tokenHash
                    && x.ContextHash == passwordContext && x.UsedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now));
            if (consumed != 1) return false;
            user.PasswordHash = HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        private static string HashResetValue(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || user.IsDemoAccount || !user.IsActive || user.DeletedAt.HasValue ||
                !VerifyPasswordHash(request.CurrentPassword, user.PasswordHash)) return false;
            user.PasswordHash = HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        public async Task<TokenDto?> LoginAsync(LoginDto loginDto)
        {
            var user = await _userRepository.GetByEmailAsync(NormalizeEmail(loginDto.Email));
            if (user == null || !user.IsActive || user.DeletedAt.HasValue || !VerifyPasswordHash(loginDto.Password, user.PasswordHash))
            {
                return null;
            }

            if (!user.PasswordHash!.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                user.PasswordHash = HashPassword(loginDto.Password);
                await _unitOfWork.SaveChangesAsync();
            }

            return await GenerateJwtTokenAsync(user, loginDto.UseRefreshSession);
        }

        public async Task<bool> VerifyPasswordAsync(Guid userId, string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return false;
            return VerifyPasswordHash(password, user.PasswordHash);
        }

        public Task<TokenDto?> BecomeMuaAsync(Guid userId, MuaApplicationRequestDto request) =>
            new UnifiedStyleService(_dbContext).Write(userId, () => BecomeMuaCore(userId, request), x => x != null);

        private async Task<TokenDto?> BecomeMuaCore(Guid userId, MuaApplicationRequestDto request)
        {
            if (!WorkLocationPolicy.ValidCoordinates(request.Latitude, request.Longitude)) return null;
            if ((request.WorkLocationAddress != null || request.WorkLocationName != null || request.AllowCustomerVisit)
                && !WorkLocationPolicy.Valid(request.WorkLocationName, request.WorkLocationAddress, request.Latitude, request.Longitude, request.OperatingLocationConfirmed, request.AllowCustomerVisit)) return null;
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || (user.Role != UserRole.Customer && user.Role != UserRole.MUA))
            {
                return null;
            }

            await new UnifiedStyleService(_dbContext).EnsureWritable(userId, requireProfile: false);
            var profile = await _dbContext.MakeupArtistProfiles.FirstOrDefaultAsync(x => x.MUAId == user.UserId);
            if (profile == null)
            {
                profile = new MakeupArtistProfile
                {
                    MUAId = user.UserId,
                    AverageRating = 0,
                    TotalBookings = 0,
                    Status = MuaStatus.Draft
                    ,VerificationStatus = MuaVerificationStatus.Draft
                };
                _dbContext.MuaMonetizationAccounts.Add(new() { MuaId = user.UserId, Cohort = MuaActivationCohort.RolloutWaiver, EnrolledAt = DateTime.UtcNow });
                await _muaRepository.AddProfileAsync(profile);
            }
            else if (profile.VerificationStatus is MuaVerificationStatus.PendingReview or MuaVerificationStatus.Approved)
            {
                return null;
            }

            user.FullName = request.DisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !string.Equals(user.PhoneNumber, request.PhoneNumber.Trim(), StringComparison.Ordinal))
            {
                user.PhoneNumber = request.PhoneNumber.Trim();
                user.PhoneVerified = false;
            }
            user.AvatarUrl = request.AvatarUrl.Trim();
            profile.City = request.City.Trim();
            profile.Bio = request.Bio?.Trim();
            profile.District = request.District?.Trim();
            profile.ProvinceCode = request.ProvinceCode;
            profile.DistrictCode = request.DistrictCode;
            profile.ExperienceLevel = request.ExperienceLevel;
            profile.Latitude = request.Latitude;
            profile.Longitude = request.Longitude;
            profile.OperatingLocationConfirmed = request.OperatingLocationConfirmed;
            profile.PublicMeetingPoint = request.PublicMeetingPoint;
            profile.OperatingLocationLabel = request.OperatingLocationLabel?.Trim();
            if (request.WorkLocationAddress != null || request.WorkLocationName != null || request.AllowCustomerVisit)
                WorkLocationPolicy.Set(profile, request.WorkLocationName, request.WorkLocationAddress, request.Latitude, request.Longitude, request.OperatingLocationConfirmed, request.AllowCustomerVisit);
            else { profile.WorkLocationName = null; profile.WorkLocationAddress = null; profile.AllowCustomerVisit = false; }
            if (request.OperatingAreaIds != null)
            {
                profile.OperatingProvinceCode = request.OperatingProvinceCode;
                profile.City = OperatingAreas.Province(request.OperatingProvinceCode)!.Name;
                var wanted = request.OperatingAreaIds.ToHashSet();
                foreach (var old in profile.OperatingAreas.Where(a => !wanted.Contains(a.AreaId)).ToList()) profile.OperatingAreas.Remove(old);
                foreach (var id in wanted.Where(id => !profile.OperatingAreas.Any(a => a.AreaId == id)))
                    profile.OperatingAreas.Add(new MuaOperatingArea { MuaId = userId, AreaId = id });
            }
            profile.ExperienceYears = request.ExperienceYears ?? 0;
            profile.Specialization = request.Specialization?.Trim();
            profile.SocialLinks = request.SocialLinks?.Trim();
            profile.Address = request.Address?.Trim();

            await new UnifiedStyleService(_dbContext).ReplaceSpecialties(userId, request.StyleIds, request.NewStyleNames);

            user.Role = UserRole.MUA;
            await _unitOfWork.SaveChangesAsync();

            return await GenerateJwtTokenAsync(user);
        }

        public async Task<TokenDto?> GoogleLoginAsync(GoogleLoginDto googleLoginDto)
        {
            var clientIds = GetGoogleClientIds();
            if (clientIds.Count == 0)
            {
                return null;
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    googleLoginDto.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = clientIds
                    });
            }
            catch (InvalidJwtException)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(payload.Email) || payload.EmailVerified != true)
            {
                return null;
            }

            var user = await _userRepository.GetByEmailAsync(payload.Email);
            if (user == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    FullName = payload.Name ?? payload.Email,
                    Email = payload.Email,
                    VerifiedCommerceEmail = payload.Email.Trim().ToLowerInvariant(),
                    PasswordHash = string.Empty,
                    AvatarUrl = payload.Picture,
                    PhoneNumber = null,
                    Role = UserRole.Customer,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _userRepository.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();
            }
            else
            {
                if (!user.IsActive || user.DeletedAt.HasValue) return null;

                var changed = user.VerifiedCommerceEmail != payload.Email.Trim().ToLowerInvariant();
                user.VerifiedCommerceEmail = payload.Email.Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(payload.Name))
                {
                    user.FullName = payload.Name;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(user.AvatarUrl) && !string.IsNullOrWhiteSpace(payload.Picture))
                {
                    user.AvatarUrl = payload.Picture;
                    changed = true;
                }

                if (changed)
                {
                    await _unitOfWork.SaveChangesAsync();
                }
            }

            return await GenerateJwtTokenAsync(user);
        }

        private static UserDto ToUserDto(User user, bool hasMuaProfile)
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
                HasMuaProfile = hasMuaProfile,
                IsDemoAccount = user.IsDemoAccount
            };
        }

        private static string HashPassword(string password) => PasswordHasher.Hash(password);
        private static bool VerifyPasswordHash(string password, string? stored) => PasswordHasher.Verify(password, stored);

        private List<string> GetGoogleClientIds()
        {
            var clientIds = _configuration
                .GetSection("GoogleAuth:ClientIds")
                .Get<List<string>>() ?? new List<string>();

            var webClientId = _configuration["MuaWeb:GoogleClientId"];
            if (!string.IsNullOrWhiteSpace(webClientId)) clientIds.Add(webClientId);
            var singleClientId = _configuration["GoogleAuth:ClientId"];
            if (!string.IsNullOrWhiteSpace(singleClientId))
            {
                clientIds.Add(singleClientId);
            }

            return clientIds
                .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
                .Distinct()
                .ToList();
        }

        private string NewRefreshSession(User user, Guid family, DateTime expiresAt)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _dbContext.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), FamilyId = family,
                UserId = user.UserId, TokenHash = HashResetValue(raw), PasswordContext = HashResetValue(user.PasswordHash ?? ""), ExpiresAt = expiresAt });
            return raw;
        }

        public async Task<TokenDto?> RefreshAsync(string refreshToken)
        {
            var hash = HashResetValue(refreshToken);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var session = await _dbContext.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
            if (session == null) return null;
            var now = DateTime.UtcNow;
            var user = await _userRepository.GetByIdAsync(session.UserId);
            if (user == null || !user.IsActive || user.DeletedAt != null) {
                await _dbContext.RefreshSessions.Where(x => x.FamilyId == session.FamilyId).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                return null;
            }
            if (session.RevokedAt != null || session.ExpiresAt <= now ||
                session.PasswordContext != HashResetValue(user.PasswordHash ?? ""))
            {
                await _dbContext.RefreshSessions.Where(x => x.FamilyId == session.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
                await transaction.CommitAsync();
                return null;
            }
            // Atomic consume prevents two concurrent requests from rotating the same credential.
            var consumed = await _dbContext.RefreshSessions.Where(x => x.Id == session.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            if (consumed != 1) {
                await _dbContext.RefreshSessions.Where(x => x.FamilyId == session.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
                await transaction.CommitAsync();
                return null;
            }
            var result = await GenerateJwtTokenAsync(user, shortAccess: true);
            result.RefreshToken = NewRefreshSession(user, session.FamilyId, session.ExpiresAt);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        }

        public async Task RevokeRefreshAsync(string refreshToken)
        {
            var hash = HashResetValue(refreshToken);
            var family = await _dbContext.RefreshSessions.Where(x => x.TokenHash == hash).Select(x => (Guid?)x.FamilyId).SingleOrDefaultAsync();
            if (family != null) await _dbContext.RefreshSessions.Where(x => x.FamilyId == family).ExecuteDeleteAsync();
        }

        private async Task<TokenDto> GenerateJwtTokenAsync(User user, bool issueRefresh = false, bool shortAccess = false)
        {
            var jwtKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            var jwtIssuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
            var jwtAudience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
            var jwtDuration = double.Parse(_configuration["Jwt:DurationInMinutes"] ?? "1440");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var expiration = DateTime.UtcNow.AddMinutes(issueRefresh || shortAccess ? 15 : jwtDuration);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);

            if (issueRefresh)
                await _dbContext.RefreshSessions.Where(x => x.UserId == user.UserId && x.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync();
            var refresh = issueRefresh ? NewRefreshSession(user, Guid.NewGuid(), DateTime.UtcNow.AddDays(30)) : null;
            if (refresh != null) await _dbContext.SaveChangesAsync();
            return new TokenDto
            {
                RefreshToken = refresh,
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = expiration,
                UserId = user.UserId,
                FullName = user.FullName ?? "",
                Email = user.Email ?? "",
                Role = user.Role,
                HasMuaProfile = hasMuaProfile,
                IsDemoAccount = user.IsDemoAccount
            };
        }
    }
}
