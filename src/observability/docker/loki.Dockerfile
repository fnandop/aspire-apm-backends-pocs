FROM grafana/loki:latest
COPY grafana/loki.yml /etc/loki/local-config.yaml
