using DocShareAPI.Data;
using DocShareAPI.EmailServices;
using DocShareAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using System.Text;


var builder = WebApplication.CreateBuilder(args);



// Lấy chứng chỉ SSL từ biến môi trường
var sslCaCert = Environment.GetEnvironmentVariable("SSL_CA_CERT");

// Lấy chuỗi kết nối từ biến môi trường trước
string connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION");

// Nếu không có trong biến môi trường, lấy từ appsettings.json
if (string.IsNullOrEmpty(connectionString))
{
    connectionString = builder.Configuration.GetConnectionString("MysqlConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("MySQL connection string is not set in environment variables (MYSQL_CONNECTION) or appsettings.json (MysqlConnection).");
    }
}

if (!string.IsNullOrEmpty(sslCaCert))
{
    // Sử dụng file tạm thời an toàn
    string caCertPath = Path.GetTempFileName();
    try
    {
        File.WriteAllText(caCertPath, sslCaCert);

        // Sử dụng MySqlConnectionStringBuilder để thêm thông tin SSL
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            SslMode = MySqlSslMode.Required, // Hoặc Preferred tùy yêu cầu
            SslCa = caCertPath
        };
        connectionString = connectionStringBuilder.ToString();

        // Cấu hình DbContext với SSL
        builder.Services.AddDbContext<DocShareDbContext>(options =>
            options.UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 0, 29)),
                mySqlOptions => mySqlOptions.EnableRetryOnFailure()
            ));
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Failed to configure SSL certificate for database connection.", ex);
    }
    finally
    {
        // Xóa file tạm sau khi dùng xong
        if (File.Exists(caCertPath))
        {
            try { File.Delete(caCertPath); } catch { /* Bỏ qua lỗi xóa file */ }
        }
    }
}
else
{
    // Cấu hình DbContext không dùng SSL
    builder.Services.AddDbContext<DocShareDbContext>(options =>
        options.UseMySql(
            connectionString,
            new MySqlServerVersion(new Version(8, 0, 29)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure()
        ));
}


// Thêm CORS vào dịch vụ
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", builder =>
    {
        //builder.WithOrigins("http://localhost:3000") // Cho phép các trang web được sử dụng
        //       .AllowAnyHeader()
        //       .AllowAnyMethod();
        builder.AllowAnyOrigin() // Cho phép các trang web được sử dụng
               .AllowAnyHeader()
               .AllowAnyMethod();
    });
});

// Add services to the container.
builder.Services.AddControllers();

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

// Add services Http to the container.
builder.Services.AddHttpClient(); // Register HttpClient

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowSpecificOrigins");

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseTokenValidation();

app.MapControllers();

app.MapGet("/", () => "DocumentShare project is running.");
app.MapGet("/api", () => "Api project is running.");

app.Run();
