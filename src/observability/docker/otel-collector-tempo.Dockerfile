FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-tempo.yml /etc/otelcol-contrib/config.yaml
