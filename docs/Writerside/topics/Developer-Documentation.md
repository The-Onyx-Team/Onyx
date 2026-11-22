# Development

This chapter contains relevant information for the development and subsequent release process. It lists bugs, open issues, new ideas and other topics connected to the app. **Bugs** includes everything that actually has an effect on the use of the service. **Issues** includes everything that does not necessarily lock features but would definitely increase usability. **Ideas** keeps track of anything that would enhance the app's features.

Items marked **Done** are implemented; the note says where.

## Bugs

- **Done:** The login redirect does not work properly. *(Login returns to the page that required it via
  `?returnUrl=`, default `/home`; the app navigates after signing in; only local paths are accepted.)*
- **Done:** The Avatar image upload does not work. If it fails, at least display a reason. *(Pictures are scaled to
  256 px in the browser/web view and stored with the account; errors are shown.)*
- **Done:** Saving the Profile Settings does not work in general. *(Profile page saves user name and phone number,
  with validation messages.)*

## Issues

- **Done:** The landing page should not be visible at all on the android app. *(The app starts at the login page;
  public pages get a plain bar with a back button.)*
- **Done:** The app logo and favicon does not show correctly. *(Favicon on the web; logo in the app bars.)*
- **Done:** The app's icon is not correctly scaled, so the full onyx stone cannot be seen. *(Adaptive icon: cream
  background, stone scaled into the safe zone; new splash screen.)*
- **Done:** The app's appbars are obstructed by the phone's navigation bars *(`SafeAreaEdges="All"` on the main
  page, light system bar icons.)*
- **Done:** The "privacy notice" should be called "privacy policy" and it should be formatted, so it does not get
  blocked by the app bar. *(Operator and contact come from `Onyx__Operator` / `Onyx__ContactEmail`.)*
- **Done:** The font colour is not usable in the dark mode (should be a dark colour). *(Login/register card uses dark
  text in both themes; links and text buttons are lighter on dark surfaces.)*
- **Done:** The icons of the apps do not show properly in the web dashboard and are weirdly aligned in the android
  app. *(Icon and name share one cell with a fixed-size icon; apps without an icon show their first letter.)*
- **Done:** The top of the "home" page is not aligned correctly. *(Filters on one line with equal heights.)*

## Ideas

- **Done:** Some packages are not translated to app names -> needs a way to still assign the correct DisplayName and
  Category. *(Settings → Apps: own name and category per user, used by dashboard and reports.)*
- **Done:** Print weekly reports with plots describing usage data overall, for each device and analyse trends in the
  app usage. Keep the report in the Onyx design scheme and use python charts to give it a good look. These reports
  get a complete page on their own. *(`/reports`; charts rendered by `Reports/weekly_report.py` with matplotlib;
  Print / PDF on the web.)*

# Developer Documentation

This document is intended for developers who want to contribute to the project.
It provides an overview of the project's architecture, the technologies used, and the development process.

## Tools

### Required Tools

- .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)
- Dotnet ef tools (10.x)
- Python 3 with matplotlib, for the weekly report charts (`pacman -S python-matplotlib`, `apt install python3-matplotlib`)
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

