# Azure Storage File Lab — Learning Notes

Hands-on notes from building an ASP.NET Core API with Azure Blob Storage and Azurite. These notes cover the backend exercises completed so far. React, Table Storage, Queue Storage, and centralized exception handling are planned next steps.

## 1. Local development with Azurite

Azurite provides local Blob, Queue, and Table Storage endpoints.

```cmd
npm install -g azurite
azurite --version
mkdir "%USERPROFILE%\azurite-data"
cd /d "%USERPROFILE%\azurite-data"
azurite
```

Keep the terminal running while testing the API. The working directory holds the local emulator data.

| Service | Default local endpoint |
| --- | --- |
| Blob | http://127.0.0.1:10000 |
| Queue | http://127.0.0.1:10001 |
| Table | http://127.0.0.1:10002 |

During this exercise, the SDK requested an API version that Azurite rejected. We restarted the emulator with the following option.

```cmd
azurite --skipApiVersionCheck
```

This option bypasses the version check. It does not add support for missing service features.

## 2. Configuration and dependency injection

Install the Blob SDK from the API project directory.

```cmd
dotnet add package Azure.Storage.Blobs
```

Place `ConnectionStrings` at the root of `appsettings.Development.json`, alongside `Logging`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "AzureStorage": "UseDevelopmentStorage=true"
  }
}
```

Both expressions read the same setting.

```csharp
config["ConnectionStrings:AzureStorage"];
config.GetConnectionString("AzureStorage");
```

A colon separates configuration sections. Putting the setting inside `Logging` changes its path to `Logging:ConnectionStrings:AzureStorage`.

One Azurite connection string can configure Blob, Queue, and Table clients. An additional database connection can have its own key inside `ConnectionStrings`.

Register the reusable Blob client as a singleton before `builder.Build()`.

```csharp
builder.Services.AddSingleton<BlobServiceClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("AzureStorage")
        ?? throw new InvalidOperationException(
            "AzureStorage connection string is missing.");

    return new BlobServiceClient(connectionString);
});
```

## 3. The Blob client hierarchy

| Client | Responsibility |
| --- | --- |
| `BlobServiceClient` | Work with Blob Storage at account level |
| `BlobContainerClient` | Work with one container |
| `BlobClient` | Work with one blob |

```csharp
var container = blobServiceClient.GetBlobContainerClient(containerName);
var blob = container.GetBlobClient(blobName);
```

Getting these clients creates local objects. A service request happens when an operation such as `UploadAsync` executes, or when an asynchronous listing is enumerated.

Containers are peers within an account. In this lab, a name such as `reports/2026/sales.pdf` uses a blob-name prefix to represent folders.

## 4. Attribute routing

```csharp
[ApiController]
[Route("api/[controller]")]
public class ContainersController : ControllerBase
{
}
```

`[controller]` resolves to `Containers`. The action route is appended to the controller route.

| Action attribute | Endpoint |
| --- | --- |
| `[HttpPost]` | `POST /api/containers` |
| `[HttpGet]` | `GET /api/containers` |
| `[HttpPost("init")]` | `POST /api/containers/init` |
| `[HttpPost("{containerName}/blobs")]` | `POST /api/containers/{containerName}/blobs` |
| `[HttpGet("{containerName}/blobs")]` | `GET /api/containers/{containerName}/blobs` |
| `[HttpGet("{containerName}/blobs/{blobName}")]` | `GET /api/containers/{containerName}/blobs/{blobName}` |

These attributes determine the route independently of the method name. The single-segment `blobName` route fits the GUID-based blob names generated in this lab. Names containing folder separators need a route or query design that supports them.

## 5. Request models and validation

Keep HTTP request and response models under `Contracts`, grouped by feature.

Suggested request file location is `Contracts/Containers/CreateContainerRequest.cs`.

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateContainerRequest(
    [Required(ErrorMessage = "Container name is required.")]
    [StringLength(63, MinimumLength = 3,
        ErrorMessage = "Container name must be between 3 and 63 characters.")]
    [RegularExpression(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        ErrorMessage = "Use lowercase letters, numbers, and single hyphens between them.")]
    string Name);
```

For this positional record, validation metadata belongs on the constructor parameter. With `[ApiController]`, invalid model input produces a `400` response before the action executes.

A request DTO describes HTTP input. A command can represent an application operation when a separate application layer and handler are introduced.

## 6. Binding input

| Source | Example |
| --- | --- |
| Route | `[FromRoute] string containerName` |
| JSON body | `CreateContainerRequest request` inferred as body input with `[ApiController]` |
| Multipart form upload | `IFormFile? file` |
| Request cancellation | `CancellationToken ct` |

Use the route string when selecting an existing container. The create request model belongs to the JSON contract for creating a container.

## 7. Choosing an action return type

The SDK response describes what Storage returns to the controller. The action return type describes what the controller returns for HTTP processing.

| Type | Meaning |
| --- | --- |
| `IActionResult` | An interface for an executable MVC action result |
| `ActionResult` | The base class used by MVC results, implementing `IActionResult` |
| `ActionResult<T>` | A wrapper that accepts a value of type `T` or an MVC `ActionResult` |

Typical signatures in this project are shown below.

```csharp
Task<IActionResult> // Download a file or return an HTTP error
Task<IActionResult> // Create a container and return 204
Task<ActionResult<List<GetFileResult>>> // Return a list or an HTTP error
```

