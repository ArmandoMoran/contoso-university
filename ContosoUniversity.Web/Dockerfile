# syntax=docker/dockerfile:1
# Multi-stage build for the MVC site. Build context is the repo root:
#   docker build -f ContosoUniversity.Web/Dockerfile -t contoso-web .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project (and its referenced projects) first for layer caching.
COPY ContosoUniversity.sln ./
COPY ContosoUniversity.Web/ContosoUniversity.Web.csproj ContosoUniversity.Web/
COPY ContosoUniversity.Common/ContosoUniversity.Common.csproj ContosoUniversity.Common/
COPY ContosoUniversity.Data/ContosoUniversity.Data.csproj ContosoUniversity.Data/
RUN dotnet restore ContosoUniversity.Web/ContosoUniversity.Web.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish ContosoUniversity.Web/ContosoUniversity.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Run as the non-root user provided by the base image.
USER $APP_UID
ENTRYPOINT ["dotnet", "ContosoUniversity.Web.dll"]
