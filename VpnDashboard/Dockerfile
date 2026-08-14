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

# Ядро Xray для проверки серверов: поднимается на несколько секунд с конфигом записи,
# чтобы убедиться, что через сервер реально ходит трафик. Берём последний релиз апстрима;
# при желании версию можно закрепить, подставив тег вместо latest/download.
FROM alpine:3 AS xray
RUN apk add --no-cache curl unzip \
 && curl -fsSL -o /tmp/xray.zip https://github.com/XTLS/Xray-core/releases/latest/download/Xray-linux-64.zip \
 && unzip -j /tmp/xray.zip xray geoip.dat geosite.dat -d /xray

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./VpnDashboard.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
# Каталог для SQLite-файла (монтируется как volume). Создаём с правами app-пользователя.
USER root
RUN mkdir -p /data && chown -R $APP_UID /data
# Ядро и его geo-базы: правила роутинга внутри конфигов провайдеров ссылаются на geosite/geoip,
# без файлов ядро на таких конфигах не стартует.
COPY --from=xray /xray/xray /usr/local/bin/xray
COPY --from=xray /xray/geoip.dat /xray/geosite.dat /usr/local/share/xray/
RUN chmod 0755 /usr/local/bin/xray
USER $APP_UID
COPY --from=publish /app/publish .
ENV ConnectionStrings__DefaultConnection="Data Source=/data/vpndashboard.db"
ENV XRAY_LOCATION_ASSET="/usr/local/share/xray"
ENTRYPOINT ["dotnet", "VpnDashboard.dll"]
