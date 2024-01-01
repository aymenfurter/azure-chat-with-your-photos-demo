using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using AzureChatWithPhotos.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureChatWithPhotos.Services
{
    public class BlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<BlobStorageService> _logger;
        private readonly string _imageContainerName;

        public BlobStorageService(BlobServiceClient blobServiceClient, 
                                  ILogger<BlobStorageService> logger, 
                                  IConfiguration configuration)
        {
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _imageContainerName = configuration.GetValue<string>("BlobContainers:Images") ?? "images";
        }

        public async Task<Stream> GetImageStreamAsync(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
            {
                throw new ArgumentException("Image name must be provided.", nameof(imageName));
            }

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_imageContainerName);
                var blobClient = containerClient.GetBlobClient(imageName);

                if (await blobClient.ExistsAsync())
                {
                    var stream = new MemoryStream();
                    await blobClient.DownloadToAsync(stream);
                    stream.Position = 0;
                    return stream;
                }

                throw new BlobNotFoundException($"Image {imageName} not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving image {imageName}.");
                throw;
            }
        }
    }
}