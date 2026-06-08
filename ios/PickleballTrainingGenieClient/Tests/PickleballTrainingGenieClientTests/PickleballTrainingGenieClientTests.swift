import Foundation
import Testing
@testable import PickleballTrainingGenieClient

@Test func recommendationsUsesExpectedURL() async throws {
    let client = PickleballTrainingGenieClient(baseURL: URL(string: "https://example.com/")!)

    let components = try client.urlComponents(path: "api/Drills/recommendations")

    #expect(components.url?.absoluteString == "https://example.com/api/Drills/recommendations")
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
