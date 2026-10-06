// Azure AI Search: índice vectorial de la normativa que consultan los agentes (RAG).

param location string
param environment string
param principalId string

@description('SKU del servicio. "free" no cuesta nada (50 MB, 3 índices) y basta para esta fase; solo se permite uno por suscripción.')
@allowed(['free', 'basic'])
param sku string = 'free'

var searchName = 'srch-centinela-${environment}-${take(uniqueString(resourceGroup().id), 6)}'

// Dos roles separados: crear/cambiar el esquema del índice, y leer/escribir sus documentos.
var serviceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
var indexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'

resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchName
  location: location
  sku: { name: sku }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    // Sin claves de administrador: solo Microsoft Entra ID, igual que en Foundry.
    disableLocalAuth: true
    publicNetworkAccess: 'enabled'
  }
}

resource schemaAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: search
  name: guid(search.id, principalId, serviceContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceContributorRoleId)
    principalId: principalId
    principalType: 'User'
  }
}

resource dataAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: search
  name: guid(search.id, principalId, indexDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', indexDataContributorRoleId)
    principalId: principalId
    principalType: 'User'
  }
}

output searchEndpoint string = 'https://${searchName}.search.windows.net'
