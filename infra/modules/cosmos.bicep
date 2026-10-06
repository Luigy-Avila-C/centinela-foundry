// Azure Cosmos DB: persistencia de los casos de cumplimiento (estado, borradores, auditorías y quién aprobó qué).

param location string
param environment string
param principalId string

@description('Nivel gratuito: los primeros 1000 RU/s y 25 GB no cuestan nada. Solo una cuenta por suscripción: si ya hay otra, el despliegue FALLA en lugar de cobrar.')
param enableFreeTier bool = true

var accountName = 'cosmos-centinela-${environment}-${take(uniqueString(resourceGroup().id), 6)}'

// Rol integrado «Cosmos DB Built-in Data Contributor»: leer y escribir documentos (no administrar la cuenta).
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: enableFreeTier
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    // Sin claves de cuenta: solo Microsoft Entra ID, igual que Foundry y Search.
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: 'Tls12'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: account
  name: 'centinela'
  properties: {
    resource: { id: 'centinela' }
    // Rendimiento compartido y mínimo (400 RU/s, dentro del nivel gratuito).
    options: { throughput: 400 }
  }
}

resource cases 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'cases'
  properties: {
    resource: {
      id: 'cases'
      partitionKey: { paths: ['/id'], kind: 'Hash', version: 2 }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [{ path: '/*' }]
        // El snapshot es grande y solo se lee entero por id: indexarlo gastaría RU sin servir para nada.
        excludedPaths: [{ path: '/snapshot/*' }, { path: '/"_etag"/?' }]
      }
    }
  }
}

resource dataAccess 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: account
  name: guid(account.id, principalId, dataContributorRoleId)
  properties: {
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    principalId: principalId
    scope: account.id
  }
}

output cosmosEndpoint string = account.properties.documentEndpoint
