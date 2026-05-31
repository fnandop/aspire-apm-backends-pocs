var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.Configuration["OBSERVABILITY_BACKEND"] ?? "elastic";

var appHostDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? throw new InvalidOperationException("Could not determine app host directory");
var solutionDir = Path.GetFullPath(Path.Combine(appHostDir, "..", "..", "..", ".."));
var observabilityDir = Path.Combine(solutionDir, "observability");

var collectorConfigPath = backend switch
{
    "elastic" => Path.Combine(observabilityDir, "collector", "collector-elastic.yml"),
    "appinsights" => Path.Combine(observabilityDir, "collector", "collector-appinsights.yml"),
    "jaeger" => Path.Combine(observabilityDir, "collector", "collector-jaeger.yml"),
    "tempo" => Path.Combine(observabilityDir, "collector", "collector-tempo.yml"),
    "grafana-full" => Path.Combine(observabilityDir, "collector", "collector-grafana-full.yml"),
    _ => Path.Combine(observabilityDir, "collector", "collector-elastic.yml")
};

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib:latest")
    .WithHttpEndpoint(4318, name: "otlp-http", isProxied: false)
    .WithHttpEndpoint(4317, name: "otlp-grpc", isProxied: false)
    .WithBindMount(collectorConfigPath, "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithEnvironment("OBSERVABILITY_BACKEND", backend)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");

if (backend == "appinsights")
{
    otelCollector.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? "");
}

var apiService = builder.AddProject<Projects.AspireApmBackendsDemo_ApiService>("api-service")
    .WithHttpEndpoint(8080, isProxied: false)
    .WithEnvironment("OTEL_SERVICE_NAME", "api-service")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-grpc"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OBSERVABILITY_BACKEND", backend)
    .WithHttpHealthCheck("/health")
    .WaitFor(otelCollector);

if (backend == "elastic")
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
        .WaitFor(elasticsearch);

    var apmServerConfigPath = Path.Combine(observabilityDir, "elastic", "apm-server.yml");
    var apmServer = builder.AddContainer("apm-server", "docker.elastic.co/apm/apm-server:8.15.0")
        .WithHttpEndpoint(8200, isProxied: false)
        .WithBindMount(apmServerConfigPath, "/usr/share/apm-server/apm-server.yml", isReadOnly: true)
        .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
        .WithEnvironment("KIBANA_HOST", "http://kibana:5601")
        .WithEnvironment("OUTPUT_ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
        .WithEnvironment("SSL_ENABLED", "false")
        .WithEnvironment("MONITORING_ENABLED", "false")
        .WithEnvironment("BEAT_CONFIG_PATH", "/usr/share/apm-server/apm-server.yml")
        .WaitFor(elasticsearch)
        .WaitFor(kibana);

    apiService.WaitFor(elasticsearch);
    otelCollector.WaitFor(apmServer);
}

if (backend == "jaeger")
{
    var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one:latest")
        .WithHttpEndpoint(16686, name: "jaeger-ui", isProxied: false)
        .WithHttpEndpoint(14268, name: "jaeger-thrift-http", isProxied: false)
        .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true");

    otelCollector.WaitFor(jaeger);
    apiService.WaitFor(jaeger);
}

if (backend == "tempo")
{
    var tempo = builder.AddContainer("tempo", "grafana/tempo:latest")
        .WithHttpEndpoint(3100, name: "tempo-http", isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "tempo.yml"), "/etc/tempo.yaml", isReadOnly: true)
        .WithArgs("-config.file=/etc/tempo.yaml");

    builder.AddContainer("grafana", "grafana/grafana:latest")
        .WithHttpEndpoint(3000, isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "grafana-datasources.yml"), "/etc/grafana/provisioning/datasources/datasources.yml", isReadOnly: true)
        .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
        .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
        .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true");

    otelCollector.WaitFor(tempo);
    apiService.WaitFor(tempo);
}

if (backend == "grafana-full")
{
    var tempo = builder.AddContainer("tempo", "grafana/tempo:latest")
        .WithHttpEndpoint(3100, name: "tempo-http", isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "tempo.yml"), "/etc/tempo.yaml", isReadOnly: true)
        .WithArgs("-config.file=/etc/tempo.yaml");

    builder.AddContainer("prometheus", "prom/prometheus:latest")
        .WithHttpEndpoint(9090, isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "prometheus.yml"), "/etc/prometheus/prometheus.yml", isReadOnly: true);

    builder.AddContainer("loki", "grafana/loki:latest")
        .WithHttpEndpoint(3101, targetPort: 3100, isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "loki.yml"), "/etc/loki/local-config.yaml", isReadOnly: true)
        .WithArgs("-config.file=/etc/loki/local-config.yaml");

    builder.AddContainer("grafana", "grafana/grafana:latest")
        .WithHttpEndpoint(3000, isProxied: false)
        .WithBindMount(Path.Combine(observabilityDir, "grafana", "grafana-datasources.yml"), "/etc/grafana/provisioning/datasources/datasources.yml", isReadOnly: true)
        .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
        .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
        .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true");

    otelCollector.WaitFor(tempo);
    apiService.WaitFor(tempo);
}

builder.Build().Run();
