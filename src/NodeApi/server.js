import express from 'express';
import { MongoClient } from 'mongodb';
import { logger, SeverityNumber } from './tracing.js';

const app = express();
const port = Number(process.env.PORT);
const mongoUrl = process.env.ConnectionStrings__mongo;

if (!Number.isInteger(port) || port <= 0) {
  throw new Error('PORT must be provided by the AppHost.');
}

if (!mongoUrl) {
  throw new Error('ConnectionStrings__mongo must be provided by the AppHost.');
}

const client = new MongoClient(mongoUrl);

function emitLog(severityNumber, severityText, body, attributes = {}) {
  logger.emit({
    severityNumber,
    severityText,
    body,
    attributes: {
      'service.name': 'node-api',
      ...attributes
    }
  });
}

app.get('/health', (_req, res) => {
  emitLog(SeverityNumber.INFO, 'INFO', 'Node API health check requested', { 'http.route': '/health' });
  res.send('OK');
});

app.get('/work', async (_req, res, next) => {
  try {
    emitLog(SeverityNumber.INFO, 'INFO', 'Writing Node API event to MongoDB', { 'http.route': '/work' });
    await client.connect();
    const database = client.db();
    const collection = database.collection('demo_events');
    const event = { service: 'node-api', createdAt: new Date() };
    const result = await collection.insertOne(event);
    emitLog(SeverityNumber.INFO, 'INFO', 'Wrote Node API event to MongoDB', {
      'http.route': '/work',
      'db.system.name': 'mongodb',
      'db.operation.name': 'insert',
      'db.response.returned_rows': 1
    });
    res.json({ service: 'node-api', database: 'mongodb', eventId: result.insertedId, createdAt: event.createdAt });
  } catch (error) {
    emitLog(SeverityNumber.ERROR, 'ERROR', 'Failed to write Node API event to MongoDB', {
      'http.route': '/work',
      'exception.type': error.name,
      'exception.message': error.message,
      'exception.stacktrace': error.stack
    });
    next(error);
  }
});

app.listen(port, () => {
  emitLog(SeverityNumber.INFO, 'INFO', 'Node API started', { 'server.port': port });
  console.log(`node-api listening on ${port}`);
});
