using System.ComponentModel.DataAnnotations;

namespace FindBook.Domain.Clients;

public abstract class HttpClientOptions
{
    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    public TimeSpan TimeoutSettings { get; init; }
}
