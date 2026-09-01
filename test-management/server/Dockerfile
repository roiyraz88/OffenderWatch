# Bonus B-05 — One-command startup (docker compose up --build).
#
# Build context is the REPO ROOT (see docker-compose.yml), not this
# directory: RunOrchestrator (Step 4/7/9) starts pytest and Playwright as
# child processes from automation/api and automation/ui, which
# RunnerOptions.RepoRootRelativeToContentRoot ("../..") expects to find as
# siblings of test-management/server — so the image reproduces that exact
# repo-relative layout under /repo, unchanged from local development.

# ---- build: publish the ASP.NET Core API -----------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY test-management/server/OffenderWatch.TestManagement.Server.csproj test-management/server/
RUN dotnet restore test-management/server/OffenderWatch.TestManagement.Server.csproj
COPY test-management/server/ test-management/server/
RUN dotnet publish test-management/server/OffenderWatch.TestManagement.Server.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Automation runtime deps (Step 4/8/9) — the SAME two toolchains local
# development already assumes (Python/pytest, Node/Playwright), just their
# Linux builds. Nothing here is a new dependency; every version below comes
# from the repo's own requirements.txt / package.json — see the COPY steps.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       python3 python3-pip python3-venv ca-certificates curl gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /repo

# --- Python (pytest) suite — automation/api, exact pinned versions ---
COPY automation/api/requirements.txt automation/api/requirements.txt
RUN pip3 install --break-system-packages --no-cache-dir -r automation/api/requirements.txt
COPY automation/api/ automation/api/

# --- Playwright (UI) suite — automation/ui, exact pinned version ---
COPY automation/ui/package.json automation/ui/package-lock.json automation/ui/
RUN cd automation/ui && npm ci
COPY automation/ui/ automation/ui/
# --with-deps installs the Linux OS libraries Chromium itself needs, in
# addition to the browser binary — required for a clean Docker image (no
# host-machine browser install to fall back on, unlike local development).
RUN cd automation/ui && npx playwright install --with-deps chromium

# --- Published API ---
COPY --from=build /app/publish test-management/server/

WORKDIR /repo/test-management/server

# The container interface, not localhost-only (12/"listen on the container
# interface"). "Docker" is a distinct ASPNETCORE_ENVIRONMENT from
# "Development" — appsettings.Docker.json layers Docker-only overrides
# (Runner.PythonExecutable/PlaywrightExecutableRelativePath, ClientOrigins)
# without touching appsettings.Development.json (still used by local
# `dotnet run` on Windows/macOS/Linux, Step 13).
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=5 \
    CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "OffenderWatch.TestManagement.Server.dll"]
