using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class ImageController : ControllerBase
{
    private readonly BlobServiceClient _blobServiceClient;

    public ImageController(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient; 
    }

    [HttpGet("images/{imageName}")]
    public async Task<IActionResult> GetImage(string imageName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient("images");
        var blobClient = containerClient.GetBlobClient(imageName);

        if (await blobClient.ExistsAsync())
        {
            var stream = new MemoryStream();
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return File(stream, "image/jpeg"); 
        }

        return NotFound();
    }
}
