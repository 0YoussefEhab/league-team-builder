# League Team Builder, web edition. Build from the repository root:  docker build -t league-team-builder .
# Not built or run by the author (no Docker in the authoring environment): the first `docker compose up --build` is the test.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, so a change in the code does not download the packages again.
COPY src/LeagueTeamBuilder/LeagueTeamBuilder.csproj src/LeagueTeamBuilder/
RUN dotnet restore src/LeagueTeamBuilder/LeagueTeamBuilder.csproj
COPY src/LeagueTeamBuilder/ src/LeagueTeamBuilder/
RUN dotnet publish src/LeagueTeamBuilder/LeagueTeamBuilder.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# ONE folder holds everything that must survive a restart: /data.
#   /data/leagueteambuilder.db   the SQLite database (Database__Path below)
#   /data/uploads                pictures people upload; the app writes to wwwroot/uploads, which is a link to this folder
# Mount a volume or disk at /data and that is the whole backup.
RUN mkdir -p /data/uploads \
 && rm -rf /app/wwwroot/uploads \
 && ln -s /data/uploads /app/wwwroot/uploads \
 && chown -R $APP_UID /data
ENV Database__Path=/data/leagueteambuilder.db

# Runs as the image's non-root user. If a host gives you a disk that this user cannot write to
# (the log then says "unable to open database file"), either fix the folder's owner or delete the next line.
USER $APP_UID

# The aspnet image listens on 8080 (ASPNETCORE_HTTP_PORTS). A host that tells you a port (usually in PORT) needs ASPNETCORE_HTTP_PORTS set to it.
EXPOSE 8080
# The mkdir covers a disk that starts empty and hides the folder made above.
ENTRYPOINT ["sh", "-c", "mkdir -p /data/uploads && exec dotnet LeagueTeamBuilder.dll"]
