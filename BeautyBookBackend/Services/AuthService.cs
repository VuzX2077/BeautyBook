using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<UserDto?> RegisterAsync(RegisterDto registerDto)
        {
            // Kiểm tra email trùng
            if (await _context.Users.AnyAsync(u => u.Email == registerDto.Email))
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
                Role = registerDto.Role,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _context.Users.AddAsync(user);

            // Tự động tạo ví ảo cho người dùng mới
            var wallet = new Wallet
            {
                WalletId = Guid.NewGuid(),
                UserId = user.UserId,
                Balance = 0,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.Wallets.AddAsync(wallet);

            // Nếu đăng ký làm MUA, tự động tạo hồ sơ Makeup Artist trống
            if (registerDto.Role == UserRole.MUA)
            {
                var muaProfile = new MakeupArtistProfile
                {
                    MUAId = user.UserId,
                    Bio = "Hãy viết vài dòng giới thiệu bản thân...",
                    ExperienceYears = 0,
                    RatingAverage = 5.0m, // Mặc định 5 sao cho người mới
                    TotalBookings = 0,
                    PortfolioCoverUrl = null
                };
                await _context.MakeupArtistProfiles.AddAsync(muaProfile);
            }

            await _context.SaveChangesAsync();

            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                IsActive = user.IsActive
            };
        }

        public async Task<TokenDto?> LoginAsync(LoginDto loginDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginDto.Email);
            if (user == null || user.PasswordHash != HashPassword(loginDto.Password))
            {
                return null;
            }

            return GenerateJwtToken(user);
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        private TokenDto GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "SuperSecretKeyForBeautyBookProject2026!KeepItSecret";
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "BeautyBookBackend";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "BeautyBookClients";
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

            return new TokenDto
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = expiration,
                UserId = user.UserId,
                FullName = user.FullName ?? "",
                Email = user.Email ?? "",
                Role = user.Role
            };
        }
    }
}
