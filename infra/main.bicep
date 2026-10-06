// Despliegue de Centinela a nivel de suscripción: crea el grupo de recursos y,
// dentro de él, el recurso de Azure AI Foundry con su proyecto y los modelos.
targetScope = 'subscription'

@description('Región de Azure. Spain Central mantiene los datos en España.')
param location string = 'spaincentral'

@description('Entorno lógico (dev, pro...). Forma parte de los nombres.')
param environment string = 'dev'

@description('Object ID de la persona que usará los agentes desde el código (az ad signed-in-user show).')
param principalId string

@description('SKU de Azure AI Search. «free» no cuesta nada pero Azure solo permite UNO por suscripción; si ya lo usaste, elige «basic» (de pago, con cuota fija mensual).')
@allowed(['free', 'basic'])
param searchSku string = 'free'

@description('Usa el nivel gratuito de Cosmos DB (1000 RU/s y 25 GB gratis). Solo se permite UNA cuenta gratuita por suscripción; si ya la usaste, ponlo a false (pasa a pago por RU/s aprovisionados).')
param cosmosFreeTier bool = true

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-centinela-${environment}'
  location: location
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  scope: rg
  params: {
    location: location
    environment: environment
    principalId: principalId
  }
}

module search 'modules/search.bicep' = {
  name: 'search'
  scope: rg
  params: {
    location: location
    environment: environment
    principalId: principalId
    sku: searchSku
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: {
    location: location
    environment: environment
    principalId: principalId
    enableFreeTier: cosmosFreeTier
  }
}

@description('Endpoint de Cosmos DB, donde se guardan los casos.')
output cosmosEndpoint string = cosmos.outputs.cosmosEndpoint

@description('Endpoint del proyecto de Foundry; es lo que consume el código .NET.')
output projectEndpoint string = foundry.outputs.projectEndpoint

@description('Endpoint del servicio de búsqueda donde se indexa la normativa.')
output searchEndpoint string = search.outputs.searchEndpoint

output resourceGroupName string = rg.name
