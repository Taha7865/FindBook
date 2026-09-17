namespace FindBook.Domain.Models;

public record CatalogWork(string OpenLibraryWorkId, string[] AuthorIds)
{
    // The sole linked author, or the only author explicitly marked as primary by the work.
    public string? PrimaryAuthorId { get; init; }
}

public record CatalogAuthor(string OpenLibraryAuthorId, string Name, string[] AlternateNames);
