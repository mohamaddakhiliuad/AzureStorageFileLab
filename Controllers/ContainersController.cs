using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
       ;
        private readonly long _maxFileSizeBytes;

        public ContainersController(BlobServiceClient blobServiceClient,IConfiguration configuration)
        {
            _blobServiceClient = blobServiceClient;
          
            _maxFileSizeBytes = configuration.GetValue<long>(
    "FileUpload:MaxFileSizeBytes");

            if (_maxFileSizeBytes <= 0)
            {
                throw new InvalidOperationException(
                    "FileUpload:MaxFileSizeBytes must be greater than zero.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(ContainerRequest createContainerRequest)
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
        public async Task<IActionResult> UploadBlobAsync([FromRoute]
        

        ContainerRequest containerRequest, IFormFile? file, CancellationToken ct)

        {
            if (file == null)
                return BadRequest();
            if (file.Length > _maxFileSizeBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge,
                    new { message = "The file exceeds the configured upload size limit." });
            }
            if (file.Length == 0 || string.IsNullOrWhiteSpace(file.FileName))
            {
                return BadRequest();
            }
            try
            {
                var fileExtention = Path.GetExtension(file.FileName);
                var uniqueBlobName = $"{Guid.NewGuid()}{fileExtention}";
                var container = _blobServiceClient.GetBlobContainerClient(containerRequest.Name);

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

                return NotFound(new { message = $"Container not found '{containerRequest.Name}' " });
            }



        }
        [HttpGet("{containerName}/blobs")]
        public async Task<ActionResult<List<GetFileResult>>> GetBlobDetailsAsync([FromRoute] ContainerRequest containerRequest)
        {
            try
            {
                var bsc = _blobServiceClient.GetBlobContainerClient(containerRequest.Name);
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

                return NotFound(new { message = $"Container not found '{containerRequest.Name}' " });
            }


        }


        [HttpGet("{containerName}/blobs/{blobName}")]
        public async Task<IActionResult> DownloadAsync([FromRoute] ContainerRequest containerRequest, [FromRoute] string blobName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(containerRequest.Name) || string.IsNullOrWhiteSpace(blobName))
                return BadRequest(new { message = "Container name and blob name are required." });
             
            try
            {

                var bsc = _blobServiceClient.GetBlobContainerClient(containerRequest.Name); ;
                var response = await bsc.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: ct);
                return File(
     response.Value.Content,
     response.Value.Details.ContentType ?? "application/octet-stream",
     fileDownloadName: blobName);
            }
            catch (RequestFailedException ex) when (ex.Status == 404 && (ex.ErrorCode == "ContainerNotFound" || ex.ErrorCode == "BlobNotFound"))
            {
                
                return NotFound(new { message = "Blob '{blobName}' was not found in container '{containerName}'." });
            }
        }
    }
}