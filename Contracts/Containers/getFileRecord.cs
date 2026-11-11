namespace AzureStorageFileLab.Api.Contracts.Containers
{
    public record GetFileResult( string BlobName, string? originalFileName, long? SizeInBytes);
    
}
