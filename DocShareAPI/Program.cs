using DocShareAPI.Data;
using DocShareAPI.Helpers;
using DocShareAPI.Middleware;
using ELearningAPI.Helpers;
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
    //Lấy tên máy chủ MySQL từ biến môi trường
    var serverName = Environment.GetEnvironmentVariable("MYSQL_SERVER_NAME");
    //Lấy tên tài khoản MySQL từ biến môi trường
    var MySQLUserName = Environment.GetEnvironmentVariable("MYSQL_USER_NAME");
    //Lấy mật khẩu MySQL từ biến môi trường
    var MySQLPassword = Environment.GetEnvironmentVariable("MYSQL_PASSWORD");
    //Lấy tên database
    var MySQLDatabase = Environment.GetEnvironmentVariable("MYSQL_DATABASE");

    // Cập nhật chuỗi kết nối MySQL với đường dẫn chứng chỉ
    string connectionString = $"Server={serverName};Port=22588;Database={MySQLDatabase};" +
        $"User={MySQLUserName};Password={MySQLPassword};SslMode=REQUIRED;SslCa={caCertPath};";

    // Cấu hình DbContext với chuỗi kết nối MySQL
    builder.Services.AddDbContext<DocShareDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
}

// Đăng ký dịch vụ DbContext với MySQL. Sử dụng ở local
else
{
    builder.Services.AddDbContext<DocShareDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("MysqlConnection"),
        new MySqlServerVersion(new Version(8, 0, 29))));
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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
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
