function rankDrills(drills, { skillLevel, focus, completedDrillIds = [] }) {
  const completed = new Set(completedDrillIds);

  return drills
    .filter((drill) => !completed.has(drill.id))
    .map((drill) => ({
      ...drill,
      score:
        (drill.skillLevel === skillLevel ? 2 : 0) +
        (drill.focus === focus ? 3 : 0) +
        (completed.size === 0 ? 1 : 0),
    }))
    .sort((left, right) => right.score - left.score || left.name.localeCompare(right.name));
}

function buildRecommendations(drills, criteria) {
  const ranked = rankDrills(drills, criteria).slice(0, 3);

  return ranked.map(({ score, ...drill }) => ({
    drill,
    reason:
      drill.focus === criteria.focus
        ? `Matches the requested focus on ${criteria.focus}.`
        : `Provides balanced work for a ${criteria.skillLevel} player.`,
  }));
}

module.exports = {
  buildRecommendations,
};
