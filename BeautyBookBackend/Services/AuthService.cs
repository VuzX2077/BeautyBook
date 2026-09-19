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

        public AuthService(
            IUserRepository userRepository,
            IMuaRepository muaRepository,
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            ApplicationDbContext dbContext)
        {
            _userRepository = userRepository;
            _muaRepository = muaRepository;
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            if (await _userRepository.EmailExistsAsync(registerDto.Email))
            {
                return null;
            }

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

        public async Task<TokenDto?> LoginAsync(LoginDto loginDto)
        {
            var user = await _userRepository.GetByEmailAsync(loginDto.Email);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue || user.PasswordHash != HashPassword(loginDto.Password))
            {
                return null;
            }

            return await GenerateJwtTokenAsync(user);
        }

        public async Task<TokenDto?> BecomeMuaAsync(Guid userId)
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

            var profile = await _dbContext.MakeupArtistProfiles.FirstOrDefaultAsync(x => x.MUAId == user.UserId);
            if (profile == null)
            {
                await _muaRepository.AddProfileAsync(new MakeupArtistProfile
                {
                    MUAId = user.UserId,
                    ExperienceYears = 0,
                    AverageRating = 0,
                    TotalBookings = 0,
                    Status = MuaStatus.Draft
                });
            }

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

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
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
