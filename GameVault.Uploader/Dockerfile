# Build from the repository root:  docker build -f GameVault.Uploader/Dockerfile -t gamevault-uploader .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GameVault.Uploader/ GameVault.Uploader/
RUN dotnet publish GameVault.Uploader/GameVault.Uploader.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    FILES_DIRECTORY=/files
EXPOSE 8080
# Runs as the image's unprivileged user unless the compose file sets "user:" to the owner of the games folder
USER $APP_UID
ENTRYPOINT ["dotnet", "GameVault.Uploader.dll"]
