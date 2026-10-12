# =================================================================================================
# YDot.Integration worker image. Build context = the solution root:
#
#   docker build -f src/YDot.Integration.Worker/Dockerfile -t ydot-integration-worker:1.0.0 .
#
# or simply `docker compose build` with the docker-compose.yml at the solution root.
# =================================================================================================

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

# /app/data is the volume shared with the CAM API (uploads land in /app/data/inbox) and /app/logs
# the rolling log files. Both are created and handed to the non-root app user HERE: a named volume
# mounted on a folder that does not exist in the image is created root-owned, and the worker would
# then fail with "Access to the path '/app/data/inbox' is denied".
#
# libgssapi-krb5-2 is the Kerberos library the PostgreSQL driver looks for when it opens its first
# connection. The worker never uses Kerberos, and without the library everything still works - but
# the driver prints "Error: libgssapi_krb5.so.2: cannot open shared object file" at every start,
# which reads like a fault to whoever is watching the log. Installing it removes the line.
USER root
RUN apt-get update     && apt-get install -y --no-install-recommends libgssapi-krb5-2     && rm -rf /var/lib/apt/lists/*     && mkdir -p /app/data /app/logs     && chown -R $APP_UID /app/data /app/logs
USER $APP_UID

# ---- Build --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["src/YDot.Integration.Domain/YDot.Integration.Domain.csproj", "src/YDot.Integration.Domain/"]
COPY ["src/YDot.Integration.Application/YDot.Integration.Application.csproj", "src/YDot.Integration.Application/"]
COPY ["src/YDot.Integration.Infrastructure/YDot.Integration.Infrastructure.csproj", "src/YDot.Integration.Infrastructure/"]
COPY ["src/YDot.Integration.Worker/YDot.Integration.Worker.csproj", "src/YDot.Integration.Worker/"]
RUN dotnet restore "./src/YDot.Integration.Worker/YDot.Integration.Worker.csproj"
COPY . .
WORKDIR "/src/src/YDot.Integration.Worker"
RUN dotnet build "./YDot.Integration.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YDot.Integration.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# ---- Runtime ------------------------------------------------------------------------------------
FROM base AS final
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production \
    Worker__DataRoot=/app/data
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YDot.Integration.Worker.dll"]
