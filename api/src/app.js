const express = require('express');

const { drills } = require('./data');
const { buildRecommendations } = require('./recommendations');

function createApp() {
  const app = express();
  const trainingSessions = [];
  let nextSessionNumber = 1;

  app.use(express.json());

  app.get('/health', (_request, response) => {
    response.json({ status: 'ok' });
  });

  app.get('/api/drills', (_request, response) => {
    response.json({ drills });
  });

  app.get('/api/recommendations', (request, response) => {
    const skillLevel = request.query.skillLevel || 'beginner';
    const focus = request.query.focus || 'consistency';

    response.json({
      recommendations: buildRecommendations(drills, { skillLevel, focus }),
    });
  });

  app.get('/api/training-sessions', (_request, response) => {
    response.json({ sessions: trainingSessions });
  });

  app.post('/api/training-sessions', (request, response) => {
    const {
      playerName,
      skillLevel,
      focus,
      durationMinutes,
      completedDrillIds = [],
    } = request.body ?? {};

    if (!skillLevel || !focus || !Number.isFinite(durationMinutes) || durationMinutes <= 0) {
      return response.status(400).json({
        error: 'skillLevel, focus, and a positive durationMinutes value are required.',
      });
    }

    const session = {
      id: `session-${nextSessionNumber++}`,
      playerName: playerName || 'Player',
      skillLevel,
      focus,
      durationMinutes,
      completedDrillIds,
      createdAt: new Date().toISOString(),
      recommendations: buildRecommendations(drills, { skillLevel, focus, completedDrillIds }),
    };

    trainingSessions.unshift(session);

    return response.status(201).json(session);
  });

  return app;
}

module.exports = {
  createApp,
};
