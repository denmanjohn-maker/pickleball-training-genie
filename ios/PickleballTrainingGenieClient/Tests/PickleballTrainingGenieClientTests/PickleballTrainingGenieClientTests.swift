import Foundation
import Testing
@testable import PickleballTrainingGenieClient

@Test func recommendationsUsesExpectedURL() async throws {
    let client = PickleballTrainingGenieClient(baseURL: URL(string: "https://example.com/")!)

    let components = try client.urlComponents(path: "api/Drills/recommendations")

    #expect(components.url?.absoluteString == "https://example.com/api/Drills/recommendations")
}

@Test func generateWorkoutUsesExpectedURL() async throws {
    let client = PickleballTrainingGenieClient(baseURL: URL(string: "https://example.com/")!)

    let components = try client.urlComponents(path: "api/workouts/generate")

    #expect(components.url?.absoluteString == "https://example.com/api/workouts/generate")
}

@Test func workoutPlanResponseDecodesAPIShape() throws {
    let json = """
    {
      "drills": [
        {
          "title": "Cross-Court Dink Rally",
          "category": "Dinking",
          "durationMinutes": 10,
          "coachingNotes": "Focus on soft hands."
        }
      ],
      "totalDuration": 30,
      "warmup": "Light stretching",
      "cooldown": "Cool down walk",
      "coachingNotes": "Great session plan."
    }
    """.data(using: .utf8)!

    let decoded = try JSONDecoder().decode(WorkoutPlanResponse.self, from: json)

    #expect(decoded.totalDuration == 30)
    #expect(decoded.drills.count == 1)
    #expect(decoded.drills[0].title == "Cross-Court Dink Rally")
    #expect(decoded.drills[0].durationMinutes == 10)
}

@Test func recommendationDecodesAPIShape() throws {
    let json = """
    [
      {
        "id": "dink-control",
        "title": "Dink Control Ladder",
        "description": "Improve touch and consistency.",
        "targetDUPRLevel": 3.0,
        "category": "Dinking",
        "videoUrl": null,
        "sourceUrl": "https://example.com/drill1",
        "createdAt": "2024-01-01T00:00:00Z"
      }
    ]
    """.data(using: .utf8)!

    let decoded = try JSONDecoder().decode([Drill].self, from: json)

    #expect(decoded.count == 1)
    #expect(decoded[0].id == "dink-control")
    #expect(decoded[0].targetDUPRLevel == 3.0)
}
