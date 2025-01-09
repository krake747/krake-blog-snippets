using Krake.Snippets.FeatureFlags;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement(builder.Configuration.GetSection("FeatureFlags"));

var app = builder.Build();

app.MapGet("/", () => "Hello Feature Flags!");

app.MapGet("feature-a", static () => Results.Ok("Hello from Feature A"));

app.MapGet("feature-b", static async ([FromServices] IFeatureManager manager) =>
{
    if (await manager.IsEnabledAsync("FeatureB") is false)
    {
        return Results.NotFound();
    }

    return Results.Ok("Hello from Feature B");
});

app.MapGet("feature-c", () => Results.Ok("Hello from Feature C"))
    .AddEndpointFilter(static async (context, next) =>
    {
        var featureManager = context.HttpContext.RequestServices.GetRequiredService<IFeatureManager>();
        if (await featureManager.IsEnabledAsync("FeatureC") is false)
        {
            return Results.NotFound();
        }

        return await next(context);
    });

app.MapGet("feature-d", () => Results.Ok("Hello from Feature D"))
    .AddEndpointFilter<FeatureFilter>();

// Return all feature flags and their statuses
app.MapGet("feature-flags", async (IFeatureManager manager, CancellationToken token = default) =>
{
    Dictionary<string, bool> featureFlags = [];
    await foreach (var featureName in manager.GetFeatureNamesAsync())
    {
        featureFlags[featureName] = await manager.IsEnabledAsync(featureName, token);
    }

    return Results.Ok(new { FeatureFlags = featureFlags });
});

// Return the value of a specific feature flag (true/false)
app.MapGet("feature-flags/{featureName}",
    static async ([FromServices] IFeatureManager manager, [FromRoute] string featureName,
            CancellationToken token = default) =>
        await manager.IsEnabledAsync(featureName, token)
);

// Return 404 if a specific feature flag is not enabled else OK 200
app.MapGet("/feature-flags/{featureName}/status",
    async ([FromServices] IFeatureManager manager, string featureName, CancellationToken token = default) =>
    await manager.IsEnabledAsync(featureName, token) is false
        ? Results.NotFound(new { Message = $"Feature flag '{featureName}' is not enabled or does not exist." })
        : Results.Ok(new { Message = $"Feature flag '{featureName}' is enabled." })
);

app.Run();

namespace Krake.Snippets.FeatureFlags
{
    internal sealed class FeatureFilter(IFeatureManager featureManager) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
            EndpointFilterDelegate next)
        {
            if (await featureManager.IsEnabledAsync("FeatureD") is false)
            {
                return Results.NotFound();
            }

            return await next(context);
        }
    }
}