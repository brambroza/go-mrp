# syntax=docker/dockerfile:1

# MRP API image. Build context is the api/ directory:
#   docker build -t mrp-api:dev api/
#
# The same image runs in two modes:
#   dotnet Mrp.Api.dll           -> HTTP API on port 8080
#   dotnet Mrp.Api.dll migrate   -> apply EF Core migrations + Row-Level Security, then exit
#
# Base image choice (runtime): aspnet:10.0-alpine + tzdata.
#   - The API uses IANA time zones (Asia/Bangkok), so tzdata is mandatory.
#   - The API forces invariant culture itself, so ICU is not needed.
#   - Sizes measured on 2026-09-29 (uncompressed): alpine 130 MB, noble-chiseled 131 MB (no tzdata),
#     noble-chiseled-extra 170 MB (tzdata + ICU we do not use), default noble 262 MB.
#   Alpine plus the ~3 MB tzdata package is therefore the smallest variant that has time zone data.
#   If a future native dependency needs glibc, change the runtime stage to
#   aspnet:10.0-noble-chiseled-extra, drop the "apk add" line and use the non-Alpine SDK image
#   (sdk:10.0) in the build stage so that the RID becomes linux-x64 instead of linux-musl-x64.

ARG DOTNET_VERSION=10.0

# ---------- build ----------
# The SDK always runs on the build machine's architecture and cross-compiles to TARGETARCH,
# which avoids slow emulation when building linux/amd64 images on an arm64 laptop.
# "--arch" maps Docker's amd64/arm64 to the .NET RIDs linux-musl-x64/linux-musl-arm64
# (the OS part comes from the Alpine SDK image).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build
ARG TARGETARCH
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    NUGET_XMLDOC_MODE=skip
WORKDIR /source

# Restore in its own layer: it is only invalidated when a project file or the SDK pin changes.
# When a new module project is added under src/, add its .csproj here.
COPY global.json ./
COPY src/Mrp.Api/Mrp.Api.csproj src/Mrp.Api/
COPY src/Mrp.SharedKernel/Mrp.SharedKernel.csproj src/Mrp.SharedKernel/
COPY src/Modules/Mrp.Inventory/Mrp.Inventory.csproj src/Modules/Mrp.Inventory/
COPY src/Modules/Mrp.Masters/Mrp.Masters.csproj src/Modules/Mrp.Masters/
COPY src/Modules/Mrp.Platform/Mrp.Platform.csproj src/Modules/Mrp.Platform/
COPY src/Modules/Mrp.Production/Mrp.Production.csproj src/Modules/Mrp.Production/
COPY src/Modules/Mrp.Purchasing/Mrp.Purchasing.csproj src/Modules/Mrp.Purchasing/
RUN --mount=type=cache,id=mrp-nuget,target=/root/.nuget/packages \
    dotnet restore src/Mrp.Api/Mrp.Api.csproj --arch "$TARGETARCH"

COPY src/ src/
RUN --mount=type=cache,id=mrp-nuget,target=/root/.nuget/packages \
    dotnet publish src/Mrp.Api/Mrp.Api.csproj \
      --configuration Release \
      --no-restore \
      --arch "$TARGETARCH" \
      --self-contained false \
      -p:UseAppHost=false \
      --output /app/publish

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS runtime
# tzdata: IANA time zones. krb5-libs: Npgsql probes for GSSAPI on every first connection and
# prints "Cannot load library libgssapi_krb5.so.2" to stderr without it (harmless, but noisy logs).
RUN apk add --no-cache tzdata krb5-libs
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    # The API sets invariant culture in code; keep ICU out of the image.
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    TZ=UTC
WORKDIR /app
COPY --from=build /app/publish ./

# Non-root user that ships with the official .NET images (UID 1654).
USER $APP_UID
EXPOSE 8080

# ENTRYPOINT (not CMD) so that `command: ["migrate"]` / Cloud Run `args: ["migrate"]` is appended.
ENTRYPOINT ["dotnet", "Mrp.Api.dll"]
