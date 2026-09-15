using System.ComponentModel.DataAnnotations;

namespace FindBook.Domain.Clients.OpenLibrary;

public sealed class OpenLibraryApiOptions : HttpClientOptions, IOpenLibraryApiOptions
{
    public const string SectionName = "OpenLibraryApi";

    [Range(1, 20)]
    public int MaxEditionLookups { get; init; }
}
