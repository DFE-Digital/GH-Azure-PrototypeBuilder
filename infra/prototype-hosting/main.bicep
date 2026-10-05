@description('Azure region for the prototype hosting resources.')
param location string

@description('Name of the Linux App Service plan.')
param appServicePlanName string

@description('Name of the prototype web app.')
param webAppName string

@description('Node.js runtime for the hosted prototype.')
param nodeRuntime string = 'NODE|24-lts'

@secure()
@description('Prototype password exposed to the app as an app setting for the MVP.')
param prototypePassword string

@description('Required resource tags.')
param tags object

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
    capacity: 1
  }
  properties: {
    reserved: true
  }
  tags: tags
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: nodeRuntime
      alwaysOn: false
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'NODE_ENV'
          value: 'production'
        }
        {
          name: 'PASSWORD'
          value: prototypePassword
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'true'
        }
        {
          name: 'WEBSITE_WEBDEPLOY_USE_SCM'
          value: 'true'
        }
      ]
    }
  }
}

output webAppName string = webApp.name
output defaultHostname string = webApp.properties.defaultHostName
