# Developer Documentation

This document is intended for developers who want to contribute to the project.
It provides an overview of the project's architecture, the technologies used, and the development process.

## Tools

### Required Tools

- Latest .NET 9 SDK (https://dotnet.microsoft.com/download/dotnet/9.0)
- Dotnet ef tools (9.x)
- VS2022 (Developed on 17.14 Preview)
- Docker (Engine 27.+, https://docs.docker.com/desktop/)
- A modern web browser (Chrome, Firefox, etc.)
- Git (https://git-scm.com/)
- .NET MAUI Workload (https://docs.microsoft.com/en-us/dotnet/maui/get-started/installation)

### Recommended Tools

- JetBrains Writerside (2024.3 EAP)
- JetBrains Rider (2024.3.5)

## Architecture

The UI is built as a Blazor Hybrid App with both Web and App versions. 
The backend is a .NET 9 Web API with an SQL Server database.
To make development easier, we support both SQL Server and SQLite databases in local environments.

## Building the Project

To build all Projects for supported platforms, run the `dotnet build` command in the solution folder.
You can of course also build the individual projects for a specific platform/os with a specific configuration 
using the dotnet cli, for example, `dotnet build -c Release -p:Platform=AnyCPU -p:TargetFramework=net9.0`.
Generally speaking, the build output can be found under the solutions build/ or bin/ in the project folder.

## Publishing the Project

To publish the project for a specific platform, run the `dotnet publish` command.
The Web Application ships with a Dockerfile to build a Docker Image and a prebuilt compose file as well.
The Maui App can be published for Windows, macOS, Android, and iOS.

Reference:
- https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/?view=net-maui-9.0
- https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/?view=net-maui-9.0
- https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/overview?view=net-maui-9.0
- https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/deployment/?view=net-maui-9.0

## Tests

We use xUnit for unit tests and Selenium for UI tests.
The TestBase Project provides a base class for all tests and helper methods for tests.
We also use the Moq library for mocking dependencies and Shouldly for Assertions.
Each Platform is tested individually.
To execute all tests, run the `dotnet test` command in the solution folder.

## CI/CD

App packages and container images will be automatically built on release.
All Tests will run automatically run in a pipeline and are mandatory to pass for a pr to get merged.
Documentation will be auto deployed to GitHub pages on “master” push.

