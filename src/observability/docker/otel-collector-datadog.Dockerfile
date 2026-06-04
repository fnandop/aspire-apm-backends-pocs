FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-datadog.yml /etc/otelcol-contrib/config.yaml
