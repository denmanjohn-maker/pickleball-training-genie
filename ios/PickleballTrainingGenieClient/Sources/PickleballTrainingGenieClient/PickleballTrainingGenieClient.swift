import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public struct HealthResponse: Codable, Equatable, Sendable {
    public let status: String
}

public struct Drill: Codable, Equatable, Identifiable, Sendable {
    public let id: String
    public let title: String
    public let description: String
    public let targetDUPRLevel: Decimal
    public let category: String
    public let videoUrl: String?
    public let sourceUrl: String
    public let createdAt: String
}

public struct User: Codable, Equatable, Sendable {
    public let id: String
    public let email: String
    public let currentDUPR: Decimal
    public let targetDUPR: Decimal
}

public struct LoginResponse: Codable, Equatable, Sendable {
    public let token: String
}

public struct MessageResponse: Codable, Equatable, Sendable {
    public let message: String
}

public struct WorkoutDrillItem: Codable, Equatable, Sendable {
    public let title: String
    public let category: String
    public let durationMinutes: Int
    public let coachingNotes: String
}

public struct WorkoutPlanResponse: Codable, Equatable, Sendable {
    public let drills: [WorkoutDrillItem]
    public let totalDuration: Int
    public let warmup: String
    public let cooldown: String
    public let coachingNotes: String
}

public class PickleballTrainingGenieClient {
    public let baseURL: URL
    public let session: URLSession
    public var jwtToken: String?
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()

    public init(baseURL: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.session = session
    }

    public func login(email: String, password: String) async throws -> LoginResponse {
        let payload = ["email": email, "password": password]
        let response: LoginResponse = try await request(url(path: "api/Users/login"), method: "POST", body: payload, requireAuth: false)
        self.jwtToken = response.token
        return response
    }

    public func register(email: String, password: String, currentDUPR: Decimal, targetDUPR: Decimal) async throws -> MessageResponse {
        let payload: [String: Any] = ["email": email, "password": password, "currentDUPR": NSDecimalNumber(decimal: currentDUPR).doubleValue, "targetDUPR": NSDecimalNumber(decimal: targetDUPR).doubleValue]
        let data = try JSONSerialization.data(withJSONObject: payload)
        var requestObj = URLRequest(url: url(path: "api/Users/register"))
        requestObj.httpMethod = "POST"
        requestObj.setValue("application/json", forHTTPHeaderField: "Accept")
        requestObj.setValue("application/json", forHTTPHeaderField: "Content-Type")
        requestObj.httpBody = data
        let (responseData, response) = try await session.data(for: requestObj)
        guard let httpResponse = response as? HTTPURLResponse, 200..<300 ~= httpResponse.statusCode else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: 0)
        }
        return try decoder.decode(MessageResponse.self, from: responseData)
    }

    public func drills(category: String? = nil, level: Decimal? = nil) async throws -> [Drill] {
        var components = try urlComponents(path: "api/Drills")
        var queryItems: [URLQueryItem] = []
        if let category = category {
            queryItems.append(URLQueryItem(name: "category", value: category))
        }
        if let level = level {
            queryItems.append(URLQueryItem(name: "level", value: NSDecimalNumber(decimal: level).stringValue))
        }
        if !queryItems.isEmpty {
            components.queryItems = queryItems
        }
        guard let url = components.url else {
            throw PickleballTrainingGenieError.invalidURL
        }
        return try await request(url, method: "GET", requireAuth: false)
    }

    public func recommendations() async throws -> [Drill] {
        return try await request(url(path: "api/Drills/recommendations"), method: "GET", requireAuth: true)
    }

    public func completeDrill(id: String) async throws -> MessageResponse {
        return try await request(url(path: "api/Drills/\(id)/complete"), method: "POST", body: [String: String](), requireAuth: true)
    }

    public func generateWorkout(durationMinutes: Int? = nil) async throws -> WorkoutPlanResponse {
        struct Request: Encodable { let durationMinutes: Int? }
        return try await request(url(path: "api/workouts/generate"), method: "POST", body: Request(durationMinutes: durationMinutes), requireAuth: true)
    }

    func urlComponents(path: String) throws -> URLComponents {
        guard let components = URLComponents(url: url(path: path), resolvingAgainstBaseURL: false) else {
            throw PickleballTrainingGenieError.invalidURL
        }
        return components
    }

    private func url(path: String) -> URL {
        baseURL.appending(path: path)
    }

    private func request<Response: Decodable>(
        _ url: URL,
        method: String,
        requireAuth: Bool
    ) async throws -> Response {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if requireAuth, let token = jwtToken {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let (data, response) = try await session.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: 0)
        }

        guard 200..<300 ~= httpResponse.statusCode else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: httpResponse.statusCode)
        }

        return try decoder.decode(Response.self, from: data)
    }

    private func request<Response: Decodable, Body: Encodable>(
        _ url: URL,
        method: String,
        body: Body,
        requireAuth: Bool
    ) async throws -> Response {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.httpBody = try encoder.encode(body)
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if requireAuth, let token = jwtToken {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let (data, response) = try await session.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: 0)
        }

        guard 200..<300 ~= httpResponse.statusCode else {
            throw PickleballTrainingGenieError.invalidResponse(statusCode: httpResponse.statusCode)
        }

        return try decoder.decode(Response.self, from: data)
    }
}

public enum PickleballTrainingGenieError: Error, Equatable {
    case invalidURL
    case invalidResponse(statusCode: Int)
}
