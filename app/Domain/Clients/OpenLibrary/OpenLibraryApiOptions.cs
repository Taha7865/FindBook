namespace FindBook.Domain.Clients.OpenLibrary;

public sealed class OpenLibraryApiOptions : HttpClientOptions, IOpenLibraryApiOptions
{
    public const string SectionName = "OpenLibraryApi";
}
