FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-appinsights.yml /etc/otelcol-contrib/config.yaml
