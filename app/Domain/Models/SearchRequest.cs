using System.ComponentModel.DataAnnotations;

namespace FindBook.Domain.Models;

public sealed class SearchRequest
{
    [Required(ErrorMessage = "Enter a title, author, or a few book details.")]
    [StringLength(1000, ErrorMessage = "Use 1,000 characters or fewer.")]
    public string Query { get; init; } = string.Empty;
}
