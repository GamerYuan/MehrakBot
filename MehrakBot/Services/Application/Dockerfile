ARG BASE_IMAGE_TAG=opencv-4.12.0-ocvsharp-4.11.0.20250507
FROM ghcr.io/gameryuan/mehrak-app-base:${BASE_IMAGE_TAG} AS build
WORKDIR /app

COPY . .

ARG ASPNETCORE_ENVIRONMENT=Production
ARG VERSION=0.0.0

RUN dotnet restore --runtime linux-x64 Services/Application/Mehrak.Application/Mehrak.Application.csproj
RUN dotnet publish Services/Application/Mehrak.Application/Mehrak.Application.csproj \
    -c $([ "$ASPNETCORE_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 \
    -p:Version=$VERSION \
    -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app

COPY --from=build /usr/lib /usr/lib
COPY --from=build /artifacts /artifacts
COPY --from=build /app/out /app
ENTRYPOINT ["/app/Mehrak.Application"]
