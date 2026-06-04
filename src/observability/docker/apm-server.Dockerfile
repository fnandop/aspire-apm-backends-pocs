FROM docker.elastic.co/apm/apm-server:8.15.0
COPY elastic/apm-server.yml /usr/share/apm-server/apm-server.yml
