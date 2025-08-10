FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8888

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WheelBotApiApp/WheelBotApiApp.csproj", "WheelBotApiApp/"]
RUN dotnet restore "WheelBotApiApp/WheelBotApiApp.csproj"
COPY . .
WORKDIR "/src/WheelBotApiApp"
RUN dotnet build "./WheelBotApiApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WheelBotApiApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WheelBotApiApp.dll"]
