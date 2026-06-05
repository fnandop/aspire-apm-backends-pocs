using Aspire.Hosting.ApplicationModel;

internal static class ApplicationComponentsExtensions
{
    public static ApplicationResources AddApplicationComponents(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability)
    {
        var (mssql, apiService) = builder.AddApiService(observability);
        var (mongo, nodeApi) = builder.AddNodeApi(paths, observability);
        var (postgres, springApi) = builder.AddSpringBootApi(paths, observability);

        var gateway = builder.AddProject<Projects.AspireApmBackendsDemo_Gateway>(ResourceNames.Gateway)
            .WithExternalHttpEndpoints()
            .WithHttpEndpoint(name: ResourceNames.HttpEndpoint, isProxied: false)
            .WithEnvironment("OTEL_SERVICE_NAME", ResourceNames.Gateway)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", observability.OtelCollector.GetEndpoint(ResourceNames.OtlpGrpcEndpoint))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OBSERVABILITY_BACKEND", observability.Backend)
            .WithReference(apiService)
            .WithReference(nodeApi.GetEndpoint(ResourceNames.HttpEndpoint))
            .WithReference(springApi.GetEndpoint(ResourceNames.HttpEndpoint))
            .WithHttpHealthCheck("/health")
            .WaitFor(apiService)
            .WaitFor(nodeApi)
            .WaitFor(springApi)
            .WaitFor(observability.OtelCollector);

        var reactUi = builder.AddDockerfile(ResourceNames.ReactUi, Path.Combine(paths.SourceDir, "ReactUi"))
            .WithHttpEndpoint(targetPort: ResourceNames.ReactUiPort, name: ResourceNames.HttpEndpoint, isProxied: false)
            .WithExternalHttpEndpoints()
            .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint(ResourceNames.HttpEndpoint))
            .WaitFor(gateway);

        return new ApplicationResources(
            mssql,
            mongo,
            postgres,
            nodeApi,
            springApi,
            apiService,
            gateway,
            reactUi);
    }

    /// <summary>Node.js API + MongoDB</summary>
    private static (IResourceBuilder<MongoDBDatabaseResource> Mongo, IResourceBuilder<ContainerResource> NodeApi) AddNodeApi(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability)
    {
        var mongoServer = builder.AddMongoDB(ResourceNames.MongoServer);
        var mongo = mongoServer.AddDatabase(ResourceNames.Mongo, ResourceNames.ObservabilityDatabase);

        var nodeApi = builder.AddDockerfile(ResourceNames.NodeApi, Path.Combine(paths.SourceDir, "NodeApi"))
            .WithHttpEndpoint(targetPort: ResourceNames.NodeApiPort, name: ResourceNames.HttpEndpoint, isProxied: false)
            .WithEnvironment("PORT", ResourceNames.NodeApiPort.ToString())
            .WithEnvironment("OTEL_SERVICE_NAME", ResourceNames.NodeApi)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", observability.OtelCollector.GetEndpoint(ResourceNames.OtlpGrpcEndpoint))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithReference(mongo)
            .WaitFor(mongo)
            .WaitFor(observability.OtelCollector);

        return (mongo, nodeApi);
    }

    /// <summary>Spring Boot API + PostgreSQL</summary>
    private static (IResourceBuilder<PostgresDatabaseResource> Postgres, IResourceBuilder<ContainerResource> SpringApi) AddSpringBootApi(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability)
    {
        var postgresServer = builder.AddPostgres(ResourceNames.PostgresServer);
        var postgres = postgresServer.AddDatabase(ResourceNames.Postgres, ResourceNames.ObservabilityDatabase);

        var springApi = builder.AddDockerfile(ResourceNames.SpringBootApi, Path.Combine(paths.SourceDir, "SpringBootApi"))
            .WithHttpEndpoint(targetPort: ResourceNames.SpringBootApiPort, name: ResourceNames.HttpEndpoint, isProxied: false)
            .WithEnvironment("SERVER_PORT", ResourceNames.SpringBootApiPort.ToString())
            .WithEnvironment("OTEL_SERVICE_NAME", ResourceNames.SpringBootApi)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", observability.OtelCollector.GetEndpoint(ResourceNames.OtlpGrpcEndpoint))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OTEL_METRICS_EXPORTER", "none")
            .WithEnvironment("OTEL_LOGS_EXPORTER", "otlp")
            .WithEnvironment("OTEL_INSTRUMENTATION_LOGBACK_APPENDER_ENABLED", "true")
            .WithReference(postgres)
            .WaitFor(postgres)
            .WaitFor(observability.OtelCollector);

        return (postgres, springApi);
    }

    /// <summary>.NET API Service + MSSQL</summary>
    private static (IResourceBuilder<SqlServerDatabaseResource> Mssql, IResourceBuilder<ProjectResource> ApiService) AddApiService(
        this IDistributedApplicationBuilder builder,
        ObservabilityResources observability)
    {
        var mssqlPassword = builder.AddParameter("mssql-password", ResourceNames.MssqlPassword, secret: true);
        var mssqlServer = builder.AddSqlServer(ResourceNames.MssqlServer, mssqlPassword);
        var mssql = mssqlServer.AddDatabase(ResourceNames.Mssql, ResourceNames.ObservabilityDatabase);

        var apiService = builder.AddProject<Projects.AspireApmBackendsDemo_ApiService>(ResourceNames.ApiService)
            .WithHttpEndpoint(name: ResourceNames.HttpEndpoint, isProxied: false)
            .WithEnvironment("OTEL_SERVICE_NAME", ResourceNames.ApiService)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", observability.OtelCollector.GetEndpoint(ResourceNames.OtlpGrpcEndpoint))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OBSERVABILITY_BACKEND", observability.Backend)
            .WithReference(mssql)
            .WithHttpHealthCheck("/health")
            .WaitFor(mssql)
            .WaitFor(observability.OtelCollector);

        return (mssql, apiService);
    }
}
