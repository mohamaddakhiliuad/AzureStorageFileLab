
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace AzureStorageFileLab.Api.Contracts.Containers;

public record ContainerRequest(
    [FromRoute(Name = "containerName")]
    [Required(ErrorMessage = "Container name is required.")]
    [StringLength(63, MinimumLength = 3,
        ErrorMessage = "Container name must be between 3 and 63 characters.")]
    [RegularExpression(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        ErrorMessage = "Use lowercase letters, numbers, and single hyphens between them.")]
    string Name);
