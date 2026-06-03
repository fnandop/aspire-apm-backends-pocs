package demo;

import java.util.Map;
import io.opentelemetry.api.GlobalOpenTelemetry;
import io.opentelemetry.instrumentation.logback.appender.v1_0.OpenTelemetryAppender;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@SpringBootApplication
public class SpringBootApiApplication {
    public static void main(String[] args) {
        configureAspirePostgresConnection();
        OpenTelemetryAppender.install(GlobalOpenTelemetry.get());
        SpringApplication.run(SpringBootApiApplication.class, args);
    }

    private static void configureAspirePostgresConnection() {
        var connectionString = System.getenv("ConnectionStrings__postgres");
        if (connectionString == null || connectionString.isBlank()) {
            return;
        }

        var properties = parseConnectionString(connectionString);
        var host = properties.get("host");
        var port = properties.getOrDefault("port", "5432");
        var database = properties.get("database");
        var username = firstNonBlank(properties.get("username"), properties.get("user id"), properties.get("user"));
        var password = properties.get("password");

        if (host == null || database == null) {
            throw new IllegalStateException("ConnectionStrings__postgres must include Host and Database.");
        }

        System.setProperty("spring.datasource.url", "jdbc:postgresql://" + host + ":" + port + "/" + database);
        if (username != null) {
            System.setProperty("spring.datasource.username", username);
        }
        if (password != null) {
            System.setProperty("spring.datasource.password", password);
        }
    }

    private static Map<String, String> parseConnectionString(String connectionString) {
        return java.util.Arrays.stream(connectionString.split(";"))
            .map(part -> part.split("=", 2))
            .filter(parts -> parts.length == 2 && !parts[0].isBlank())
            .collect(java.util.stream.Collectors.toMap(
                parts -> parts[0].trim().toLowerCase(),
                parts -> parts[1].trim(),
                (left, right) -> right));
    }

    private static String firstNonBlank(String... values) {
        for (var value : values) {
            if (value != null && !value.isBlank()) {
                return value;
            }
        }

        return null;
    }
}

@RestController
class WorkController {
    private static final Logger logger = LoggerFactory.getLogger(WorkController.class);
    private final JdbcTemplate jdbc;

    WorkController(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @GetMapping("/health")
    String health() {
        logger.info("Spring Boot API health check requested");
        return "OK";
    }

    @GetMapping("/work")
    Map<String, Object> work() {
        try {
            logger.info("Writing Spring Boot API event to PostgreSQL");
            jdbc.execute("create table if not exists demo_events (id serial primary key, service text not null, created_at timestamptz not null default now())");
            var row = jdbc.queryForMap("insert into demo_events(service) values ('spring-boot-api') returning id, created_at");
            logger.info("Wrote Spring Boot API event to PostgreSQL");
            return Map.of("service", "spring-boot-api", "event", row);
        } catch (RuntimeException exception) {
            logger.error("Failed to write Spring Boot API event to PostgreSQL", exception);
            throw exception;
        }
    }
}
