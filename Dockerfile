FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY app/Api/Api.csproj app/Api/packages.lock.json app/Api/
COPY app/Domain/Domain.csproj app/Domain/packages.lock.json app/Domain/
RUN dotnet restore app/Api/Api.csproj --locked-mode
COPY app/Api/ app/Api/
COPY app/Domain/ app/Domain/
RUN dotnet publish app/Api/Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
