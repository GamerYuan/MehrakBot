# Contributing

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker](https://www.docker.com/)
- [Visual Studio](https://visualstudio.microsoft.com/)/[Jetbrains Rider](https://www.jetbrains.com/rider/)/[Visual Studio Code](https://code.visualstudio.com/) w/ [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
- [Discord Developer Account](https://discord.com/developers) and an [App Token](https://docs.discord.com/developers/quick-start/getting-started)

## Local Testing

To manually test your changes, follow these steps to build and run Mehrak locally. Ensure that you execute these commands from the root of the repository

1. Assets (game images, fonts, etc.) live in the `MehrakBot/Assets` git submodule with LFS files. Pull them with:

```
git submodule update --init --recursive
git lfs pull
```

2. Setup `.env.local` from `.env.template` file

3. Make a copy of `appsettings.json` in `Services/Application/Mehrak.Application/`, `Services/Bot/Mehrak.Bot`, `Services/Dashboard/Mehrak.Dashboard`, rename them as `appsettings.Development.json`, and setup the appropriate values

4. Start all services with Aspire

```
dotnet run --project MehrakBot/Mehrak.AppHost
```

Alternatively, you can run `aspire run` from the root of the repository to start all services.

This starts all infrastructure (PostgreSQL, Redis, SeaweedFS, ClickHouse) and application services automatically. The Aspire dashboard opens in your browser.

Alternatively, you can use the production docker compose directly:

```
docker compose --env-file .env.local up -d
```

Once the services are running, you should see the Bot online, and you can start interacting with the Bot through slash commands
Should you not see a newly added command or an error saying `Command is outdated` when invoking your command, press `Ctrl + R` to reload Discord and try again

## Making Changes

See the [Documentation](docs) on the code architecture, and how you could integrate new commands, application logic or other items to the project
