FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

RUN apt update && apt install -y clang

COPY . .

ARG DOTNET_ENVIRONMENT=Production
RUN dotnet restore --runtime linux-x64 Mehrak.Bot/Mehrak.Bot.csproj
RUN dotnet publish Mehrak.Bot/Mehrak.Bot.csproj \
    -c $([ "$DOTNET_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS bot
WORKDIR /app

COPY --from=build /app/out /app
ENTRYPOINT ["/app/Mehrak.Bot"]
