# syntax=docker/dockerfile:1
# Scotty webmail in one image: the API also serves the web pages. Build from the repository root:
#   docker build -f docker/Dockerfile --build-arg IMAGE_VERSION=$(cat docker/VERSION) \
#     --build-arg GIT_COMMIT=$(git rev-parse --short=7 HEAD) --build-arg GIT_REVISION=$(git rev-parse HEAD) .
# docker/README.md is the guide; docker/smoke-test.sh checks an image the way CI does.
# The base images are named by tag, not digest: each new build takes their latest fixes.

FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS web
ARG GIT_COMMIT=""
ARG RELEASE_BUILD=false
WORKDIR /src/frontend
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN GITHUB_SHA="$GIT_COMMIT" RELEASE_BUILD="$RELEASE_BUILD" npm run build -- --mode container

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
ARG GIT_COMMIT=""
ARG RELEASE_BUILD=false
WORKDIR /src
COPY src/Directory.Build.props src/
COPY src/scotty.microservice/VERSION src/scotty.microservice/scotty.microservice.core.csproj src/scotty.microservice/
COPY src/scotty.microservice.host/scotty.microservice.host.csproj src/scotty.microservice.host/
COPY src/scotty.providers.weesky/scotty.providers.weesky.csproj src/scotty.providers.weesky/
RUN dotnet restore src/scotty.microservice.host/scotty.microservice.host.csproj -a "$TARGETARCH"
COPY src/ src/
# The context carries no .git: the commit comes in as an argument, and SourceLink is told not to look.
RUN dotnet publish src/scotty.microservice.host/scotty.microservice.host.csproj \
      -c Release -a "$TARGETARCH" --self-contained false --no-restore \
      -p:ReleaseBuild="$RELEASE_BUILD" -p:SourceRevisionId="$GIT_COMMIT" \
      -p:EnableSourceControlManagerQueries=false \
      -p:SatelliteResourceLanguages= -p:DebugType=None -p:DebugSymbols=false \
      -o /out/app \
 && rm -rf /out/app/alpine /out/app/arm64 /out/app/CodeCoverage /out/app/macos /out/app/ubuntu /out/app/x64 /out/app/x86 \
 && mkdir /out/state

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
ARG IMAGE_VERSION
ARG GIT_REVISION=""
LABEL org.opencontainers.image.title="Scotty webmail" \
      org.opencontainers.image.source="https://github.com/darthmaul0181/weesky.net-mail" \
      org.opencontainers.image.licenses="AGPL-3.0" \
      org.opencontainers.image.version="$IMAGE_VERSION" \
      org.opencontainers.image.revision="$GIT_REVISION"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logs__Output=console \
    STATE_DIRECTORY=/var/lib/scotty \
    Platform=generic \
    Frontend__Path=/app/frontend \
    SCOTTY_IMAGE_VERSION=$IMAGE_VERSION \
    Mail__ImapHost= \
    Mail__SmtpHost= \
    Sieve__Host=
# The three hosts above are emptied on purpose: appsettings.json carries the original deployment's
# servers, and a line left out of .env must refuse to start rather than send passwords there.
WORKDIR /app
COPY --from=api /out/app ./
COPY --from=web /src/frontend/dist ./frontend
COPY --from=api --chown=$APP_UID:$APP_UID /out/state /var/lib/scotty
USER $APP_UID
EXPOSE 8080
VOLUME /var/lib/scotty
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --start-interval=2s --retries=3 \
  CMD ["/app/scotty.microservice", "healthcheck"]
ENTRYPOINT ["/app/scotty.microservice"]
