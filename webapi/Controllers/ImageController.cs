using System;
using System.Threading.Tasks;
using AzureChatWithPhotos.Services;
using AzureChatWithPhotos.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AzureChatWithPhotos.Controllers
{
    [ApiController]
    [Route("images")] // Simplified route
    public class ImageController : ControllerBase
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly ILogger<ImageController> _logger;

        public ImageController(BlobStorageService blobStorageService, ILogger<ImageController> logger)
        {
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{imageName}")]
        public async Task<IActionResult> GetImage(string imageName)
        {
            try
            {
                var imageStream = await _blobStorageService.GetImageStreamAsync(imageName);
                _logger.LogInformation($"Image {imageName} retrieved successfully.");
                return File(imageStream, "image/jpeg");
            }
            catch (BlobNotFoundException ex)
            {
                _logger.LogInformation(ex.Message);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving image {imageName}.");
                return StatusCode(500, "An error occurred while retrieving the image.");
            }
        }
    }
}