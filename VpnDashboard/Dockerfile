FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["VpnDashboard/VpnDashboard.csproj", "VpnDashboard/"]
RUN dotnet restore "VpnDashboard/VpnDashboard.csproj"
COPY . .
WORKDIR "/src/VpnDashboard"
RUN dotnet build "./VpnDashboard.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./VpnDashboard.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
# Каталог для SQLite-файла (монтируется как volume). Создаём с правами app-пользователя.
USER root
RUN mkdir -p /data && chown -R $APP_UID /data
USER $APP_UID
COPY --from=publish /app/publish .
ENV ConnectionStrings__DefaultConnection="Data Source=/data/vpndashboard.db"
ENTRYPOINT ["dotnet", "VpnDashboard.dll"]
