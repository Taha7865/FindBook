namespace FindBook.Domain.Models;

public record CatalogWork(string OpenLibraryWorkId, string[] AuthorIds);

public record CatalogAuthor(string OpenLibraryAuthorId, string Name, string[] AlternateNames);
