# AGENTS.md

## Build & Run

All `dotnet` commands run from `MehrakBot/`, **not** the repo root:

```
cd MehrakBot
dotnet build
dotnet test
dotnet format .          # analyzers + style
dotnet tool restore
```

Run a single test project or filter:

```
dotnet test Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj
dotnet test --filter "FullyQualifiedName~GenshinCharacter"
```

## Prerequisites

- **.NET 10 SDK**
- **Docker** — tests use Testcontainers (PostgreSQL, Redis, LocalStack); they will fail without a running Docker daemon
- **Git LFS + submodules** — assets live in `MehrakBot/Assets/` as a submodule with LFS files:
  ```
  git submodule update --init --recursive
  git lfs pull
  ```
- **Local infra** — start before running services manually:
  ```
  docker compose -f docker-compose.development.yml up -d postgres redis seaweed-master seaweed-volume seaweed-filer seaweed-s3
  ```
- Each service needs a manually created `appsettings.Development.json` copied from its `appsettings.json` (Application, Bot, Dashboard)

## Solution Layout

The `.slnx` is at `MehrakBot/MehrakBot.slnx`. Repo root only holds Docker/CI/docs.

```
MehrakBot/
  Common/
    Mehrak.Domain          — models, DTOs, protobuf, CommandName constants
    Mehrak.GameApi         — HoYoLAB API wrapper
    Mehrak.Infrastructure  — EF Core DbContexts, Redis, S3, ClickHouse
  Services/
    Application/           — gRPC server (core logic, card rendering)
    Bot/                   — Discord entry point (NetCord), auto-discovers modules
    Bot.Generators/        — Roslyn source generator (targets netstandard2.0)
    Dashboard/             — ASP.NET Web API admin backend
```

## Architecture Gotchas

**Three-process system.** Bot and Dashboard are gRPC clients; Application is the gRPC server. All three must run for end-to-end testing.

**Command dispatch is keyed DI.** Every command needs:

1. A key in `Common/Mehrak.Domain/Shared/Common/CommandName.cs`
2. A `[SlashCommand]`/`[SubSlashCommand]` method in a Bot module
3. An `IApplicationService` implementation in Application
4. A `services.AddKeyedTransient<IApplicationService, T>(key)` registration in the game's `*ApplicationServiceExtensions.cs`

**Card rendering.** `ICardService<TData>` implementations must track all `Image` objects in a list and dispose them in `finally`. Use deterministic SHA256 filenames via `GetFileName()`. Check `AttachmentExistsAsync` before regenerating.

**Seven EF DbContexts** in `Mehrak.Infrastructure`: `CharacterDbContext`, `RelicDbContext`, `UserDbContext`, `CodeRedeemDbContext`, `DashboardAuthDbContext`, `DocumentationDbContext`, `ReleaseNoteDbContext`. Migration commands must specify `--context <Name>`.

**Source generator** (`Mehrak.Bot.Generators`) targets `netstandard2.0` — cannot reference .NET 10 APIs.

## Code Style

From `.editorconfig` — these differ from C# defaults:

- Private instance fields: `m_PascalCase` (e.g., `m_ImageRepository`)
- Private static fields: `_camelCase`
- Private constants and static readonly: `PascalCase`
- File encoding: `utf-8-bom`, line endings: `crlf`
- Prefer `var` when type is apparent

## Testing

- **NUnit 4.x** + **Moq**
- Infrastructure tests spin up real PostgreSQL/Redis via Testcontainers
- Application tests use Testcontainers.LocalStack for S3 and compare card images via perceptual hashing
- Test fixtures go in `TestData/` directories; helpers in `TestUtils/`
- All production projects have `InternalsVisibleTo` for their test projects

## CI & Deploy

- **CI** (`validation.yml`): runs on `ubuntu-24.04` with .NET 10 SDK; does `dotnet restore` → `dotnet build --configuration Release` → `dotnet test`
- **Deploy** (`deploy.yml`): triggered by `v*` tags; builds Docker images, SCPs them to server
- Application Dockerfile uses a custom base image with OpenCV/OpenCvSharp preinstalled (`ghcr.io/gameryuan/mehrak-app-base`)

## Docs

Detailed guides in `docs/`: `architecture.md`, `commands.md`, `application-service.md`, `card-service.md`.
