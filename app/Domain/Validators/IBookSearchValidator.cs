using FindBook.Domain.Models;

namespace FindBook.Domain.Validators;

public interface IBookSearchValidator
{
    void ValidateSearchTerms(BookSearchTerms searchTerms);
    void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary);
}
