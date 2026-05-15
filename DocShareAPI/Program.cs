using DocShareAPI.Data;
using DocShareAPI.EmailServices;
using DocShareAPI.Services;
using DocShareAPI.Services.EmailServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Lấy chứng chỉ SSL từ biến môi trường
var sslCaCert = Environment.GetEnvironmentVariable("SSL_CA_CERT");
if (!string.IsNullOrEmpty(sslCaCert))
{
    // Tạo file tạm thời chứa nội dung chứng chỉ
    var caCertPath = "/tmp/ca.pem";  // Đường dẫn tạm thời
    System.IO.File.WriteAllText(caCertPath, sslCaCert);

    // Lấy chuỗi kết nối MySQL từ biến môi trường
    string? connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("MYSQL_CONNECTION environment variable is not set.");
    }

    // Cấu hình DbContext với chuỗi kết nối MySQL và SSL
    builder.Services.AddDbContext<DocShareDbContext>(options =>
        options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 29)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure()
        ));
}
else
{
    // Lấy chuỗi kết nối MySQL từ appsettings.json
    string? connectionString = builder.Configuration.GetConnectionString("MysqlConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("MysqlConnection is not configured in appsettings.json.");
    }

    // Cấu hình DbContext với chuỗi kết nối MySQL
    builder.Services.AddDbContext<DocShareDbContext>(options =>
        options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 29))));
}

// Thêm CORS vào dịch vụ
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", corsBuilder =>
    {
        var configuredOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")
            ?? builder.Configuration["Cors:AllowedOrigins"];

        var allowedOrigins = configuredOrigins?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            corsBuilder.WithOrigins(allowedOrigins)
                       .AllowAnyHeader()
                       .AllowAnyMethod();
        }
        else if (builder.Environment.IsDevelopment())
        {
            corsBuilder.AllowAnyOrigin()
                       .AllowAnyHeader()
                       .AllowAnyMethod();
        }
        else
        {
            throw new InvalidOperationException("CORS_ALLOWED_ORIGINS or Cors:AllowedOrigins must be configured outside Development.");
        }
    });
});

// Learn more about configuring Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });

    // Thêm cấu hình Authorization (Bearer Token)
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập token đăng nhập"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] {}
        }
    });
});

// Lấy secretKey từ biến môi trường hoặc configuration
var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? builder.Configuration["TokenSecretKey"]
    ?? throw new InvalidOperationException("JWT SecretKey is not configured.");

// Đăng ký TokenServices với secretKey
builder.Services.AddScoped<TokenServices>(_ => new TokenServices(secretKey));

// Thêm dịch vụ xác thực JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

//Thêm dịch vụ Cloudinary
builder.Services.AddSingleton<ICloudinaryService, CloudinaryService>();
//Gmail
builder.Services.AddScoped<VerifyEmailService>();
builder.Services.AddScoped<ResetPasswordEmailService>();
builder.Services.AddScoped<TwoFactorEmailService>();

// Add services Http to the container.
builder.Services.AddHttpClient(); // Register HttpClient


// Add the GeminiAIOptions configuration
builder.Services.Configure<GeminiAIOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? builder.Configuration["GeminiApiKey"]
        ?? throw new InvalidOperationException("Gemini API Key is required.");
});
// ===== 2FA Services =====
builder.Services.AddDistributedMemoryCache(); // Cho 2FA cache
// Đăng ký HttpClient cho TwoFactorEmailService
builder.Services.AddHttpClient<ITwoFactorEmailService, TwoFactorEmailService>();

// Add services to the container.
builder.Services.AddControllers();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

app.UseCors("AllowSpecificOrigins");

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseTokenValidation();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "DocumentShare project is running.");
app.MapGet("/api", () => "Api project is running.");

app.Run();
