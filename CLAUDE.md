# CLAUDE.md

Instructions for Claude Code when working in this repository.

## Always build and test before declaring a task done

Before saying any task is complete, always:

1. **Build every project you touched** (and any project that depends on it). There is no
   solution (`.sln`) file in this repo, so build each `.csproj` individually, e.g.:
   ```
   dotnet build Services/Services.csproj
   dotnet build AiDashboard/AiDashboard.csproj
   ```
   If you're not sure what else might be affected, build all projects:
   ```
   AI/AI.csproj
   AiDashboard/AiDashboard.csproj
   Application.AI.Tests/Application.AI.Tests.csproj
   Entities/Entities.csproj
   Factories/Factories.csproj
   Infrastructure.Data.Dapper/Infrastructure.Data.Dapper.csproj
   OfflineAI/OfflineAI.csproj
   OfflineAI.Api/OfflineAI.Api.csproj
   OfflineAI.Api.Tests/OfflineAI.Api.Tests.csproj
   Presentation.AiDashboard.Tests/Presentation.AiDashboard.Tests.csproj
   Services.Tests/Services.Tests.csproj
   Services/Services.csproj
   Types/Types.csproj
   ```
2. **Run the full unit test suite** for every affected test project, not just the tests you
   added:
   ```
   dotnet test Services.Tests/Services.Tests.csproj
   dotnet test Presentation.AiDashboard.Tests/Presentation.AiDashboard.Tests.csproj
   dotnet test Application.AI.Tests/Application.AI.Tests.csproj
   dotnet test OfflineAI.Api.Tests/OfflineAI.Api.Tests.csproj
   ```
3. Only report a task as finished once the build is clean (0 errors) and every test suite you
   ran shows all tests passing. If a build or test fails, fix it before moving on — don't leave
   it for a future session to discover.

This applies even when the change looks self-contained (e.g. editing one file) — this repo has
had breakage slip through where a change compiled fine in isolation but broke a project that
depends on it (or a project quietly using a renamed/removed type), and that could have been
caught by simply building everything first.
