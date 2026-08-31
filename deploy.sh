#!/usr/bin/env bash
#
# Despliegue de TranslationServiceApp en Azure Container Apps.
#
# Es idempotente: puede ejecutarse tantas veces como se quiera; la primera crea los
# recursos y las siguientes actualizan la revisión con la imagen nueva.
#
# Requisitos previos:
#   - Docker en ejecución
#   - Azure CLI autenticado            (brew install azure-cli && az login)
#   - Un token de GitHub con write:packages para publicar en ghcr.io
#
set -euo pipefail

# ── Configuración ────────────────────────────────────────────────────────────
GITHUB_USER="${GITHUB_USER:?Define GITHUB_USER con tu usuario de GitHub}"
IMAGE_TAG="${IMAGE_TAG:-$(date +%Y%m%d%H%M%S)}"
# ghcr.io rechaza mayúsculas en la ruta. Se usa `tr` y no ${VAR,,} porque macOS trae
# bash 3.2, donde esa expansión es un error de sintaxis.
GITHUB_USER_LC="$(printf '%s' "${GITHUB_USER}" | tr '[:upper:]' '[:lower:]')"
IMAGE="ghcr.io/${GITHUB_USER_LC}/translation-api:${IMAGE_TAG}"

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-translation-service}"
# westeurope no admite clientes nuevos en esta suscripción ("locationineligible").
# germanywestcentral coincide además con la región del recurso de Azure Translator.
LOCATION="${LOCATION:-germanywestcentral}"

AZURE_TRANSLATOR_API_KEY="${AZURE_TRANSLATOR_API_KEY:?Define AZURE_TRANSLATOR_API_KEY}"
AZURE_TRANSLATOR_REGION="${AZURE_TRANSLATOR_REGION:?Define AZURE_TRANSLATOR_REGION}"

info() { printf '\n\033[1;34m▸ %s\033[0m\n' "$1"; }

# ── 1. Imagen ────────────────────────────────────────────────────────────────
info "Construyendo la imagen ${IMAGE}"
# linux/amd64 explícito: en un Mac con Apple Silicon el build por defecto sería arm64
# y Container Apps no podría ejecutarlo.
docker build --platform linux/amd64 -t "${IMAGE}" .

info "Publicando en GitHub Container Registry"
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
  echo "${GITHUB_TOKEN}" | docker login ghcr.io -u "${GITHUB_USER}" --password-stdin
fi
docker push "${IMAGE}"

echo
echo "⚠️  La imagen debe ser PÚBLICA para que Container Apps la descargue sin credenciales."
echo "    Si es la primera vez, hazla pública en:"
echo "    https://github.com/users/${GITHUB_USER_LC}/packages/container/translation-api/settings"
read -r -p "    Pulsa Enter cuando esté lista... "

# ── 2. Infraestructura ───────────────────────────────────────────────────────
info "Asegurando el grupo de recursos ${RESOURCE_GROUP} en ${LOCATION}"
az group create --name "${RESOURCE_GROUP}" --location "${LOCATION}" --output none

info "Desplegando la plantilla Bicep"
DEPLOYMENT_OUTPUT=$(az deployment group create \
  --resource-group "${RESOURCE_GROUP}" \
  --template-file infra/main.bicep \
  --parameters \
      containerImage="${IMAGE}" \
      azureTranslatorApiKey="${AZURE_TRANSLATOR_API_KEY}" \
      azureTranslatorRegion="${AZURE_TRANSLATOR_REGION}" \
      location="${LOCATION}" \
  --query properties.outputs \
  --output json)

APP_URL=$(echo "${DEPLOYMENT_OUTPUT}" | python3 -c "import sys,json;print(json.load(sys.stdin)['containerAppUrl']['value'])")

# ── 3. Comprobación ──────────────────────────────────────────────────────────
info "Esperando a que la aplicación responda"
# La primera petición despierta la réplica desde cero: puede tardar decenas de segundos.
for _ in $(seq 1 30); do
  if curl -fsS -m 20 "${APP_URL}/health" >/dev/null 2>&1; then
    printf '\n\033[1;32m✅ Desplegada y respondiendo\033[0m\n'
    printf '   Aplicación : %s\n' "${APP_URL}"
    printf '   API        : %s/api/translations\n' "${APP_URL}"
    printf '   Imagen     : %s\n\n' "${IMAGE}"
    exit 0
  fi
  printf '.'
  sleep 10
done

printf '\n\033[1;31m❌ La aplicación no respondió a tiempo.\033[0m Revisa los registros:\n'
printf '   az containerapp logs show -n translation-service-app -g %s --follow\n\n' "${RESOURCE_GROUP}"
exit 1
