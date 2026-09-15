using System.Text.Json.Serialization;

namespace FindBook.Domain.Models;

public sealed record BookSelection([property: JsonRequired] SelectedBook[] Books);

public sealed record SelectedBook(
    [property: JsonRequired] string OpenLibraryWorkId,
    [property: JsonRequired] string Explanation);
