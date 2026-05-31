# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Mehrak Bot is a Discord bot for HoYoverse games (Genshin Impact, Honkai: Star Rail, Zenless Zone Zero, Honkai Impact 3rd). It provides slash commands for game data lookup, card image generation, code redemption, and daily check-in automation. A companion admin dashboard (ASP.NET Web API + Vue.js) allows non-technical contributors to manage content.

## Build & Test Commands

The .NET solution root is at `MehrakBot/` (one level deeper than the repo root).

```bash
# Build
cd MehrakBot
dotnet build

# Run all tests
dotnet test

# Run a single test project
dotnet test MehrakBot/Services/Application/Mehrak.Application.Tests/Mehrak.Application.Tests.csproj

# Run tests matching a filter
dotnet test --filter "FullyQualifiedName~GenshinCharacter"

# Format code
dotnet format .
```

### Docker Development

```bash
# Start infrastructure services
docker compose -f docker-compose.development.yml up -d postgres redis seaweed-master \
    seaweed-volume seaweed-filer seaweed-s3

# Build and run all services in Docker
docker buildx bake -f docker-compose.development.yml
docker compose up -d --no-build
```

## Architecture

### Solution Structure

The repo root contains Docker/infra configs. The actual .NET solution is in `MehrakBot/` with `MehrakBot.slnx`.

**Common libraries:**

- `Mehrak.Domain` — Models, DTOs, service abstractions, protobuf definitions, `CommandName` constants
- `Mehrak.GameApi` — HoYoLAB API wrapper
- `Mehrak.Infrastructure` — PostgreSQL, Redis, S3 (SeaweedFS), ClickHouse, Prometheus abstractions

**Services:**

- `Mehrak.Application` — ASP.NET gRPC server; core business logic, card rendering, command dispatch
- `Mehrak.Bot` — Console app; Discord entry point using NetCord, auto-discovers command modules
- `Mehrak.Bot.Generators` — Roslyn source generator (targets `netstandard2.0`)
- `Mehrak.Dashboard` — ASP.NET Web API; admin dashboard backend with cookie auth

### Request Flow

Bot and Dashboard are gRPC clients. Application is the gRPC server.

```
Discord User → Bot Module → CommandExecutorService → gRPC → GrpcApplicationService
    → CommandDispatcher (bounded channel, concurrency-limited) → keyed IApplicationService
    → optional ICardService → attachment storage → CommandResult → back to Bot → Discord
```

### Key Patterns

**Command dispatch:** Bot modules use `[SlashCommand]`/`[SubSlashCommand]` attributes and dispatch via `CommandExecutorService`. Application resolves handlers with keyed DI: `AddKeyedTransient<IApplicationService, T>(CommandName.Genshin.Character)`. Every command needs a matching key in `Mehrak.Domain/Shared/Common/CommandName.cs` and a keyed DI registration.

**Card rendering:** `ICardService<TData>` returns a `Stream` from `GetCardAsync(ICardGenerationContext<TData>)`. Services that preload assets also implement `IAsyncInitializable`. All `Image` objects must be tracked and disposed in `finally`. Generated cards use deterministic SHA256 filenames for caching.

**Configuration:** `IOptions<T>` pattern with `appsettings.json` + environment-specific overrides. Config classes: `CharacterCacheConfig`, `S3StorageConfig`, `RedisConfig`, `PgConfig`, `CommandDispatcherConfig`, `RateLimiterConfig`.

**Observability:** Serilog (Console, File, OpenTelemetry sinks), OpenTelemetry traces/metrics via OTLP, Prometheus via `prometheus-net`, ClickHouse for analytics.

## Naming Conventions

From `.editorconfig`:

- Private instance fields: `m_PascalCase` prefix (e.g., `m_ImageRepository`)
- Private static fields: `_camelCase` prefix
- Private constants and static readonly: `PascalCase`
- Prefer `var` when type is apparent
- `utf-8-bom` encoding, `crlf` line endings, 4-space indentation

## Adding a New Command

See `docs/commands.md` for the full guide. Summary:

1. Add command key to `Mehrak.Domain/Shared/Common/CommandName.cs`
2. Add bot command method in the appropriate module under `Mehrak.Bot/<Game>/` (e.g. `Mehrak.Bot/Genshin/`)
3. Implement `IApplicationService` in `Mehrak.Application/<Game>/` (e.g. `Mehrak.Application/Genshin/Character/`)
4. Register keyed service: `services.AddKeyedTransient<IApplicationService, T>(key)`
5. If card command: register `ICardService<TData>` (singleton) and optionally `RegisterAsyncInitializableFor`
6. Update help text in `HelpCommandModule`

## Adding a Card Renderer

See `docs/card-service.md` for the full guide. Key rules:

- Track all disposable `Image` objects in a list, dispose in `finally`
- Use deterministic filenames via `GetFileName(serializedData, extension, gameUid)`
- Check `AttachmentExistsAsync` before regenerating
- Throw `CommandException` on render failures
- Measure performance with `IApplicationMetrics.ObserveCardGenerationDuration`

## Testing

- **Framework:** NUnit 4.x with Moq for mocking
- **Infrastructure tests** use Testcontainers for real PostgreSQL and Redis
- **Application tests** use Testcontainers.LocalStack for S3
- All production projects have `InternalsVisibleTo` configured for their test projects
- Tests use `TestData/` directories for fixtures and `TestUtils/` for helpers
- CI runs on `ubuntu-24.04` with .NET 10 SDK

## Technology Stack

- **.NET 10.0** (all projects, except source generator targets `netstandard2.0`)
- **Discord:** NetCord 1.0.0-alpha.461
- **gRPC:** Grpc.AspNetCore 2.76.0 / Google.Protobuf 3.34.1
- **Image processing:** SixLabors.ImageSharp 3.1.12 + OpenCvSharp4 4.11.0
- **Database:** PostgreSQL via EF Core + Npgsql
- **Cache:** StackExchange.Redis
- **Object storage:** AWSSDK.S3 (backed by SeaweedFS in dev/docker)
- **Analytics:** ClickHouse
- **Code formatter:** CSharpier 1.1.2

## Documentation

Detailed guides are in `docs/`:

- `architecture.md` — Execution flow with sequence diagrams, error handling
- `commands.md` — How to add new commands (subcommands and top-level modules)
- `application-service.md` — How to implement command handlers
- `card-service.md` — How to implement card renderers

# Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)

- C# / code quality: modern-csharp-coding-standards, csharp-concurrency-patterns, api-design, type-design-performance
- ASP.NET Core / Web (incl. Aspire): aspire-service-defaults, aspire-integration-testing, transactional-emails
- Data: efcore-patterns, database-performance
- DI / config: dependency-injection-patterns, microsoft-extensions-configuration
- Testing: testcontainers-integration-tests, playwright-blazor-testing, snapshot-testing

Quality gates (use when applicable)

- dotnet-slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents

- dotnet-concurrency-specialist, dotnet-performance-analyst, dotnet-benchmark-designer, akka-net-specialist, docfx-specialist
