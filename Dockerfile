FROM node:22-bookworm-slim AS frontend
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY server/AccessLedger.Api.csproj server/
RUN dotnet restore server/AccessLedger.Api.csproj
COPY server/ server/
COPY --from=frontend /src/server/wwwroot/ server/wwwroot/
RUN dotnet publish server/AccessLedger.Api.csproj -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /publish/ .
RUN mkdir /data && chown "$APP_UID" /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 DataDirectory=/data ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "AccessLedger.Api.dll"]
