FROM ubuntu:24.04

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

RUN apt update && apt install -y clang
COPY MehrakCore.csproj .
RUN dotnet restore --runtime linux-x64 MehrakCore.csproj

COPY . .
RUN dotnet publish MehrakCore.csproj -c Release -r linux-x64 -o out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy the published bot from the publish stage
COPY --from=build /app/out/ /app/

# Your bot's entry point
ENTRYPOINT ["/app/MehrakCore"]
