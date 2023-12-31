targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the the environment which is used to generate a short unique hash used in all resources.')
param environmentName string


// Variables
var resourceToken = toLower(uniqueString(subscription().id, environmentName))
var tags = {
  // Add your desired tags here
}

param resourceGroupName string = ''
var openAiServiceName = ''
var openAiSkuName = 'S0' 
var chatGptDeploymentName = 'gpt-4'
var chatGptDeploymentName32k = 'gpt-4-32k'
@minLength(1)
@description('Primary location for all resources')
param location string
var chatGptModelName = 'gpt-4'
var chatGptModelName32k = 'gpt-4-32k'
var chatGptDeploymentCapacity = 10 
var chatGptDeployment32kCapacity = 80
var embeddingDeploymentName = 'text-embedding-ada-002'
var embeddingDeploymentCapacity = 30
param embeddingModelName string = 'text-embedding-ada-002'
param openAiResourceGroupName string = ''
var searchServiceName = ''
var searchServiceSkuName = 'standard' // Change to your desired SKU

param storageAccountName string = ''

var abbrs = loadJsonContent('abbreviations.json')
resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: !empty(resourceGroupName) ? resourceGroupName : '${abbrs.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

resource openAiResourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' existing = if (!empty(openAiResourceGroupName)) {
  name: !empty(openAiResourceGroupName) ? openAiResourceGroupName : resourceGroup.name
}

// OpenAI Deployment
module openAi 'core/ai/cognitiveservices.bicep' = {
  name: 'openai'
  scope:  openAiResourceGroup
  params: {
    name: !empty(openAiServiceName) ? openAiServiceName : '${abbrs.cognitiveServicesAccounts}${resourceToken}'
    location: location
    tags: tags
    sku: {
      name: openAiSkuName
    }
    deployments: [
      {
        name: chatGptDeploymentName32k
        model: {
          format: 'OpenAI'
          name: chatGptModelName32k
          version: '0613' 
        }
        capacity: chatGptDeployment32kCapacity
      }
      {
        name: chatGptDeploymentName
        model: {
          format: 'OpenAI'
          name: chatGptModelName
          version: 'vision-preview' 
        }
        capacity: chatGptDeploymentCapacity
      }
      {
        name: embeddingDeploymentName
        model: {
          format: 'OpenAI'
          name: embeddingModelName
          version: '2'
        }
        capacity: embeddingDeploymentCapacity
      }
    ]
  }
}

// Cognitive Search Deployment
module searchService 'core/search/search-services.bicep' = {
  name: 'search-service'
  scope: resourceGroup 
  params: {
    name: !empty(searchServiceName) ? searchServiceName : 'gptkb-${resourceToken}'
    location: location
    tags: tags
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    sku: {
      name: searchServiceSkuName
    }
    semanticSearch: 'free'
  }
}


module storage 'core/storage/storage-account.bicep' = {
  name: 'storage'
  scope: resourceGroup
  params: {
    name: !empty(storageAccountName) ? storageAccountName : 'storgcc${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    sku: {
      name: 'Standard_ZRS'
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 2
    }
    containers: [
      {
        name: 'images'
      }
    ]
  }
}

module appServicePlan './core/host/appserviceplan.bicep' = {
  name: 'appserviceplan'
  scope: resourceGroup
  params: {
    name: '${abbrs.appBackend}${resourceToken}'
    location: location
    tags: tags
    sku: {
      name: 'B1'
    }
  }

}

module keyvault 'core/security/keyvault.bicep' = {
  name: 'keyvault-deployment'
  scope: resourceGroup
  params: {
    name: 'kv-${resourceToken}'
    location: location
  }
}

module openAiApiKeySecret 'core/security/keyvault-secret.bicep' = {
  name: 'openai-api-key-secret'
  scope: resourceGroup
  params: {
    name: 'openai-api-key'
    keyVaultName: keyvault.outputs.name
    secretValue: openAi.outputs.primaryKey
  }
}

module acsKey 'core/security/keyvault-secret.bicep' = {
  name: 'acs-key'
  scope: resourceGroup
  params: {
    name: 'acs-key'
    keyVaultName: keyvault.outputs.name
    secretValue: searchService.outputs.primaryKey
  }
}

module connectionString 'core/security/keyvault-secret.bicep' = {
  name: 'connection-string'
  scope: resourceGroup
  params: {
    name: 'connection-string'
    keyVaultName: keyvault.outputs.name
    secretValue: storage.outputs.connectionString
  }
}

// Application Backend Deployment
module appBackendDeployment './app/api.bicep' = {
  name: 'appbackend-deployment'
  scope: resourceGroup
  params: {
    name: '${abbrs.appBackend}${resourceToken}'
    location: location 
    appServicePlanId: appServicePlan.outputs.id
    allowedOrigins: [
      appFrontendDeployment.outputs.SERVICE_WEB_URI
    ]
    appSettings: {
      AZURE_STORAGE_CONNECTION_STRING: '@Microsoft.KeyVault(SecretUri=${connectionString.outputs.secretUri})'
      AZURE_OPENAI_DEPLOYMENT_NAME: chatGptDeploymentName32k
      AZURE_OPENAI_ENDPOINT: openAi.outputs.endpoint
      AZURE_OPENAI_API_KEY: '@Microsoft.KeyVault(SecretUri=${openAiApiKeySecret.outputs.secretUri})'
      ACS_INSTANCE: searchService.outputs.name
      ACS_KEY: '@Microsoft.KeyVault(SecretUri=${acsKey.outputs.secretUri})'
    }
  }
}

// Application Frontend Deployment
module appFrontendDeployment './app/web.bicep' = {
  name: 'appfrontend-deployment'
  scope: resourceGroup
  params: {
    name: '${abbrs.appFrontend}${resourceToken}'
    appServicePlanId: appServicePlan.outputs.id
    location: location
  }
}

module frontendSettings './core/host/appservice-appsettings.bicep' = {
  name: 'frontend-appsettings'
  scope: resourceGroup
  params: {
    name: appFrontendDeployment.outputs.SERVICE_WEB_NAME
    appSettings: {
      UI_APP_API_BASE_URL: appBackendDeployment.outputs.SERVICE_API_URI
    }
  }
}

module keyvaultAccess './core/security/keyvault-access.bicep' = {
  name: 'keyvault-access-policy'
  scope: resourceGroup
  params: {
    keyVaultName: keyvault.outputs.name
    principalId: appBackendDeployment.outputs.SERVICE_API_IDENTITY_PRINCIPAL_ID
  }
}

// Data outputs
output AZURE_OPENAI_DEPLOYMENT_NAME string = chatGptDeploymentName
output AZURE_OPENAI_ENDPOINT string = openAi.outputs.endpoint
output AZURE_OPENAI_API_KEY string = openAi.outputs.primaryKey
output ACS_INSTANCE string = searchService.outputs.name
output ACS_KEY string = searchService.outputs.primaryKey

// App outputs
output APP_BACKEND_NAME string = appBackendDeployment.outputs.SERVICE_API_NAME
output APP_BACKEND_URL string = appBackendDeployment.outputs.SERVICE_API_URI
output APP_FRONTEND_NAME string = appFrontendDeployment.outputs.SERVICE_WEB_NAME
output APP_FRONTEND_URL string = appFrontendDeployment.outputs.SERVICE_WEB_URI

output AZURE_STORAGE_ACCOUNT string = storage.outputs.name
output AZURE_STORAGE_ACCOUNT_CONNECTION_STRING string = storage.outputs.connectionString 
