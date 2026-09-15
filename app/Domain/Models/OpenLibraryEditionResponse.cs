using System.Text.Json;
using System.Text.Json.Serialization;

namespace FindBook.Domain.Models;

internal sealed class OpenLibraryEditionResponse
{
    public string? Key { get; init; }
    public string? Title { get; init; }
    public string? Subtitle { get; init; }

    [JsonPropertyName("publish_date")]
    public string? PublishDate { get; init; }

    [JsonPropertyName("edition_name")]
    public string? EditionName { get; init; }

    public string?[]? Contributions { get; init; }
    public JsonElement Notes { get; init; }
    public OpenLibraryWorkReference[]? Works { get; init; }
}

internal sealed class OpenLibraryWorkReference
{
    public string? Key { get; init; }
}
