using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Safe baseline: no identity/tenant features are claimed until implemented and tested.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

// The correlation value is server-generated, never trusted from an incoming header.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Trace-Id"] =
            Activity.Current?.Id ?? context.TraceIdentifier;
        return Task.CompletedTask;
    });

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready"); // Dependency checks will be added with persistence.

app.MapGet("/api/v1/system/ping", () => TypedResults.Ok(new PingResponse("ok")))
    .WithName("SystemPing")
    .WithTags("System");

app.Run();

public sealed record PingResponse(string Status);

// Required for WebApplicationFactory integration tests.
public partial class Program;
