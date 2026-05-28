using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using BeautyBookBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Build connection string from configuration and environment variables
var baseConn = builder.Configuration.GetConnectionString("DefaultConnection");
var sqlUser = Environment.GetEnvironmentVariable("DB_USER");
var sqlPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");

string connectionString;
if (!string.IsNullOrEmpty(sqlUser) && !string.IsNullOrEmpty(sqlPassword))
{
    // Use SQL Authentication if credentials provided via environment variables
    // Remove any existing User Id/Password from base and build new string
    var builderConn = new SqlConnectionStringBuilder(baseConn);
    builderConn.IntegratedSecurity = false;
    builderConn.UserID = sqlUser;
    builderConn.Password = sqlPassword;
    connectionString = builderConn.ToString();
}
else
{
    // Use default (Trusted Connection)
    connectionString = baseConn;
}

// Configure DbContext with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger to support JWT Bearer authorization
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BeautyBook API", Version = "v1" });
    
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter JWT Bearer token **_only_** (without 'Bearer ' prefix)",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };
    c.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKeyForBeautyBookProject2026!KeepItSecret";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BeautyBookBackend";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BeautyBookClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// Register Application Services (Dependency Injection)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMuaService, MuaService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IWalletService, WalletService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication(); // Must be called before UseAuthorization
app.UseAuthorization();

app.MapControllers();

app.Run();
