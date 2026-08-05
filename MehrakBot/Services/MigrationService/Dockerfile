FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY . .

ARG DOTNET_ENVIRONMENT=Production
ARG VERSION=0.0.0

RUN dotnet restore --runtime linux-x64 Services/MigrationService/Mehrak.MigrationService/Mehrak.MigrationService.csproj
RUN dotnet publish Services/MigrationService/Mehrak.MigrationService/Mehrak.MigrationService.csproj \
    -c $([ "$DOTNET_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 \
    -p:Version=$VERSION \
    -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migration
WORKDIR /app

COPY --from=build /app/out /app
ENTRYPOINT ["/app/Mehrak.MigrationService"]
