using Aspire.Hosting.ApplicationModel;

internal static class ObservabilityExtensions
{
    public static ObservabilityResources AddObservability(this IDistributedApplicationBuilder builder, AppHostPaths paths)
    {
        var backend = builder.Configuration["OBSERVABILITY_BACKEND"] ?? ResourceNames.DefaultBackend;
        var collectorConfigPath = GetCollectorConfigPath(paths.ObservabilityDir, backend);

        var otelCollector = builder.AddOtelCollector(paths, backend)
            .WithHttpEndpoint(4318, name: ResourceNames.OtlpHttpEndpoint, isProxied: false)
            .WithHttpEndpoint(4317, name: ResourceNames.OtlpGrpcEndpoint, isProxied: false)
            .WithHttpEndpoint(8889, name: "prometheus-metrics", isProxied: false)
            .WithEnvironment("OBSERVABILITY_BACKEND", backend)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");

        if (builder.ExecutionContext.IsRunMode)
        {
            otelCollector.WithBindMount(collectorConfigPath, "/etc/otelcol-contrib/config.yaml", isReadOnly: true);
        }

        ConfigureCollectorSecrets(builder, backend, otelCollector);

        return new ObservabilityResources(backend, otelCollector);
    }

    public static void AddSelectedObservabilityBackend(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability,
        ApplicationResources application)
    {
        switch (observability.Backend)
        {
            case "elastic":
                builder.AddElasticStack(paths, observability, application);
                break;
            case "jaeger":
                builder.AddJaeger(observability, application);
                break;
            case "tempo":
                builder.AddTempo(paths, observability, application);
                break;
            case "grafana-full":
                builder.AddGrafanaFull(paths, observability, application);
                break;
        }
    }

    private static string GetCollectorConfigPath(string observabilityDir, string backend) => backend switch
    {
        "elastic" => Path.Combine(observabilityDir, "collector", "collector-elastic.yml"),
        "appinsights" => Path.Combine(observabilityDir, "collector", "collector-appinsights.yml"),
        "datadog" => Path.Combine(observabilityDir, "collector", "collector-datadog.yml"),
        "jaeger" => Path.Combine(observabilityDir, "collector", "collector-jaeger.yml"),
        "tempo" => Path.Combine(observabilityDir, "collector", "collector-tempo.yml"),
        "grafana-full" => Path.Combine(observabilityDir, "collector", "collector-grafana-full.yml"),
        _ => Path.Combine(observabilityDir, "collector", "collector-elastic.yml")
    };

    private static string GetCollectorDockerfilePath(string backend) => backend switch
    {
        "appinsights" => Path.Combine("docker", "otel-collector-appinsights.Dockerfile"),
        "datadog" => Path.Combine("docker", "otel-collector-datadog.Dockerfile"),
        "jaeger" => Path.Combine("docker", "otel-collector-jaeger.Dockerfile"),
        "tempo" => Path.Combine("docker", "otel-collector-tempo.Dockerfile"),
        "grafana-full" => Path.Combine("docker", "otel-collector-grafana-full.Dockerfile"),
        _ => Path.Combine("docker", "otel-collector-elastic.Dockerfile")
    };

    private static IResourceBuilder<ContainerResource> AddOtelCollector(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        string backend)
    {
        return builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile(ResourceNames.OtelCollector, paths.ObservabilityDir, GetCollectorDockerfilePath(backend))
            : builder.AddContainer(ResourceNames.OtelCollector, "otel/opentelemetry-collector-contrib:latest");
    }

    private static void ConfigureCollectorSecrets(
        IDistributedApplicationBuilder builder,
        string backend,
        IResourceBuilder<ContainerResource> otelCollector)
    {
        if (backend == "appinsights")
        {
            otelCollector.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? "");
            return;
        }

        if (backend != "datadog")
        {
            return;
        }

        var datadogApiKey = builder.Configuration["DD_API_KEY"] ?? builder.Configuration["DATADOG_API_KEY"];
        if (string.IsNullOrWhiteSpace(datadogApiKey))
        {
            throw new InvalidOperationException("Datadog backend requires DD_API_KEY or DATADOG_API_KEY to be set.");
        }

