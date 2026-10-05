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

            return await GenerateJwtTokenAsync(user);
        }

        public async Task<bool> VerifyPasswordAsync(Guid userId, string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return false;
            return VerifyPasswordHash(password, user.PasswordHash);
        }

        public async Task<TokenDto?> BecomeMuaAsync(Guid userId, MuaApplicationRequestDto request)
        {
            if (!WorkLocationPolicy.ValidCoordinates(request.Latitude, request.Longitude)) return null;
            if ((request.WorkLocationAddress != null || request.WorkLocationName != null || request.AllowCustomerVisit)
                && !WorkLocationPolicy.Valid(request.WorkLocationName, request.WorkLocationAddress, request.Latitude, request.Longitude, request.OperatingLocationConfirmed, request.AllowCustomerVisit)) return null;
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || (user.Role != UserRole.Customer && user.Role != UserRole.MUA))
            {
                return null;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var lockKey = $"mua-onboarding:{user.UserId:N}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");

            var styleIds = request.StyleIds.Distinct().ToList();
            var validStyleIds = await _dbContext.MakeupStyles
                .Where(style => styleIds.Contains(style.StyleId) && style.IsActive)
                .Select(style => style.StyleId)
                .ToListAsync();
            if (styleIds.Count == 0 || request.StyleIds.Count > 5 || validStyleIds.Count != styleIds.Count)
            {
                await transaction.RollbackAsync();
                return null;
            }

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
                await _muaRepository.AddProfileAsync(profile);
            }
            else if (profile.VerificationStatus is MuaVerificationStatus.PendingReview or MuaVerificationStatus.Approved)
            {
                await transaction.RollbackAsync();
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

            var oldStyles = await _muaRepository.GetStyleLinksByMuaIdAsync(userId);
            _muaRepository.RemoveStyleLinks(oldStyles);
            foreach (var styleId in validStyleIds)
                await _muaRepository.AddMuaStyleAsync(new MUAStyle { MUAId = userId, StyleId = styleId });

            user.Role = UserRole.MUA;
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

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

                var changed = false;

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

        private async Task<TokenDto> GenerateJwtTokenAsync(User user)
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
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var expiration = DateTime.UtcNow.AddMinutes(jwtDuration);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);

            return new TokenDto
            {
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
