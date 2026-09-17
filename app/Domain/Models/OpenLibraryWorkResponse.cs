using System.Text.Json.Serialization;

namespace FindBook.Domain.Models;

internal class OpenLibraryWorkResponse
{
    public string? Key { get; init; }
    public OpenLibraryAuthorRole?[]? Authors { get; init; }
}

internal class OpenLibraryAuthorRole
{
    public OpenLibraryAuthorReference? Author { get; init; }
    public string? Role { get; init; }
}

internal class OpenLibraryAuthorReference
{
    public string? Key { get; init; }
}

internal class OpenLibraryAuthorResponse
{
    public string? Key { get; init; }
    public string? Name { get; init; }

    [JsonPropertyName("alternate_names")]
    public string?[]? AlternateNames { get; init; }
}
