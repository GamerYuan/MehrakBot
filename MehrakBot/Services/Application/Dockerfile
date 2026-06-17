FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY . .

ARG ASPNETCORE_ENVIRONMENT=Production
ARG VERSION=0.0.0

RUN dotnet restore --runtime linux-x64 Services/Application/Mehrak.Application/Mehrak.Application.csproj
RUN --mount=type=secret,id=sixlabors-license \
    cp /run/secrets/sixlabors-license sixlabors.lic && \
    dotnet publish Services/Application/Mehrak.Application/Mehrak.Application.csproj \
    -c $([ "$ASPNETCORE_ENVIRONMENT" = "Development" ] && echo "Debug" || echo "Release" ) \
    -r linux-x64 \
    -p:Version=$VERSION \
    -o /app/out && \
    rm -f sixlabors.lic

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app

COPY --from=build /app/out /app
ENTRYPOINT ["/app/Mehrak.Application"]
