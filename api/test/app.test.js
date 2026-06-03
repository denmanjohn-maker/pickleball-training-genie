const test = require('node:test');
const assert = require('node:assert/strict');
const { once } = require('node:events');

const { createApp } = require('../src/app');

async function withServer(run) {
  const app = createApp();
  const server = app.listen(0);
  await once(server, 'listening');

  try {
    const { port } = server.address();
    await run(`http://127.0.0.1:${port}`);
  } finally {
    server.close();
    await once(server, 'close');
  }
}

test('GET /health reports API readiness', async () => {
  await withServer(async (baseUrl) => {
    const response = await fetch(`${baseUrl}/health`);
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.deepEqual(body, { status: 'ok' });
  });
});

test('GET /api/recommendations prioritizes focus matches', async () => {
  await withServer(async (baseUrl) => {
    const response = await fetch(
      `${baseUrl}/api/recommendations?skillLevel=beginner&focus=control`
    );
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.equal(body.recommendations[0].drill.id, 'dink-control');
    assert.match(body.recommendations[0].reason, /focus on control/i);
  });
});

test('POST /api/training-sessions tracks a session and returns follow-up recommendations', async () => {
  await withServer(async (baseUrl) => {
    const createResponse = await fetch(`${baseUrl}/api/training-sessions`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
      },
      body: JSON.stringify({
        playerName: 'Alex',
        skillLevel: 'intermediate',
        focus: 'offense',
        durationMinutes: 30,
        completedDrillIds: ['speed-up-counter'],
      }),
    });

    const createdSession = await createResponse.json();
    const includesCompletedDrillRecommendation = createdSession.recommendations.some(
      ({ drill }) => drill.id === 'speed-up-counter'
    );

    assert.equal(createResponse.status, 201);
    assert.equal(createdSession.playerName, 'Alex');
    assert.equal(includesCompletedDrillRecommendation, false);

    const listResponse = await fetch(`${baseUrl}/api/training-sessions`);
    const listedSessions = await listResponse.json();
    assert.equal(listedSessions.sessions[0].id, createdSession.id);
  });
});
