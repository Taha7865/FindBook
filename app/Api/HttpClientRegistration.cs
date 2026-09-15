using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace FindBook.Api;

public static class HttpClientRegistration
{
    public static IServiceCollection AddApiClientRetries(this IServiceCollection services, IConfiguration configuration)
    {
        const string sectionName = "ExternalApiRetry";
        services.AddOptions<HttpRetryStrategyOptions>(sectionName)
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // All factory clients share these settings. Each client's timeout also covers retry delays.
        services.ConfigureHttpClientDefaults(client => client.AddResilienceHandler("retry", (pipeline, context) =>
            pipeline.AddRetry(context.GetOptions<HttpRetryStrategyOptions>(sectionName))));

        return services;
    }
}
