using Krake.Snippets.GlobalExceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); //
}
else
{
    app.UseExceptionHandler();
}

app.MapGet("/", () => "Hello Global Exception!");

app.MapGet("/error", () => { throw new Exception("Hello Global Exception"); });

app.Run();

namespace Krake.Snippets.GlobalExceptions
{
    internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
            CancellationToken cancellationToken = default)
        {
            logger.LogErrorUnhandledException(exception);

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
                Title = "Server failure",
                Extensions = new Dictionary<string, object?>
                {
                    { "traceId", httpContext.TraceIdentifier }
                }
            };

            httpContext.Response.StatusCode = problemDetails.Status.Value;

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

            return true;
        }
    }

    internal static partial class LoggerExtensions
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception occurred")]
        public static partial void LogErrorUnhandledException(this ILogger logger, Exception exception);
    }
}