        otelCollector
            .WithEnvironment("DD_API_KEY", datadogApiKey)
            .WithEnvironment("DD_SITE", builder.Configuration["DD_SITE"] ?? "datadoghq.com");
    }

    private static void AddElasticStack(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability,
        ApplicationResources application)
    {
        var elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch:8.15.0")
            .WithHttpEndpoint(9200, isProxied: false)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.security.enrollment.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m");

        var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana:8.15.0")
            .WithHttpEndpoint(5601, isProxied: false)
            .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
            .WithExternalHttpEndpoints()
            .WaitFor(elasticsearch);

        var apmServerConfigPath = Path.Combine(paths.ObservabilityDir, "elastic", "apm-server.yml");
        var apmServer = builder.AddElasticApmServer(paths)
            .WithHttpEndpoint(8200, isProxied: false)
            .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
            .WithEnvironment("KIBANA_HOST", "http://kibana:5601")
            .WithEnvironment("OUTPUT_ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
            .WithEnvironment("SSL_ENABLED", "false")
            .WithEnvironment("MONITORING_ENABLED", "false")
            .WithEnvironment("BEAT_CONFIG_PATH", "/usr/share/apm-server/apm-server.yml")
            .WaitFor(elasticsearch)
            .WaitFor(kibana);

        if (builder.ExecutionContext.IsRunMode)
        {
            apmServer.WithBindMount(apmServerConfigPath, "/usr/share/apm-server/apm-server.yml", isReadOnly: true);
        }

        application.ApiService.WaitFor(elasticsearch);
        observability.OtelCollector.WaitFor(apmServer);
    }

    private static void AddJaeger(
        this IDistributedApplicationBuilder builder,
        ObservabilityResources observability,
        ApplicationResources application)
    {
        var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one:latest")
            .WithHttpEndpoint(16686, name: "jaeger-ui", isProxied: false)
            .WithHttpEndpoint(14268, name: "jaeger-thrift-http", isProxied: false)
            .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true");

        observability.OtelCollector.WaitFor(jaeger);
        application.ApiService.WaitFor(jaeger);
    }

    private static void AddTempo(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability,
        ApplicationResources application)
    {
        var tempo = builder.AddTempoContainer(paths)
            .WithHttpEndpoint(3100, name: "tempo-http", isProxied: false)
            .WithHttpEndpoint(4317, name: "tempo-otlp-grpc", isProxied: false)
            .WithHttpEndpoint(4318, name: "tempo-otlp-http", isProxied: false)
            .WithArgs("-config.file=/etc/tempo.yaml");

        if (builder.ExecutionContext.IsRunMode)
        {
            tempo.WithBindMount(Path.Combine(paths.ObservabilityDir, "grafana", "tempo.yml"), "/etc/tempo.yaml", isReadOnly: true);
        }

        observability.OtelCollector.WithEnvironment("TEMPO_OTLP_ENDPOINT", "tempo:4317");

        builder.AddGrafana(paths, tempo.GetEndpoint("tempo-http"));

        observability.OtelCollector.WaitFor(tempo);
        application.ApiService.WaitFor(tempo);
    }

    private static void AddGrafanaFull(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        ObservabilityResources observability,
        ApplicationResources application)
    {
        var tempo = builder.AddTempoContainer(paths)
            .WithHttpEndpoint(3100, name: "tempo-http", isProxied: false)
            .WithHttpEndpoint(4317, name: "tempo-otlp-grpc", isProxied: false)
            .WithHttpEndpoint(4318, name: "tempo-otlp-http", isProxied: false)
            .WithArgs("-config.file=/etc/tempo.yaml");

        var prometheus = builder.AddPrometheusContainer(paths)
            .WithHttpEndpoint(9090, isProxied: false);

        var loki = builder.AddLokiContainer(paths)
            .WithHttpEndpoint(3101, targetPort: 3100, isProxied: false)
            .WithArgs("-config.file=/etc/loki/local-config.yaml");

        observability.OtelCollector
            .WithEnvironment("TEMPO_OTLP_ENDPOINT", "tempo:4317")
            .WithEnvironment("LOKI_OTLP_ENDPOINT", loki.GetEndpoint(ResourceNames.HttpEndpoint));

        builder.AddGrafana(
            paths,
            tempo.GetEndpoint("tempo-http"),
            prometheus.GetEndpoint(ResourceNames.HttpEndpoint),
            loki.GetEndpoint(ResourceNames.HttpEndpoint));

        if (builder.ExecutionContext.IsRunMode)
        {
            tempo.WithBindMount(Path.Combine(paths.ObservabilityDir, "grafana", "tempo.yml"), "/etc/tempo.yaml", isReadOnly: true);
            prometheus.WithBindMount(Path.Combine(paths.ObservabilityDir, "grafana", "prometheus.yml"), "/etc/prometheus/prometheus.yml", isReadOnly: true);
            loki.WithBindMount(Path.Combine(paths.ObservabilityDir, "grafana", "loki.yml"), "/etc/loki/local-config.yaml", isReadOnly: true);
        }

        observability.OtelCollector.WaitFor(tempo);
        application.ApiService.WaitFor(tempo);
    }

    private static IResourceBuilder<ContainerResource> AddGrafana(
        this IDistributedApplicationBuilder builder,
        AppHostPaths paths,
        EndpointReference tempoEndpoint,
        EndpointReference? prometheusEndpoint = null,
        EndpointReference? lokiEndpoint = null)
    {
        var grafana = builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile("grafana", paths.ObservabilityDir, Path.Combine("docker", "grafana.Dockerfile"))
            : builder.AddContainer("grafana", "grafana/grafana:latest");

        grafana
            .WithHttpEndpoint(3000, isProxied: false)
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
            .WithEnvironment("TEMPO_URL", tempoEndpoint)
            .WithEnvironment("PROMETHEUS_URL", "http://prometheus:9090")
            .WithEnvironment("LOKI_URL", "http://loki:3100")
            .WithExternalHttpEndpoints();

        if (prometheusEndpoint is not null)
        {
            grafana.WithEnvironment("PROMETHEUS_URL", prometheusEndpoint);
        }

        if (lokiEndpoint is not null)
        {
            grafana.WithEnvironment("LOKI_URL", lokiEndpoint);
        }

        if (builder.ExecutionContext.IsRunMode)
        {
            grafana.WithBindMount(Path.Combine(paths.ObservabilityDir, "grafana", "grafana-datasources.yml"), "/etc/grafana/provisioning/datasources/datasources.yml", isReadOnly: true);
        }

        return grafana;
    }

    private static IResourceBuilder<ContainerResource> AddElasticApmServer(this IDistributedApplicationBuilder builder, AppHostPaths paths)
    {
        return builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile("apm-server", paths.ObservabilityDir, Path.Combine("docker", "apm-server.Dockerfile"))
            : builder.AddContainer("apm-server", "docker.elastic.co/apm/apm-server:8.15.0");
    }

    private static IResourceBuilder<ContainerResource> AddTempoContainer(this IDistributedApplicationBuilder builder, AppHostPaths paths)
    {
        return builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile("tempo", paths.ObservabilityDir, Path.Combine("docker", "tempo.Dockerfile"))
            : builder.AddContainer("tempo", "grafana/tempo:latest");
    }

    private static IResourceBuilder<ContainerResource> AddPrometheusContainer(this IDistributedApplicationBuilder builder, AppHostPaths paths)
    {
        return builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile("prometheus", paths.ObservabilityDir, Path.Combine("docker", "prometheus.Dockerfile"))
            : builder.AddContainer("prometheus", "prom/prometheus:latest");
    }

    private static IResourceBuilder<ContainerResource> AddLokiContainer(this IDistributedApplicationBuilder builder, AppHostPaths paths)
    {
        return builder.ExecutionContext.IsPublishMode
            ? builder.AddDockerfile("loki", paths.ObservabilityDir, Path.Combine("docker", "loki.Dockerfile"))
            : builder.AddContainer("loki", "grafana/loki:latest");
    }
}
