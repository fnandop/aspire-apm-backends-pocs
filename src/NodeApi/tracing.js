import { NodeSDK } from '@opentelemetry/sdk-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { LoggerProvider, BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';

const otlpEndpoint = (process.env.OTEL_EXPORTER_OTLP_ENDPOINT ?? 'http://otel-collector:4318').replace(/\/$/, '');
const otlpTracesEndpoint = process.env.OTEL_EXPORTER_OTLP_TRACES_ENDPOINT ?? `${otlpEndpoint}/v1/traces`;
const otlpLogsEndpoint = process.env.OTEL_EXPORTER_OTLP_LOGS_ENDPOINT ?? `${otlpEndpoint}/v1/logs`;
const resource = resourceFromAttributes({
  [ATTR_SERVICE_NAME]: process.env.OTEL_SERVICE_NAME ?? 'node-api'
});

const sdk = new NodeSDK({
  resource,
  traceExporter: new OTLPTraceExporter({
    url: otlpTracesEndpoint
  }),
  instrumentations: [getNodeAutoInstrumentations()]
});

const loggerProvider = new LoggerProvider({
  resource,
  processors: [new BatchLogRecordProcessor(new OTLPLogExporter({ url: otlpLogsEndpoint }))]
});
logs.setGlobalLoggerProvider(loggerProvider);

sdk.start();

process.on('SIGTERM', async () => {
  await loggerProvider.shutdown();
  await sdk.shutdown();
});

export const logger = logs.getLogger(process.env.OTEL_SERVICE_NAME ?? 'node-api');
export { SeverityNumber };
