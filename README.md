# .NET Aspire APM Backend Comparison Demo

This proof-of-concept demonstrates how to compare different APM/observability backends using OpenTelemetry Collector with a single .NET Aspire API service.

## Purpose

Compare different APM/observability solutions with the same .NET API service:
- **Switching backends** requires changing only Aspire infrastructure and Collector configuration
- **No application code changes** required when switching backends
- **Backend-neutral** API that only knows about OpenTelemetry and the Collector

## Architecture

### Core Flow

```
.NET API
  → OTLP exporter
  → OpenTelemetry Collector
  → selected backend
```

### 1. Elastic APM / ELK Stack

```mermaid
graph LR
    A["⚙️ .NET API"] --> B["📦 OTLP"]
    B --> C["🔭 OpenTelemetry Collector"]
    C --> D["🟣 Elastic APM Server"]
    D --> E["🔎 Elasticsearch"]
    E --> F["📊 Kibana"]

    classDef app fill:#512BD4,stroke:#2b166f,color:#fff
    classDef protocol fill:#E8F1FF,stroke:#4C7BD9,color:#16325C
    classDef collector fill:#F5A623,stroke:#9A6400,color:#111
    classDef elastic fill:#00BFB3,stroke:#00756E,color:#061D1B
    classDef ui fill:#F04E98,stroke:#A91F62,color:#fff
    class A app
    class B protocol
    class C collector
    class D,E elastic
    class F ui
```

### 2. Azure Application Insights

```mermaid
graph LR
    A["⚙️ .NET API"] --> B["📦 OTLP"]
    B --> C["🔭 OpenTelemetry Collector"]
    C --> D["☁️ Azure Monitor<br/>Application Insights"]

    classDef app fill:#512BD4,stroke:#2b166f,color:#fff
    classDef protocol fill:#E8F1FF,stroke:#4C7BD9,color:#16325C
    classDef collector fill:#F5A623,stroke:#9A6400,color:#111
    classDef azure fill:#0078D4,stroke:#004E8A,color:#fff
    class A app
    class B protocol
    class C collector
    class D azure
```

### 3. Jaeger

```mermaid
graph LR
    A["⚙️ .NET API"] --> B["📦 OTLP"]
    B --> C["🔭 OpenTelemetry Collector"]
    C --> D["🧭 Jaeger"]
    D --> E["🕵️ Jaeger UI"]

    classDef app fill:#512BD4,stroke:#2b166f,color:#fff
    classDef protocol fill:#E8F1FF,stroke:#4C7BD9,color:#16325C
    classDef collector fill:#F5A623,stroke:#9A6400,color:#111
    classDef jaeger fill:#66C2A5,stroke:#2F7D69,color:#10231E
    classDef ui fill:#8E44AD,stroke:#5E2A78,color:#fff
    class A app
    class B protocol
    class C collector
    class D jaeger
    class E ui
```

### 4. Grafana Tempo

```mermaid
graph LR
    A["⚙️ .NET API"] --> B["📦 OTLP"]
    B --> C["🔭 OpenTelemetry Collector"]
    C --> D["⏱️ Tempo"]
    D --> E["📈 Grafana"]

    classDef app fill:#512BD4,stroke:#2b166f,color:#fff
    classDef protocol fill:#E8F1FF,stroke:#4C7BD9,color:#16325C
    classDef collector fill:#F5A623,stroke:#9A6400,color:#111
    classDef tempo fill:#7B61FF,stroke:#443399,color:#fff
    classDef grafana fill:#F46800,stroke:#9B3F00,color:#fff
    class A app
    class B protocol
    class C collector
    class D tempo
    class E grafana
```

### 5. Full Grafana Stack

