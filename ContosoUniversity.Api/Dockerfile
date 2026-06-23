# syntax=docker/dockerfile:1
# Multi-stage build for the REST API. Build context is the repo root:
#   docker build -f ContosoUniversity.Api/Dockerfile -t contoso-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ContosoUniversity.sln ./
COPY ContosoUniversity.Api/ContosoUniversity.Api.csproj ContosoUniversity.Api/
COPY ContosoUniversity.Common/ContosoUniversity.Common.csproj ContosoUniversity.Common/
COPY ContosoUniversity.Data/ContosoUniversity.Data.csproj ContosoUniversity.Data/
RUN dotnet restore ContosoUniversity.Api/ContosoUniversity.Api.csproj

COPY . .
RUN dotnet publish ContosoUniversity.Api/ContosoUniversity.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ContosoUniversity.Api.dll"]
