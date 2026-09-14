using FindBook.Api.Clients.OpenLibrary;
using FindBook.Api.Configuration;
using FindBook.Api.Services;
using FindBook.Domain.Interfaces;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<OpenLibraryOptions>()
    .BindConfiguration("OpenLibrary")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<IBookCatalog, OpenLibraryClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FindBook/0.1 (+https://github.com/Taha7865/FindBook)");
});
builder.Services.AddScoped<BookSearchService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();
app.Run();
