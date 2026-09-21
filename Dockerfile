# --- Derleme aşaması ---
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Önce sadece proje dosyasını kopyalayıp restore et: bağımlılıklar değişmediği sürece
# katman önbellekten gelir ve kaynak kod değişikliklerinde yeniden indirilmez.
COPY src/Findikhane.Api/Findikhane.Api.csproj Findikhane.Api/
RUN dotnet restore Findikhane.Api/Findikhane.Api.csproj

COPY src/Findikhane.Api/ Findikhane.Api/
RUN dotnet publish Findikhane.Api/Findikhane.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# --- Çalışma zamanı aşaması ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S findikhane && adduser -S findikhane -G findikhane
COPY --from=build /app/publish .
RUN chown -R findikhane:findikhane /app
USER findikhane

ENV PORT=8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Findikhane.Api.dll"]
