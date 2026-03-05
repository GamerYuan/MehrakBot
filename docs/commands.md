# Command Modules Guide

This document explains how command modules are structured in `Mehrak.Bot`, and what contributors must do when adding:

- a new command under an existing module
- a new top-level module

## What Command Modules Are

In this project, command modules are classes that inherit from `ApplicationCommandModule<ApplicationCommandContext>` and expose Discord slash commands.

Examples:

- `Mehrak.Bot/Modules/GenshinCommandModule.cs`
- `Mehrak.Bot/Modules/HsrCommandModule.cs`
- `Mehrak.Bot/Modules/Common/DailyCheckInCommandModule.cs`

Each module is responsible for:

- Defining command metadata via attributes (`[SlashCommand]`, `[SubSlashCommand]`, parameter attributes)
- Converting Discord input into a normalized internal parameter dictionary
- Choosing an internal command key from `Mehrak.Domain.Common.CommandName`
- Delegating execution to `CommandExecutorService` using `ICommandExecutorBuilder`

The module does not execute game/business logic directly. It only validates user input at the bot boundary and dispatches work to `Mehrak.Application` through gRPC.

## Module Patterns In This Repository

### Pattern A: Root command + subcommands (game modules)

Used by `GenshinCommandModule` and `HsrCommandModule`.

- Root command: `[SlashCommand("genshin", ...)]` / `[SlashCommand("hsr", ...)]`
- Subcommands: `[SubSlashCommand("character", ...)]`, `[SubSlashCommand("notes", ...)]`, etc.
- Common behavior:
  - Add parameters (for example `character`, `server`, `game`)
  - Set internal command key (for example `CommandName.Genshin.Character`)
  - Optionally set validators and ephemeral response
  - Call `await executor.ExecuteAsync(profile)`

### Pattern B: Single top-level command module

Used by `DailyCheckInCommandModule`.

- Top-level command: `[SlashCommand("checkin", ...)]`
- No server validation required (`ValidateServer(false)`)
- Response is ephemeral (`WithEphemeralResponse(true)`)
- Uses `CommandName.Common.CheckIn`

## Runtime Discovery and Dispatch

### Bot side discovery

`Mehrak.Bot/Program.cs` calls:

- `host.AddModules(typeof(Program).Assembly);`

So command modules in the bot assembly are discovered automatically. No manual module registration list is needed.

### Internal command routing key

Every command dispatched from bot to application uses a string key from:

- `Mehrak.Domain/Common/CommandName.cs`

Example keys:

- `"genshin character"`
- `"hsr character"`
- `"check in"`

### Application side resolution

`CommandDispatcher` resolves handlers with keyed DI:

- `GetKeyedService<IApplicationService>(command.Request.CommandName)`

Therefore, every new command key must have a matching keyed registration in `Mehrak.Application`.

## Add A New Command Under An Existing Module

Use this checklist when adding a new subcommand under existing roots like `/genshin` or `/hsr`.

### 1. Add internal command key

Update `Mehrak.Domain/Common/CommandName.cs` with a new constant in the correct nested class.

Example:

```csharp
public static class Genshin
{
    public const string NewMode = "genshin newmode";
}
```

### 2. Add bot command method

In the module (for example `GenshinCommandModule`):

- Add `[SubSlashCommand("newmode", "...")]`
- Define parameters with `[SlashCommandParameter(...)]`
- Build `parameters` list and include `("game", Game.Genshin)` if game-scoped
- Set `.WithCommandName(CommandName.Genshin.NewMode)`
- Add validators if needed
- Execute via `await executor.ExecuteAsync(profile)`

### 3. Add application service implementation

Create an `IApplicationService` implementation in `Mehrak.Application`, usually under the related game folder.

- Parse parameters from `IApplicationContext`
- Retrieve profile/authenticated game data
- Call GameApi/infrastructure services
- Return `CommandResult.Success(...)` or `CommandResult.Failure(...)`

If this command renders images, add an `ICardService<T>` and wire it similarly to existing card commands.

### 4. Register keyed service in DI

Add keyed registration in the proper extension class, for example:

- `Mehrak.Application/Services/Genshin/GenshinApplicationServiceExtensions.cs`
- `Mehrak.Application/Services/Hsr/HsrApplicationServiceExtensions.cs`
- or `Mehrak.Application/ApplicationServiceCollectionExtension.cs` (common commands)

Example:

```csharp
services.AddKeyedTransient<IApplicationService, GenshinNewModeApplicationService>(CommandName.Genshin.NewMode);
```

If applicable, register card service:

```csharp
services.AddSingleton<ICardService<NewModeData>, GenshinNewModeCardService>();
services.RegisterAsyncInitializableFor<ICardService<NewModeData>, GenshinNewModeCardService>();
```

### 5. Update help text

Update help outputs so users can discover the command:

- `GetHelpString(...)` in module class
- `Mehrak.Bot/Modules/Common/HelpCommandModule.cs` summary list and switch mapping if needed

### 6. Verify end-to-end

Minimum validation:

- Slash command appears and is callable
- Bot sends correct internal `CommandName`
- Application resolves keyed handler (no "No service registered" warning)
- Result reaches Discord with expected ephemeral/public behavior

## Add A New Top-Level Module

Use this checklist when creating a new root command like `/mygame`.

### 1. Create module class

Add new module in `Mehrak.Bot/Modules`:

- Inherit `ApplicationCommandModule<ApplicationCommandContext>`
- Add `[SlashCommand("mygame", "...")]`
- Optionally add `[RateLimit<ApplicationCommandContext>]`
- Inject `ICommandExecutorBuilder` and logger
- Add subcommands that dispatch using `WithCommandName(...)`

### 2. Add command name constants

Define a nested class in `CommandName.cs` (or extend existing) with all command keys this module uses.

### 3. Implement and register application handlers

Create `IApplicationService` handlers in `Mehrak.Application/Services/<Area>` and register all keys with `AddKeyedTransient<IApplicationService, ...>(key)`.

### 4. Register supporting services

If needed, register card/render/cache services in relevant extension class.

### 5. Update help command

Add module support in `HelpCommandModule`:

- include module in command list
- add switch case to route `/help mygame` to module `GetHelpString(...)`

### 6. Smoke test

- Ensure bot starts and module is auto-discovered by `host.AddModules(...)`
- Ensure each subcommand resolves correct application handler key

## Contributor Tips

- Keep slash command parameter names aligned with application expectations (`context.GetParameter("...")`).
- Use `nameof(parameter)` when adding parameters to reduce typo risk.
- Keep command keys stable once released, since they are the routing contract between bot and application.
- Prefer early validation in module/executor for user-facing input errors; keep business validation in application service.
- For user-facing docs consistency, update both `GetHelpString(...)` and external documentation when adding commands.
