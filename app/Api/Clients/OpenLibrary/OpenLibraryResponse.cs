using System.Text.Json.Serialization;

namespace FindBook.Api.Clients.OpenLibrary;

internal sealed class OpenLibraryResponse
{
    [JsonPropertyName("docs")]
    public OpenLibraryBook?[]? Docs { get; init; }
}

internal sealed class OpenLibraryBook
{
    public string? Key { get; init; }
    public string? Title { get; init; }

    [JsonPropertyName("author_name")]
    public string?[]? Authors { get; init; }

    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; init; }

    [JsonPropertyName("cover_i")]
    public int? CoverId { get; init; }

    public OpenLibraryEditions? Editions { get; init; }
}

internal sealed class OpenLibraryEditions
{
    public OpenLibraryEdition?[]? Docs { get; init; }
}

internal sealed class OpenLibraryEdition
{
    public string? Key { get; init; }
    public string? Title { get; init; }
}
