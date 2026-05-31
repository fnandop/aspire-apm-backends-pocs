using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();

builder.Services.AddProblemDetails();

builder.AddDefaultOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDefaultOpenApi();
}

var activitySource = new ActivitySource(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "api-service");

app.MapGet("/", () =>
{
    var backend = Environment.GetEnvironmentVariable("OBSERVABILITY_BACKEND") ?? "unknown";
    return Results.Ok($"api-service is running with backend: {backend}");
})
    .WithName("Root")
    .WithOpenApi(operation => new(operation) { Summary = "Root endpoint returning service status" });

app.MapGet("/health", () => Results.Ok("OK"))
    .WithName("Health")
    .WithOpenApi(operation => new(operation) { Summary = "Health check endpoint" });

app.MapGet("/error", () =>
{
    throw new Exception("Test exception for APM - this should appear in your observability backend");
})
.WithName("Error")
.WithOpenApi(operation => new(operation) { Summary = "Test error handling - throws an exception" });

app.MapGet("/slow", async () =>
{
    await Task.Delay(2000);
    return Results.Ok("slow response completed after 2 seconds");
})
.WithName("Slow")
.WithOpenApi(operation => new(operation) { Summary = "Simulates slow response (2 second delay)" });

app.MapGet("/outgoing", async (IHttpClientFactory httpClientFactory) =>
{
    var httpClient = httpClientFactory.CreateClient();
    var response = await httpClient.GetAsync("https://httpbin.org/get");
    var status = response.StatusCode.ToString();
    return Results.Ok($"Outgoing HTTP call completed with status: {status}");
})
.WithName("Outgoing")
.WithOpenApi(operation => new(operation) { Summary = "Makes outgoing HTTP call to httpbin.org" });

app.MapGet("/logs", (ILogger<Program> logger) =>
{
    logger.LogInformation("Information log from /logs endpoint");
    logger.LogWarning("Warning log from /logs endpoint");
    logger.LogError("Error log from /logs endpoint");
    return Results.Ok("Check your observability backend for info, warning, and error logs");
})
.WithName("Logs")
.WithOpenApi(operation => new(operation) { Summary = "Generates logs at Info, Warning, and Error levels" });

app.MapGet("/custom-span", (ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("custom-operation", ActivityKind.Internal);
    activity?.SetTag("custom.attribute", "custom-value");
    activity?.SetTag("operation.type", "demo");
    activity?.AddEvent(new ActivityEvent("custom-event", tags: new ActivityTagsCollection
    {
        { "event.attribute", "event-value" }
    }));

    logger.LogInformation("Custom span created with tags and event");

    return Results.Ok("Custom span with tags and events has been created");
})
.WithName("CustomSpan")
.WithOpenApi(operation => new(operation) { Summary = "Creates a custom Activity/span with tags and events" });

app.MapGet("/random", (ILogger<Program> logger) =>
{
    var random = Random.Shared.Next(3);

    using var activity = activitySource.StartActivity("random-operation", ActivityKind.Internal);
    activity?.SetTag("random.choice", random);

    switch (random)
    {
        case 0:
            logger.LogInformation("Random: success case");
            return Results.Ok("Random: success");

        case 1:
            logger.LogInformation("Random: slow case - starting 2 second delay");
            Thread.Sleep(2000);
            return Results.Ok("Random: slow response completed");

        default:
            logger.LogWarning("Random: error case");
            throw new Exception("Random error for APM testing");
    }
})
.WithName("Random")
.WithOpenApi(operation => new(operation) { Summary = "Randomly returns success, slow response, or error" });

app.MapDefaultEndpoints();

app.Run();