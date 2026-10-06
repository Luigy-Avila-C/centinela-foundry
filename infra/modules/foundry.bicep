// Recurso de Azure AI Foundry + proyecto + despliegues de modelos.

param location string
param environment string
param principalId string

// El subdominio personalizado debe ser único en todo Azure: se deriva del grupo de recursos.
var accountName = 'foundry-centinela-${environment}-${take(uniqueString(resourceGroup().id), 6)}'
var projectName = 'centinela'

// Modelos por agente: uno barato para cribar y vigilar la seguridad, uno potente para
// razonar y redactar, y embeddings para el RAG. La capacidad es un tope (miles de tokens
// por minuto), no un gasto: se paga por token consumido.
var deployments = [
  { name: 'gpt-4.1-mini', model: 'gpt-4.1-mini', version: '2025-04-14', capacity: 50 }
  // 150: el análisis por artículo y las evaluaciones hacen cientos de llamadas en paralelo; con 50 se
  // saturaba el límite de tokens por minuto. Es un techo de velocidad, no un coste.
  { name: 'gpt-4.1', model: 'gpt-4.1', version: '2025-04-14', capacity: 150 }
  // Juez del verificador de citas. Es deliberadamente de otra generación que el analista (gpt-4.1):
  // un modelo tiende a aprobar su propio trabajo, así que el juez no debe ser el mismo.
  { name: 'gpt-5.1', model: 'gpt-5.1', version: '2025-11-13', capacity: 150 }
  { name: 'text-embedding-3-small', model: 'text-embedding-3-small', version: '1', capacity: 50 }
]

// Rol "Foundry User": permite usar agentes y modelos del proyecto sin poder administrarlo.
var foundryUserRoleId = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: accountName
    allowProjectManagement: true
    publicNetworkAccess: 'Enabled'
    // Sin claves de API: solo se entra con Microsoft Entra ID, así no hay secretos que filtrar.
    disableLocalAuth: true
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Centinela'
    description: 'Agentes de cumplimiento normativo'
  }
}

// Los despliegues de un mismo recurso no admiten concurrencia: se crean de uno en uno.
@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = [for d in deployments: {
  parent: account
  name: d.name
  sku: { name: 'GlobalStandard', capacity: d.capacity }
  properties: {
    model: { format: 'OpenAI', name: d.model, version: d.version }
    // Se fijan los valores que hoy da Azure por defecto, para que no dependan de él.
    raiPolicyName: 'Microsoft.DefaultV2'
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
  dependsOn: [project]
}]

resource userAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, principalId, foundryUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', foundryUserRoleId)
    principalId: principalId
    principalType: 'User'
  }
}

output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'
