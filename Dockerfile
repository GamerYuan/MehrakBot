FROM ubuntu:24.04

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

ARG DOTNET_ENVIRONMENT=Production

RUN apt update && apt install -y clang

COPY . .

RUN dotnet restore --runtime linux-x64 MehrakCore/MehrakCore.csproj
RUN dotnet publish MehrakCore/MehrakCore.csproj \
    -c $([ "$DOTNET_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 -o out
RUN ls /app/out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy the published bot from the publish stage
COPY --from=build /app/out/ /app/

EXPOSE 9090

# Your bot's entry point
ENTRYPOINT ["/app/MehrakCore"]
