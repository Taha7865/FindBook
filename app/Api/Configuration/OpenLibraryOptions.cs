using System.ComponentModel.DataAnnotations;

namespace FindBook.Api.Configuration;

public sealed class OpenLibraryOptions
{
    [Required, Url]
    public string BaseUrl { get; init; } = "https://openlibrary.org/";

    [Range(1, 60)]
    public int TimeoutSeconds { get; init; } = 15;
}
