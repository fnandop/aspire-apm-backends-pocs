FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-elastic.yml /etc/otelcol-contrib/config.yaml
