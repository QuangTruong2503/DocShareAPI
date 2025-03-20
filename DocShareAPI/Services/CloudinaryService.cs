using CloudinaryDotNet;

namespace DocShareAPI.Services
{
    public interface ICloudinaryService
    {
        Cloudinary Cloudinary { get; }
        string CloudName { get; }
    }
    public class CloudinaryService : ICloudinaryService
    {
        public Cloudinary Cloudinary { get; }
        public string CloudName { get; }
        public CloudinaryService(IConfiguration configuration, ILogger<CloudinaryService> logger)
        {
            try
            {
                var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")
                    ?? configuration["Cloudinary:CloudName"];
                var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")
                    ?? configuration["Cloudinary:ApiKey"];
                var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
                    ?? configuration["Cloudinary:ApiSecret"];

                if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    throw new ArgumentNullException("Cloudinary configuration is incomplete");
                }

                Cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
                CloudName = cloudName;
            }
            catch (Exception ex)
            {
                logger.LogError($"Error initializing Cloudinary: {ex}");
                throw;
            }
        }
    }
}
