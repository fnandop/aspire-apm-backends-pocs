using System.Diagnostics;
using System.Net.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;
DistributedContextPropagator.Current = DistributedContextPropagator.CreateDefaultPropagator();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

static string NormalizeAddress(string? address, string serviceName)
{
    if (string.IsNullOrWhiteSpace(address))
    {
        throw new InvalidOperationException($"Service discovery address for '{serviceName}' was not configured by Aspire.");
    }

    return address.EndsWith('/') ? address : address + "/";
}

var dotnetApiAddress = NormalizeAddress(builder.Configuration["services:api-service:http:0"], "api-service");
var nodeApiAddress = NormalizeAddress(builder.Configuration["services:node-api:http:0"], "node-api");
var springApiAddress = NormalizeAddress(builder.Configuration["services:spring-boot-api:http:0"], "spring-boot-api");

var routes = new[]
{
    CreateRoute("dotnet-route", "dotnet-cluster", "/api/dotnet/{**catch-all}", "/api/dotnet"),
    CreateRoute("node-route", "node-cluster", "/api/node/{**catch-all}", "/api/node"),
    CreateRoute("spring-route", "spring-cluster", "/api/spring/{**catch-all}", "/api/spring")
};

var clusters = new[]
{
    CreateCluster("dotnet-cluster", "api-service", dotnetApiAddress),
    CreateCluster("node-cluster", "node-api", nodeApiAddress),
    CreateCluster("spring-cluster", "spring-boot-api", springApiAddress)
};

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddTransforms(context =>
    {
        context.AddRequestTransform(transformContext =>
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return ValueTask.CompletedTask;
            }

            RemovePropagationHeaders(transformContext.ProxyRequest);
            DistributedContextPropagator.Current.Inject(activity, transformContext.ProxyRequest, static (object? request, string name, string value) =>
            {
                if (request is HttpRequestMessage httpRequest)
                {
                    httpRequest.Headers.TryAddWithoutValidation(name, value);
                }
            });

            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    service = "gateway",
    routes = new[]
    {
        "/api/dotnet/{**catch-all}",
        "/api/node/{**catch-all}",
        "/api/spring/{**catch-all}"
    }
}));

app.Use(async (context, next) =>
{
    await next();

    var activity = Activity.Current;
    if (activity is not null && !context.Response.HasStarted)
    {
        context.Response.Headers["x-gateway-trace-id"] = activity.TraceId.ToString();
        context.Response.Headers["x-gateway-span-id"] = activity.SpanId.ToString();
    }
});

app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();

static void RemovePropagationHeaders(HttpRequestMessage request)
{
    request.Headers.Remove("traceparent");
    request.Headers.Remove("tracestate");
    request.Headers.Remove("baggage");
}

static RouteConfig CreateRoute(string routeId, string clusterId, string path, string prefix) => new()
{
    RouteId = routeId,
    ClusterId = clusterId,
    Match = new RouteMatch { Path = path },
    Transforms = new[]
    {
        new Dictionary<string, string>
        {
            ["PathRemovePrefix"] = prefix
        }
    }
};

static ClusterConfig CreateCluster(string clusterId, string destinationId, string address) => new()
{
    ClusterId = clusterId,
    Destinations = new Dictionary<string, DestinationConfig>
    {
        [destinationId] = new() { Address = address }
    }
};

