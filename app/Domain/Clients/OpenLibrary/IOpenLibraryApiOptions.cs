namespace FindBook.Domain.Clients.OpenLibrary;

public interface IOpenLibraryApiOptions
{
    string BaseUrl { get; }
    TimeSpan TimeoutSettings { get; }
}
