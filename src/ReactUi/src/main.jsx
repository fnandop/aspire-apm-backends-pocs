import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';

const serviceCalls = [
  { key: 'dotnet', label: '.NET API', database: 'MSSQL', path: '/api/dotnet/work' },
  { key: 'node', label: 'Node API', database: 'MongoDB', path: '/api/node/work' },
  { key: 'spring', label: 'Spring Boot API', database: 'PostgreSQL', path: '/api/spring/work' }
];

function App() {
  const [result, setResult] = useState(null);
  const [loadingKey, setLoadingKey] = useState(null);

  async function runGatewayTrace(service) {
    setLoadingKey(service.key);
    setResult(null);
    try {
      const response = await fetch(service.path);
      const body = await response.json();
      setResult({ service: service.key, ok: response.ok, body });
    } catch (error) {
      setResult({ service: service.key, ok: false, error: error.message });
    } finally {
      setLoadingKey(null);
    }
  }

  return (
    <main>
      <section className="hero">
        <div>
          <p className="eyebrow">Microservices trace lab</p>
          <h1>React client -> YARP gateway -> independent backend APIs</h1>
          <p className="summary">Run one flow at a time so each gateway request writes to a specific database and creates a focused service trace.</p>
        </div>
      </section>

      <section className="flow" aria-label="Trace flows">
        {serviceCalls.map((service) => {
          const loading = loadingKey === service.key;
          return (
            <article className="flow-card" key={service.key}>
              <div>
                <span>Client -> Gateway</span>
                <strong>{service.label} -> {service.database}</strong>
              </div>
              <button onClick={() => runGatewayTrace(service)} disabled={loadingKey !== null}>
                {loading ? 'Running...' : `Run ${service.key}`}
              </button>
            </article>
          );
        })}
      </section>

      <pre>{result ? JSON.stringify(result, null, 2) : 'Choose a flow to call one backend through the gateway.'}</pre>
    </main>
  );
}

createRoot(document.getElementById('root')).render(<App />);
