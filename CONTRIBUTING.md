# Contributing

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker](https://www.docker.com/)
- [Node.js 24.x](https://nodejs.org/en)
- [PostgreSQL](https://www.postgresql.org/)
- [Visual Studio](https://visualstudio.microsoft.com/)/[Jetbrains Rider](https://www.jetbrains.com/rider/)/[Visual Studio Code](https://code.visualstudio.com/) w/ [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
- [Discord Developer Account](https://discord.com/developers) and an [App Token](https://docs.discord.com/developers/quick-start/getting-started)

## Local Testing

To manually test your changes, follow these steps to build and run Mehrak locally. Ensure that you execute these commands from the root of the repository

1. Setup `.env` with the provided `.env.template` file

2. Make a copy of `appsettings.json` in `Services/Application/Mehrak.Application/`, `Services/Bot/Mehrak.Bot`, `Services/Dashboard/Mehrak.Dashboard`, rename them as `appsettings.Development.json`, and setup the appropriate values

3. Start Docker services

```
docker compose -f docker-compose.development.yml up -d postgres redis seaweed-master \
    seaweed-volume seaweed-filer seaweed-s3
```

4. Build and run from your IDE

Alternatively, you can build the docker images and run from docker directly

```
docker buildx bake -f docker-compose.development.yml
docker compose up -d --no-build
```

Once the services are running, you should see the Bot online, and you can start interacting with the Bot through slash commands
Should you not see a newly added command or an error saying `Command is outdated` when invoking your command, press `Ctrl + R` to reload Discord and try again

## Making Changes

See the [Documentation](docs) on the code architecture, and how you could integrate new commands, application logic or other items to the project
