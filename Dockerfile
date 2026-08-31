# syntax=docker/dockerfile:1

# ─────────────────────────────────────────────────────────────────────────────
# Etapa 1 — CSS de Tailwind
#
# Aislada en una imagen Node en lugar de instalar npm en la imagen del SDK: la capa de
# node_modules sólo se invalida cuando cambia package-lock.json, no en cada cambio de C#.
# Sin esta etapa la aplicación se publicaría sin hoja de estilos.
# ─────────────────────────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:22-alpine AS css
WORKDIR /src/TranslationService.Client

COPY TranslationService.Client/package.json TranslationService.Client/package-lock.json ./
RUN npm ci --no-audit --no-fund

# Tailwind necesita ver los .razor para saber qué clases conservar en el purgado.
COPY TranslationService.Client/ ./
RUN npm run build:css


# ─────────────────────────────────────────────────────────────────────────────
# Etapa 2 — Restore, build y publish
#
# --platform=$BUILDPLATFORM es obligatorio, no una optimización: compilar en una imagen
# amd64 emulada sobre Apple Silicon hace que MSBuild aborte con AccessViolationException.
# Aquí se compila de forma nativa en la arquitectura del runner.
# ─────────────────────────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Los .csproj se copian antes que el código para que `dotnet restore` quede en su propia
# capa: cambiar una línea de C# no obliga a volver a descargar todos los paquetes.
COPY Directory.Build.props Directory.Packages.props ./
COPY TranslationService.Api/TranslationService.Api.csproj                       TranslationService.Api/
COPY TranslationService.Application/TranslationService.Application.csproj       TranslationService.Application/
COPY TranslationService.Client/TranslationService.Client.csproj                 TranslationService.Client/
COPY TranslationService.Contracts/TranslationService.Contracts.csproj           TranslationService.Contracts/
COPY TranslationService.Domain/TranslationService.Domain.csproj                 TranslationService.Domain/
COPY TranslationService.Infrastructure/TranslationService.Infrastructure.csproj TranslationService.Infrastructure/
RUN dotnet restore TranslationService.Api/TranslationService.Api.csproj

COPY . .
COPY --from=css /src/TranslationService.Client/wwwroot/css/app.css TranslationService.Client/wwwroot/css/app.css

# UseAppHost=false evita generar el ejecutable nativo, que sería específico de la
# arquitectura de compilación. La salida queda como IL portable, que la imagen de runtime
# amd64 ejecuta mediante `dotnet <dll>` sin importar dónde se compiló.
RUN dotnet publish TranslationService.Api/TranslationService.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:SkipTailwindBuild=true \
        -p:UseAppHost=false


# ─────────────────────────────────────────────────────────────────────────────
# Etapa 3 — Runtime
#
# Imagen aspnet (no sdk): sin compilador ni herramientas de build, menor tamaño y menor
# superficie de ataque. Es la única etapa de la arquitectura de destino.
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Usuario sin privilegios provisto por la imagen oficial: nunca root en producción.
USER $APP_UID

ENTRYPOINT ["dotnet", "TranslationService.Api.dll"]
