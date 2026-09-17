using System.Net;
using System.Text;
using FindBook.Domain.Clients.OpenLibrary;

namespace FindBook.Tests;

public class OpenLibraryAuthorTests
{
    [Theory]
    [InlineData("[{\"author\":{\"key\":\"/authors/OL1A\"}}]", "OL1A")]
    [InlineData("[{\"author\":{\"key\":\"/authors/OL1A\"}},{\"author\":{\"key\":\"/authors/OL2A\"}}]", null)]
    [InlineData("[{\"author\":{\"key\":\"/authors/OL1A\"}},{\"author\":{\"key\":\"/authors/OL2A\"},\"role\":\"Primary Author\"}]", "OL2A")]
    [InlineData("[{\"author\":{\"key\":\"/authors/OL1A\"},\"role\":\"primary author\"},{\"author\":{\"key\":\"/authors/OL2A\"},\"role\":\"primary author\"}]", null)]
    [InlineData("[{\"author\":{\"key\":\"/authors/OL1A\"}},null]", null)]
    [InlineData("[]", null)]
    [InlineData("null", null)]
    public async Task Reads_a_primary_author_only_from_a_complete_unambiguous_work_author_list(
        string authorsJson, string? expectedPrimaryAuthorId)
    {
        var response = "{\"key\":\"/works/OL1W\",\"authors\":" + authorsJson + "}";
        var client = new OpenLibraryApiClient(new StubFactory(response), new OpenLibraryApiOptions());

        var work = await client.GetWorkAsync("OL1W", default);

        Assert.NotNull(work);
        Assert.Equal(expectedPrimaryAuthorId, work.PrimaryAuthorId);
    }

    private class StubFactory(string response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHandler(response)) { BaseAddress = new Uri("https://openlibrary.org/") };
    }

    private class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response, Encoding.UTF8, "application/json") });
    }
}
