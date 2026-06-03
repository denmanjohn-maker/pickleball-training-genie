import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public struct HealthResponse: Codable, Equatable, Sendable {
    public let status: String
}

public struct Drill: Codable, Equatable, Identifiable, Sendable {
    public let id: String
    public let name: String
    public let description: String
    public let focus: String
    public let skillLevel: String
    public let durationMinutes: Int
}

public struct Recommendation: Codable, Equatable, Sendable {
    public let drill: Drill
    public let reason: String
}

public struct TrainingSessionPayload: Codable, Equatable, Sendable {
    public let playerName: String
    public let skillLevel: String
    public let focus: String
    public let durationMinutes: Int
    public let completedDrillIds: [String]

    public init(
        playerName: String,
        skillLevel: String,
        focus: String,
        durationMinutes: Int,
        completedDrillIds: [String] = []
    ) {
        self.playerName = playerName
        self.skillLevel = skillLevel
        self.focus = focus
        self.durationMinutes = durationMinutes
        self.completedDrillIds = completedDrillIds
    }
}

public struct TrainingSessionRecord: Codable, Equatable, Identifiable, Sendable {
    public let id: String
    public let playerName: String
    public let skillLevel: String
    public let focus: String
    public let durationMinutes: Int
    public let completedDrillIds: [String]
    public let createdAt: String
    public let recommendations: [Recommendation]
}

public struct DrillsResponse: Codable, Equatable, Sendable {
    public let drills: [Drill]
}

public struct RecommendationsResponse: Codable, Equatable, Sendable {
    public let recommendations: [Recommendation]
}

public struct TrainingSessionsResponse: Codable, Equatable, Sendable {
    public let sessions: [TrainingSessionRecord]
}

public struct PickleballTrainingGenieClient {
    public let baseURL: URL
    public let session: URLSession
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()

    public init(baseURL: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.session = session
    }

    public func health() async throws -> HealthResponse {
        try await get(path: "health")
    }

    public func drills() async throws -> [Drill] {
        let response: DrillsResponse = try await get(path: "api/drills")
        return response.drills
    }

    public func recommendations(skillLevel: String, focus: String) async throws -> [Recommendation] {
        var components = urlComponents(path: "api/recommendations")
        components.queryItems = [
            URLQueryItem(name: "skillLevel", value: skillLevel),
            URLQueryItem(name: "focus", value: focus),
        ]

        let response: RecommendationsResponse = try await request(components.url!, method: "GET")
        return response.recommendations
    }

    public func createTrainingSession(_ payload: TrainingSessionPayload) async throws -> TrainingSessionRecord {
        try await request(url(path: "api/training-sessions"), method: "POST", body: payload)
    }

    public func trainingSessions() async throws -> [TrainingSessionRecord] {
        let response: TrainingSessionsResponse = try await get(path: "api/training-sessions")
        return response.sessions
    }

    func urlComponents(path: String) -> URLComponents {
        URLComponents(url: url(path: path), resolvingAgainstBaseURL: false)!
    }

    private func url(path: String) -> URL {
        baseURL.appending(path: path)
    }

    private func get<Response: Decodable>(path: String) async throws -> Response {
        try await request(url(path: path), method: "GET")
    }

    private func request<Response: Decodable>(
        _ url: URL,
        method: String
    ) async throws -> Response {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        let (data, response) = try await session.data(for: request)
        let httpResponse = response as! HTTPURLResponse

        guard 200..<300 ~= httpResponse.statusCode else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: httpResponse.statusCode)
        }

        return try decoder.decode(Response.self, from: data)
    }

    private func request<Response: Decodable, Body: Encodable>(
        _ url: URL,
        method: String,
        body: Body
    ) async throws -> Response {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.httpBody = try encoder.encode(body)
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")

        let (data, response) = try await session.data(for: request)
        let httpResponse = response as! HTTPURLResponse

        guard 200..<300 ~= httpResponse.statusCode else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: httpResponse.statusCode)
        }

        return try decoder.decode(Response.self, from: data)
    }
}

public enum PickleballTrainingGenieError: Error, Equatable {
    case invalidResponse(statusCode: Int)
}
