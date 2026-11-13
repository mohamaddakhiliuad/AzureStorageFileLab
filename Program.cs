using System.Data.Common;
using Azure.Identity;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<BlobServiceClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var blobServiceUri = config["AzureStorage:BlobServiceUri"];

    if (!string.IsNullOrWhiteSpace(blobServiceUri))
    {
        return new BlobServiceClient(
            new Uri(blobServiceUri),
            new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }

    var connectionString = config.GetConnectionString("AzureStorage")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:AzureStorage is missing.");

    return new BlobServiceClient(connectionString);
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()|| app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
