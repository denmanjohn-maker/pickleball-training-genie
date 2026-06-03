// swift-tools-version: 6.3
// The swift-tools-version declares the minimum version of Swift required to build this package.

import PackageDescription

let package = Package(
    name: "PickleballTrainingGenieClient",
    platforms: [
        .iOS(.v17),
        .macOS(.v14),
    ],
    products: [
        .library(
            name: "PickleballTrainingGenieClient",
            targets: ["PickleballTrainingGenieClient"]
        ),
    ],
    targets: [
        .target(
            name: "PickleballTrainingGenieClient"
        ),
        .testTarget(
            name: "PickleballTrainingGenieClientTests",
            dependencies: ["PickleballTrainingGenieClient"]
        ),
    ],
    swiftLanguageModes: [.v6]
)
