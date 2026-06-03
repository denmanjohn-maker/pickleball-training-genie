import Foundation
import Testing
@testable import PickleballTrainingGenieClient

@Test func recommendationsUsesExpectedQueryItems() async throws {
    let client = PickleballTrainingGenieClient(baseURL: URL(string: "https://example.com/")!)

    let components = client.urlComponents(path: "api/recommendations")

    #expect(components.url?.absoluteString == "https://example.com/api/recommendations")
}

@Test func trainingSessionPayloadEncodesCompletedDrills() throws {
    let payload = TrainingSessionPayload(
        playerName: "Taylor",
        skillLevel: "beginner",
        focus: "control",
        durationMinutes: 25,
        completedDrillIds: ["dink-control"]
    )

    let encoded = try JSONEncoder().encode(payload)
    let decoded = try JSONDecoder().decode(TrainingSessionPayload.self, from: encoded)

    #expect(decoded == payload)
}

@Test func recommendationDecodesAPIShape() throws {
    let json = """
    {
      "recommendations": [
        {
          "drill": {
            "id": "dink-control",
            "name": "Dink Control Ladder",
            "description": "Improve touch and consistency.",
            "focus": "control",
            "skillLevel": "beginner",
            "durationMinutes": 15
          },
          "reason": "Matches the requested focus on control."
        }
      ]
    }
    """.data(using: .utf8)!

    let decoded = try JSONDecoder().decode(RecommendationsResponse.self, from: json)

    #expect(decoded.recommendations.count == 1)
    #expect(decoded.recommendations[0].drill.id == "dink-control")
}