```mermaid
graph LR
    A["⚙️ .NET API"] --> B["📦 OTLP"]
    B --> C["🔭 OpenTelemetry Collector"]
    C --> D["⏱️ Tempo<br/>traces"]
    C --> E["🔥 Prometheus<br/>metrics"]
    C --> F["🪵 Loki<br/>logs"]
    D --> G["📈 Grafana"]
    E --> G
    F --> G

    classDef app fill:#512BD4,stroke:#2b166f,color:#fff
    classDef protocol fill:#E8F1FF,stroke:#4C7BD9,color:#16325C
    classDef collector fill:#F5A623,stroke:#9A6400,color:#111
    classDef tempo fill:#7B61FF,stroke:#443399,color:#fff
    classDef prometheus fill:#E6522C,stroke:#9C2E16,color:#fff
    classDef loki fill:#2ECC71,stroke:#1F8E4D,color:#102015
    classDef grafana fill:#F46800,stroke:#9B3F00,color:#fff
    class A app
    class B protocol
    class C collector
    class D tempo
    class E prometheus
    class F loki
    class G grafana
```

## How to Run

### Prerequisites

- Docker Desktop (with Kubernetes support)
- .NET 10 SDK
- Aspire workload installed (`dotnet workload install aspire`)

### Running with Different Backends

Set the `OBSERVABILITY_BACKEND` environment variable before running:

```bash
# Elastic APM / ELK Stack
$env:OBSERVABILITY_BACKEND="elastic"
dotnet run --project AspireApmBackendsDemo.AppHost

# Azure Application Insights
$env:OBSERVABILITY_BACKEND="appinsights"
$env:APPLICATIONINSIGHTS_CONNECTION_STRING="your-connection-string"
dotnet run --project AspireApmBackendsDemo.AppHost

# Jaeger
$env:OBSERVABILITY_BACKEND="jaeger"
dotnet run --project AspireApmBackendsDemo.AppHost

# Grafana Tempo
$env:OBSERVABILITY_BACKEND="tempo"
dotnet run --project AspireApmBackendsDemo.AppHost

# Full Grafana Stack (Tempo + Prometheus + Loki)
$env:OBSERVABILITY_BACKEND="grafana-full"
dotnet run --project AspireApmBackendsDemo.AppHost
```

## Application Insights Setup

When using `appinsights` backend, you must set the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable:

```bash
$env:APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=your-key;IngestionEndpoint=https://your-endpoint.in.applicationinsights.azure.com/"
```

Do not hardcode this value. Use environment variables or Azure Key Vault in production.

## Test Commands

After starting the application, use curl to test each endpoint:

```bash
# Root endpoint - shows selected backend
curl http://localhost:8080/

# Health check
curl http://localhost:8080/health

# Error endpoint - throws exception for APM testing
curl http://localhost:8080/error

# Slow response - 2 second delay
curl http://localhost:8080/slow

# Outgoing HTTP call - tests dependency tracing
curl http://localhost:8080/outgoing

# Log levels - generates info, warning, and error logs
curl http://localhost:8080/logs

# Custom span - creates custom Activity/span with tags and events
curl http://localhost:8080/custom-span

# Random - randomly returns success, slow response, or error
curl http://localhost:8080/random
```

## Where to View Telemetry

| Backend | UI URL | Notes |
|---------|--------|-------|
| Elastic | http://localhost:5601 | Kibana → Observability → APM → Services |
| Application Insights | Azure Portal | Application Insights → Transaction Search / Application Map / Failures |
| Jaeger | http://localhost:16686 | Search traces by service `api-service`; Jaeger does not store application logs or metrics |
| Tempo | http://localhost:3000 | Grafana → Explore → Tempo datasource |
| Grafana Full | http://localhost:3000 | Explore Tempo with service `api-service`; Explore Loki with `{service_name="api-service"}` |

## Backend Comparison

| Feature | Elastic APM | Application Insights | Jaeger | Grafana Tempo | Full Grafana Stack |
|---------|-------------|---------------------|--------|---------------|---------------------|
| **Traces** | Yes | Yes | Yes | Yes | Yes |
| **Metrics** | Yes | Yes | No | No | Yes (Prometheus) |
| **Logs** | Yes (ELK) | Yes | No | No | Yes (Loki) |
| **UI** | Kibana | Azure Portal | Jaeger UI | Grafana | Grafana |
| **Hosting** | Self-hosted | Azure-managed | Self-hosted | Self-hosted | Self-hosted |
| **Best For** | Full observability with Elasticsearch | Teams already in Azure | Simple trace visualization | Trace-focused teams | Complete observability stack |
| **Complexity** | Medium-High | Low | Low | Medium | High |

