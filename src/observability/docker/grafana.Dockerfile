FROM grafana/grafana:latest
COPY grafana/grafana-datasources.yml /etc/grafana/provisioning/datasources/datasources.yml
