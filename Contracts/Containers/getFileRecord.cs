namespace AzureStorageFileLab.Api.Contracts.Containers
{
    public record GetFileResult( string BlobName, long? SizeInBytes);
    
}
