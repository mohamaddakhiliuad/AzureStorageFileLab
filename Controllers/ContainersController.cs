using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureStorageFileLab.Api.Contracts.Containers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AzureStorageFileLab.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContainersController : ControllerBase
    {
        private readonly BlobServiceClient _blobServiceClient;
        public ContainersController(BlobServiceClient blobServiceClient)
        {
            _blobServiceClient = blobServiceClient;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateContainerRequest createContainerRequest)
        {

            await _blobServiceClient.GetBlobContainerClient(createContainerRequest.Name).CreateIfNotExistsAsync();
            return NoContent();
        }
        [HttpGet]
        public async Task<ActionResult<List<string>>> GetContainersAsync()
        {
            List<string> containerNames = new List<string>();
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                containerNames.Add(container.Name);
            }
            return containerNames;
        }

        [HttpPost("{containerName}/blobs")]
        public async Task<IActionResult> UploadBlobAsync([FromRoute] string containerName, IFormFile? file, CancellationToken ct)

        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (file.Length == 0 || string.IsNullOrWhiteSpace(file.FileName))
            {
                return BadRequest();
            }
            try
            {
                var fileExtention = Path.GetExtension(file.FileName);
                var uniqueBlobName = $"{Guid.NewGuid()}{fileExtention}";
                var container = _blobServiceClient.GetBlobContainerClient(containerName);

                var blobClient = container.GetBlobClient(uniqueBlobName);
                using var stream = file.OpenReadStream();


                await blobClient.UploadAsync(stream, ct);
                return StatusCode(StatusCodes.Status201Created, new
                {
                    blobName = uniqueBlobName,
                    originalFileName = file.FileName
                });


            }
            catch (RequestFailedException ex) when (ex.Status == 404) // یا بررسی ارور ContainerNotFound
            {

                return NotFound(new { message = $"Container not found '{containerName}' " });
            }



        }
        [HttpGet("{containerName}/blobs")]
        public async Task<ActionResult<List<GetFileResult>>> GetBlobDetailsAsync([FromRoute] string containerName)
        {
            try
            {
                var bsc = _blobServiceClient.GetBlobContainerClient(containerName);
                List<GetFileResult> fileList = new List<GetFileResult>();
                await foreach (var blobItem in bsc.GetBlobsAsync())
                {
                    fileList.Add(new GetFileResult(blobItem.Name, blobItem.Properties.ContentLength));
                }
                ;

                return Ok(fileList);
            }
            catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "ContainerNotFound")
            {

                return NotFound(new { message = $"Container not found '{containerName}' " });
            }


        }


        [HttpGet("{containerName}/blobs/{blobName}")]
        public async Task<ActionResult<BlobDownloadStreamingResult>> DownloadAsync([FromRoute] string containerName, [FromRoute] string blobName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(containerName))
            {
                throw new BadHttpRequestException(nameof(containerName));
            }
            if (string.IsNullOrWhiteSpace(blobName))
            {
                throw new BadHttpRequestException(nameof(blobName));
            }
            try
            {

                var bsc = _blobServiceClient.GetBlobContainerClient(containerName); ;
                var response = await bsc.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: ct);
                return File(
     response.Value.Content,
     response.Value.Details.ContentType ?? "application/octet-stream",
     fileDownloadName: blobName);
            }
            catch (RequestFailedException ex) when (ex.Status == 404 && (ex.ErrorCode == "ContainerNotFound" || ex.ErrorCode == "BlobNotFound"))
            {
                return NotFound(new { message = $"not found '{containerName} or {blobName}'" });
            }
        }
    }
}