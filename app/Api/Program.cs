using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Matching;
using FindBook.Domain.Validators;
using FindBook.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<OpenLibraryApiOptions>()
    .BindConfiguration(OpenLibraryApiOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
        "OpenLibraryApi:BaseUrl must be an absolute HTTP or HTTPS URL.")
    .Validate(options => options.TimeoutSettings > TimeSpan.Zero
        && options.TimeoutSettings <= TimeSpan.FromSeconds(60),
        "OpenLibraryApi:TimeoutSettings must be greater than zero and at most one minute.")
    .ValidateOnStart();
builder.Services.AddSingleton<IOpenLibraryApiOptions>(services =>
    services.GetRequiredService<IOptions<OpenLibraryApiOptions>>().Value);
builder.Services.AddHttpClient(OpenLibraryApiOptions.SectionName, (services, client) =>
{
    var options = services.GetRequiredService<IOpenLibraryApiOptions>();
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = options.TimeoutSettings;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FindBook/0.1 (+https://github.com/Taha7865/FindBook)");
});
builder.Services.AddTransient<IOpenLibraryApiClient, OpenLibraryApiClient>();
builder.Services.AddScoped<ISearchInterpretationValidator, SearchInterpretationValidator>();
builder.Services.AddScoped<BookMatcher>();
builder.Services.AddScoped<BookSearchService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();
app.Run();
