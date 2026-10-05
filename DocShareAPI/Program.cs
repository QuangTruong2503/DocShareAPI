using DocShareAPI.Data;
using DocShareAPI.EmailServices;
using DocShareAPI.Hubs;
using DocShareAPI.Services;
using DocShareAPI.Services.EmailServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024 * 1024;
});

var sslCaCert = Environment.GetEnvironmentVariable("SSL_CA_CERT");
if (!string.IsNullOrEmpty(sslCaCert))
{
    var caCertPath = "/tmp/ca.pem";
    System.IO.File.WriteAllText(caCertPath, sslCaCert);
}

var connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION")
    ?? builder.Configuration.GetConnectionString("MysqlConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("MYSQL_CONNECTION or ConnectionStrings: MysqlConnection must be configured.");
}

builder.Services.AddDbContext<DocShareDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 29)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

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

if (Encoding.UTF8.GetByteCount(secretKey) < 32)
    throw new InvalidOperationException("JWT signing key must contain at least 32 bytes.");

// Đăng ký TokenServices với secretKey
builder.Services.AddScoped<TokenServices>(_ => new TokenServices(secretKey));
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFolderPermissionService, FolderPermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

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


builder.Services.AddSingleton(OpenAIOptions.FromConfiguration(builder.Configuration));
builder.Services.AddHttpClient<IOpenAITextService, OpenAITextService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = Timeout.InfiniteTimeSpan; // Service applies an overall deadline, including retries.
});
builder.Services.AddHttpClient("ai-documents", client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IAIDocumentReader, AIDocumentReader>();
// ===== 2FA Services =====
builder.Services.AddDistributedMemoryCache(); // Cho 2FA cache
// Đăng ký HttpClient cho TwoFactorEmailService
builder.Services.AddHttpClient<ITwoFactorEmailService, TwoFactorEmailService>();

// Add services to the container.
var signalR = builder.Services.AddSignalR();
var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? builder.Configuration["Redis:Connection"];
if (!string.IsNullOrWhiteSpace(redisConnection)) signalR.AddStackExchangeRedis(redisConnection);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 64 * 1024 * 1024;
});
builder.Services.AddScoped<AssetDelivery>();
builder.Services.AddScoped<AssetDeliveryFilter>();
builder.Services.AddControllers(options => options.Filters.AddService<AssetDeliveryFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<AssetCleanupWorker>();
builder.Services.AddHostedService<NotificationDispatchWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = (context.Request.Path.Value ?? "").ToLowerInvariant();
        if (path.StartsWith("/api/public/ai/") || path.StartsWith("/api/public/gemini/"))
            return RateLimitPartition.GetFixedWindowLimiter($"ai:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
        if (!path.StartsWith("/api/users/public/") && !path.StartsWith("/api/verification/public/") && !path.EndsWith("/verify-password")) return RateLimitPartition.GetNoLimiter("other");
        return RateLimitPartition.GetFixedWindowLimiter($"{context.Connection.RemoteIpAddress}:{path}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
    });
});
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

if (!app.Environment.IsDevelopment())
{
    // Handle downstream failures inside CORS so browsers can read the error response.
    // The exception handler logs the cause and returns a generic Problem Details body.
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();

app.UseRateLimiter();
app.UseTokenValidation();
app.UseMiddleware<DocShareAPI.Middleware.MutationLockMiddleware>();

app.UseAuthorization();

app.MapGet("/api/public/health/ready", async (DocShareDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready", buildSha = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "local" }) : Results.StatusCode(503));
app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

app.MapGet("/", () => "DocumentShare project is running.");
app.MapGet("/api", () => "Api project is running.");

app.Run();
