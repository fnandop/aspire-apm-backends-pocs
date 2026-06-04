FROM otel/opentelemetry-collector-contrib:latest
COPY collector/collector-jaeger.yml /etc/otelcol-contrib/config.yaml
