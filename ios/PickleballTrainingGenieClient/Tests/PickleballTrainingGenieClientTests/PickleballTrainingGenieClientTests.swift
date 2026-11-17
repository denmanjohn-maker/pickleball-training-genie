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

@Test func drillDecodesPlayersAndEquipment_AndToleratesTheirAbsence() throws {
    let tagged = """
    {
      "id": "wall-dink",
      "title": "Wall Dink Touch",
      "description": "Summary.\\n\\nSetup: a wall.\\nHow to run it: dink.\\nGoal: 25.\\nCoaching cue: soft.",
      "targetDUPRLevel": 2.0,
      "category": "Dinking",
      "videoUrl": null,
      "sourceUrl": "https://example.com/drill",
      "createdAt": "2024-01-01T00:00:00Z",
      "minPlayers": 1,
      "equipment": "Wall"
    }
    """.data(using: .utf8)!
    let legacy = """
    {
      "id": "legacy",
      "title": "Legacy Drill",
      "description": "Old text.",
      "targetDUPRLevel": 3.0,
      "category": "General",
      "videoUrl": null,
      "sourceUrl": "https://example.com/legacy",
      "createdAt": "2024-01-01T00:00:00Z"
    }
    """.data(using: .utf8)!

    let decodedTagged = try JSONDecoder().decode(Drill.self, from: tagged)
    let decodedLegacy = try JSONDecoder().decode(Drill.self, from: legacy)

    #expect(decodedTagged.minPlayers == 1)
    #expect(decodedTagged.equipment == "Wall")
    #expect(decodedLegacy.minPlayers == nil)
    #expect(decodedLegacy.equipment == nil)
}

@Test func workoutDrillItemDecodesEnrichmentFields() throws {
    let json = """
    {
      "drills": [
        {
          "title": "Wall Dink Touch",
          "category": "Dinking",
          "durationMinutes": 10,
          "coachingNotes": "Soft hands.",
          "drillId": "a1000000-0000-0000-0000-000000000018",
          "description": "Summary.",
          "minPlayers": 1,
          "equipment": "Wall"
        }
      ],
      "totalDuration": 30,
      "warmup": "",
      "cooldown": "",
      "coachingNotes": ""
    }
    """.data(using: .utf8)!

    let decoded = try JSONDecoder().decode(WorkoutPlanResponse.self, from: json)

    #expect(decoded.drills[0].drillId == "a1000000-0000-0000-0000-000000000018")
    #expect(decoded.drills[0].minPlayers == 1)
    #expect(decoded.drills[0].equipment == "Wall")
}

@Test func generateWorkoutRequestOmitsNilFields_AndEncodesPlayersAvailable() throws {
    let encoder = JSONEncoder()
    encoder.outputFormatting = .sortedKeys

    let solo = try encoder.encode(
        PickleballTrainingGenieClient.GenerateWorkoutRequest(durationMinutes: 30, playersAvailable: PlayersAvailable.solo.rawValue))
    let legacy = try encoder.encode(
        PickleballTrainingGenieClient.GenerateWorkoutRequest(durationMinutes: 30, playersAvailable: nil))

    #expect(String(decoding: solo, as: UTF8.self) == #"{"durationMinutes":30,"playersAvailable":1}"#)
    #expect(String(decoding: legacy, as: UTF8.self) == #"{"durationMinutes":30}"#)
}