With `ActionResult<List<GetFileResult>>`, both statements are valid.

```csharp
return fileList;
return Ok(fileList);
```

With `IActionResult` or non-generic `ActionResult`, wrap data in an HTTP result.

```csharp
return Ok(fileList);
```

`File(...)` creates a `FileStreamResult` for a stream. This result can be returned through either `IActionResult` or `ActionResult`. `Task` adds asynchronous completion to the selected return type.

Calling `NoContent()` creates a result object. Use `return NoContent();` to return that result from the action.

## 8. HTTP responses

| Situation | Status | Example |
| --- | --- | --- |
| Successful list, including an empty list | 200 | `Ok(items)` |
| New blob uploaded | 201 | `StatusCode(StatusCodes.Status201Created, response)` |
| Container exists after create-if-missing | 204 | `NoContent()` |
| Invalid input | 400 | `BadRequest(new { message = "Invalid input." })` |
| Container or blob missing | 404 | `NotFound(new { message = "File not found." })` |

The container creation contract uses `204` both when the container is created and when it already exists.

Our upload response contains the saved blob name and the original filename.

```csharp
return StatusCode(StatusCodes.Status201Created, new
{
    blobName = uniqueBlobName,
    originalFileName = file.FileName
});
```

This returns a response body with status `201`. A future refinement can use `CreatedAtAction` to generate a `Location` header pointing to the download endpoint.

## 9. Uploading with streams

Validate the file before reading its members.

```csharp
if (file is null || file.Length == 0 ||
    string.IsNullOrWhiteSpace(file.FileName))
{
    return BadRequest(new { message = "Please select a non-empty file." });
}
```

Generate a fresh blob name for each upload.

```csharp
var extension = Path.GetExtension(file.FileName);
var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
```

`new Guid()` always produces the empty GUID. `Guid.NewGuid()` generates a new identifier.

```csharp
using var stream = file.OpenReadStream();
await blobClient.UploadAsync(stream, cancellationToken: ct);
```

The upload finishes before the stream is disposed. The API delegates file reading to a stream instead of explicitly copying the whole file into a byte array. ASP.NET Core may buffer the incoming multipart upload in memory or temporary storage.

The upload response alone does not persist the original filename. Preserving that name across later requests requires metadata or another data store.

## 10. Asynchronous listing

`GetBlobContainersAsync()` and `GetBlobsAsync()` return asynchronous, paginated sequences.

```csharp
var fileList = new List<GetFileResult>();

await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
{
    fileList.Add(new GetFileResult(
        blob.Name,
        blob.Properties.ContentLength));
}

return Ok(fileList);
```

The size in the SDK properties is nullable, so the response model can use `long?`.

```csharp
public record GetFileResult(string BlobName, long? SizeInBytes);
```

`await foreach` fetches additional pages as needed. Each item does not require a separate service request. The loop above still accumulates the complete output list in API memory.

Place `try` around the loop when handling listing errors because service requests happen during enumeration.

## 11. Downloading and stream ownership

```csharp
var response = await blobClient.DownloadStreamingAsync(
    cancellationToken: ct);

return File(
    response.Value.Content,
    response.Value.Details.ContentType ?? "application/octet-stream",
    fileDownloadName: blobName);
```

ASP.NET Core reads the stream while executing the file result, after the action returns. It disposes the stream after result execution. Keep the stream open when returning it through `File(...)`.

| Operation | Who consumes the stream? | Disposal |
| --- | --- | --- |
| Upload | The Storage SDK during the awaited upload | Use `using` in the action |
| Download | ASP.NET Core while writing the HTTP response | Transfer ownership through the file result |

`application/octet-stream` is a generic binary content type. This lab currently downloads files using their saved blob names.

## 12. Error handling and cancellation

Handle known storage errors by inspecting `RequestFailedException`.

```csharp
catch (RequestFailedException ex) when (
    ex.Status == 404 &&
    (ex.ErrorCode == "ContainerNotFound" ||
     ex.ErrorCode == "BlobNotFound"))
{
    return NotFound(new { message = "The requested file was not found." });
}
```

For container listing or upload, match `ContainerNotFound` specifically.

An existence check followed by an operation can race with deletion. Handling the actual operation's error covers that case and avoids a separate existence request.

Pass the action's cancellation token to asynchronous SDK operations. A broad catch can accidentally turn cancellation into a `500` response.

Centralized exception handling is a planned final exercise. It will log unexpected errors and produce consistent error responses. Avoid sending raw exception messages to the client. Errors during response streaming may happen after the action has returned and after response headers have been sent.

## 13. Manual verification checklist

- Create a valid container twice and receive `204` both times.
- Submit an invalid name such as `project--docs` and receive `400`.
- List containers and find the newly created name.
- Upload the same file twice and receive `201` with different blob names.
- List blobs and check their names and sizes.
- List an existing empty container and receive `200` with `[]`.
- Request an unknown container and receive `404`.
- Download an uploaded file and verify that it opens correctly.
- Download an unknown blob and receive `404`.

## 14. Next exercises

1. Connect the API to React for container selection, upload, listing, and download.
2. Store file metadata and processing status in Table Storage.
3. Send processing work through Queue Storage and consume it in a worker.
4. Practice duplicate message handling and recovery after partial failures.
5. Add centralized exception handling and review the API contracts.
6. Document local startup and publish a reproducible project on GitHub.
