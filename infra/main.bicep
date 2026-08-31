targetScope = 'resourceGroup'

@description('Región de despliegue. Por defecto, la del grupo de recursos.')
param location string = resourceGroup().location

@description('Prefijo para los nombres de los recursos.')
@minLength(3)
@maxLength(20)
param appName string = 'translation-service'

@description('Imagen del contenedor, con etiqueta. Ej: ghcr.io/usuario/translation-service:v1')
param containerImage string

@description('Clave de suscripción de Azure Translator. Se almacena como secreto de la Container App.')
@secure()
param azureTranslatorApiKey string

@description('Región de la suscripción de Azure Translator (ej. westeurope).')
param azureTranslatorRegion string

@description('Idioma destino de las traducciones.')
param targetLanguage string = 'es'

@description('''
Réplicas mínimas. Cero permite escalar a cero cuando no hay tráfico, que es lo que mantiene
el consumo dentro de la bolsa gratuita mensual de Container Apps. El precio es un arranque
en frío en la primera petición tras un periodo de inactividad.
''')
@minValue(0)
@maxValue(1)
param minReplicas int = 0

var resourceToken = uniqueString(resourceGroup().id)

// ─────────────────────────────────────────────────────────────────────────────
// Observabilidad. El SKU PerGB2018 incluye una franja mensual de ingesta sin coste,
// suficiente de sobra para los logs de una única réplica.
// ─────────────────────────────────────────────────────────────────────────────
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${resourceToken}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${appName}-app'
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        // El ingress termina TLS y redirige HTTP a HTTPS en el borde. La aplicación honra
        // X-Forwarded-Proto para no entrar en bucle de redirección.
        allowInsecure: false
      }
      secrets: [
        {
          // La clave nunca viaja como variable de entorno en claro en la plantilla:
          // se guarda como secreto de la Container App y se referencia por nombre.
          name: 'azure-translator-api-key'
          value: azureTranslatorApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: {
            // El tamaño mínimo soportado. Traducir es E/S pura: el cuello de botella es la
            // latencia de Azure Translator, no la CPU.
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'AzureTranslator__ApiKey', secretRef: 'azure-translator-api-key' }
            { name: 'AzureTranslator__Region', value: azureTranslatorRegion }
            { name: 'Translation__TargetLanguage', value: targetLanguage }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas

        // ⚠️ RESTRICCIÓN DE CORRECCIÓN, NO DE COSTE ⚠️
        //
        // El repositorio de trabajos es un ConcurrentDictionary y la cola un
        // System.Threading.Channel: ambos viven en la memoria del proceso. Con dos o más
        // réplicas, un POST atendido por la réplica A guarda el trabajo sólo en A, y un GET
        // posterior balanceado hacia B devolvería 404 de forma intermitente.
        //
        // Subir este valor exige antes sustituir InMemoryTranslationJobRepository por un
        // almacén compartido (Redis, Cosmos DB, SQL) y ChannelMessageQueue por un broker
        // real (Service Bus). Ambos están tras un puerto, así que es un cambio localizado
        // en la capa Infrastructure, pero no puede hacerse subiendo este número.
        maxReplicas: 1

        rules: [
          {
            name: 'http-scaling'
            http: { metadata: { concurrentRequests: '100' } }
          }
        ]
      }
    }
  }
}

output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output containerAppName string = containerApp.name