## Design Decisions

### Why OpenTelemetry Collector?

The Collector provides a clear separation between application code and backend configuration:
- Applications only need to know about OTLP
- Backend-specific exporters live in the Collector
- Switching backends doesn't require recompiling the application

### Why Backend-Neutral API?

The same API code works with all backends because:
- It only uses OpenTelemetry SDK and OTLP exporter
- No backend-specific packages (Elastic APM .NET Agent, Application Insights SDK, Jaeger client, etc.)
- Configuration happens at deployment/infrastructure level

### Why Not Logstash?

Logstash is not needed because:
- OpenTelemetry Collector handles log export directly
- The Collector has native OTLP support for logs
- Adding Logstash would introduce another service and complexity

### Why Elastic APM Server?

For Elastic APM:
- The APM Server is the native ingestion point for Elastic's APM format
- It converts OTLP data to Elastic's format for Elasticsearch
- Kibana's APM UI expects data in this format

### Why Collector-Based Application Insights Exporter?

The Application Insights exporter lives in the Collector because:
- It keeps the API service backend-neutral
- The Collector uses the Azure Monitor exporter
- API code only knows about OTLP

### Why Jaeger and Tempo Are Trace-Focused

Jaeger and Tempo are trace backends in this demo:
- Jaeger receives traces only; application logs and metrics are not exported in `jaeger` mode
- Tempo is designed for traces; metrics come from Prometheus
- For full observability, the Grafana full stack is recommended

### Trade-offs of Using Collector

**Advantages:**
- More flexible backend selection
- Centralized backend configuration
- Easier backend switching
- Single point for sampling and processing

**Disadvantages:**
- One extra running service (the Collector)
- Additional network hop
- Collector configuration complexity

## Project Structure

```
/AspireApmBackendsDemo
  /AspireApmBackendsDemo.AppHost      # Aspire orchestration
  /AspireApmBackendsDemo.ApiService   # .NET Minimal API
  /AspireApmBackendsDemo.ServiceDefaults # Shared configuration
  /observability
    /collector                         # OTel Collector configs
      collector-elastic.yml
      collector-appinsights.yml
      collector-jaeger.yml
      collector-tempo.yml
      collector-grafana-full.yml
    /elastic
      apm-server.yml
    /grafana
      tempo.yml
      prometheus.yml
      loki.yml
      grafana-datasources.yml
  README.md
```

## Environment Variables

| Variable | Description | Used By |
|----------|-------------|---------|
| `OBSERVABILITY_BACKEND` | Backend selection (elastic, appinsights, jaeger, tempo, grafana-full) | AppHost, API Service |
| `OTEL_SERVICE_NAME` | OpenTelemetry service name | API Service, Collector |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP endpoint for export | API Service |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | OTLP protocol (grpc/http) | API Service |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure App Insights connection string | Collector (appinsights backend only) |

## Ports

| Service | Port | Description |
|---------|------|-------------|
| api-service | 8080 | ASP.NET Core API |
| otel-collector | 4317 | OTLP gRPC |
| otel-collector | 4318 | OTLP HTTP |
| elasticsearch | 9200 | Elasticsearch |
| kibana | 5601 | Kibana |
| apm-server | 8200 | Elastic APM Server |
| jaeger-ui | 16686 | Jaeger UI |
| jaeger-otlp | 4317 | Jaeger OTLP |
| tempo | 3100 | Tempo HTTP |
| tempo-otlp | 4317 | Tempo OTLP inside the Docker network; not published to the host |
| prometheus | 9090 | Prometheus |
| loki | 3100 | Loki |
| grafana | 3000 | Grafana |

## Cleanup

To stop all containers:

```bash
docker ps --filter "name=otel-collector" --filter "name=elasticsearch" --filter "name=kibana" --filter "name=apm-server" --filter "name=jaeger" --filter "name=tempo" --filter "name=prometheus" --filter "name=loki" --filter "name=grafana" -q | xargs docker stop
```

Or simply stop the Aspire orchestration with Ctrl+C.
