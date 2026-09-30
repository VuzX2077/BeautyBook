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
        private readonly IEmailSender _emailSender;

        public AuthService(
            IUserRepository userRepository,
            IMuaRepository muaRepository,
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            ApplicationDbContext dbContext,
            IEmailSender emailSender)
        {
            _userRepository = userRepository;
            _muaRepository = muaRepository;
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _dbContext = dbContext;
            _emailSender = emailSender;
        }

        public async Task<string?> SendRegistrationOtpAsync(string email)
        {
            email = NormalizeEmail(email);
            if (await _userRepository.EmailExistsAsync(email)) return "EMAIL_EXISTS";
            await IssueOtpAsync(email, "REGISTER");
            return null;
        }

        public async Task SendPasswordResetOtpAsync(string email)
        {
            email = NormalizeEmail(email);
            var user = await _userRepository.GetByEmailAsync(email);
            if (user != null && user.IsActive && !user.DeletedAt.HasValue && !string.IsNullOrWhiteSpace(user.PasswordHash))
                await IssueOtpAsync(email, "RESET_PASSWORD");
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            registerDto.Email = NormalizeEmail(registerDto.Email);
            if (await _userRepository.EmailExistsAsync(registerDto.Email))
            {
                return null;
            }

            var otp = await ConsumeOtpAsync(registerDto.Email, "REGISTER", registerDto.Otp);
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
            if (user == null || !user.IsActive || user.DeletedAt.HasValue || string.IsNullOrWhiteSpace(user.PasswordHash)) return false;
            if (!await ConsumeOtpAsync(email, "RESET_PASSWORD", request.Otp)) return false;
            user.PasswordHash = HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue ||
                !VerifyPasswordHash(request.CurrentPassword, user.PasswordHash)) return false;
            user.PasswordHash = HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private async Task IssueOtpAsync(string email, string purpose)
        {
            var now = DateTime.UtcNow;
            var latest = await _dbContext.EmailOtps
                .Where(x => x.Email == email && x.Purpose == purpose)
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            var cooldownSeconds = purpose == "REGISTER" ? 45 : 60;
            if (latest != null && latest.CreatedAt > now.AddSeconds(-cooldownSeconds))
                throw new OtpCooldownException(cooldownSeconds);

            var active = await _dbContext.EmailOtps
                .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null).ToListAsync();
            foreach (var item in active) item.UsedAt = now;

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var issuedOtp = new EmailOtp
            {
                Id = Guid.NewGuid(), Email = email, Purpose = purpose,
                CodeHash = HashOtp(email, purpose, code), CreatedAt = now, ExpiresAt = now.AddMinutes(5)
            };
            _dbContext.EmailOtps.Add(issuedOtp);
            await _dbContext.SaveChangesAsync();
            try
            {
                await _emailSender.SendOtpAsync(email, code, purpose);
            }
            catch (EmailDeliveryException)
            {
                // A failed request must not leave a usable code or impose the
                // successful-send cooldown. Previous codes stay invalidated.
                _dbContext.EmailOtps.Remove(issuedOtp);
                await _dbContext.SaveChangesAsync();
                throw;
            }
        }

        private async Task<bool> ConsumeOtpAsync(string email, string purpose, string code)
        {
            var now = DateTime.UtcNow;
            var otp = await _dbContext.EmailOtps.Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            if (otp == null || otp.ExpiresAt <= now || otp.FailedAttempts >= 5) return false;
            var valid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(HashOtp(email, purpose, code)));
            if (!valid) { otp.FailedAttempts++; await _dbContext.SaveChangesAsync(); return false; }
            otp.UsedAt = now;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        private string HashOtp(string email, string purpose, string code)
        {
            var secret = _configuration["Otp:HashKey"] ?? _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Otp:HashKey is not configured.");
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{email}|{purpose}|{code}")));
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
                HasMuaProfile = hasMuaProfile
            };
        }

        private static string HashPassword(string password)
        {
            const int iterations = 210_000;
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPasswordHash(string password, string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            if (stored.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                var parts = stored.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
                try
                {
                    var salt = Convert.FromBase64String(parts[2]);
                    var expected = Convert.FromBase64String(parts[3]);
                    var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                    return CryptographicOperations.FixedTimeEquals(expected, actual);
                }
                catch (FormatException) { return false; }
            }
            using var sha256 = SHA256.Create();
            var legacy = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(stored), legacy); }
            catch (FormatException) { return false; }
        }

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
                HasMuaProfile = hasMuaProfile
            };
        }
    }
}
