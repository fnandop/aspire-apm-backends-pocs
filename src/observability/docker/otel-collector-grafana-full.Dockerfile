FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-grafana-full.yml /etc/otelcol-contrib/config.yaml
