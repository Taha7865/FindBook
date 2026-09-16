using FindBook.Domain.Models;

namespace FindBook.Domain.Validators;

public interface IBookSearchValidator
{
    void ValidateSearchSuggestions(BookSearchSuggestions suggestions);
    void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary);
}
