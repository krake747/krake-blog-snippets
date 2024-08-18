using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog(static (ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration)); //
//builder.Services.AddSerilog(Log.Logger) //

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGet("/", () => "Hello Serilog!");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}