using Krake.Snippets.FeatureFlags;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatureManagement(builder.Configuration.GetSection("FeatureFlags"));

var app = builder.Build();

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