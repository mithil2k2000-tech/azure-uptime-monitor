// Azure environment for the uptime monitor, as code.
//
//   Function app (Flex Consumption, .NET isolated) with a managed identity
//     -> Storage account: the Functions host's own state, the deployment
//        package, and the results table. No storage key exists: shared-key
//        access is switched off and the app reaches storage as itself.
//     -> Application Insights + Log Analytics: logs and traces
//     -> An alert rule that emails you when any endpoint is down
//
//   az deployment group create -g <rg> -f infra/main.bicep -p alertEmail=you@example.com

targetScope = 'resourceGroup'

@description('Region for every resource.')
param location string = resourceGroup().location

@description('Short prefix for resource names.')
@maxLength(12)
param namePrefix string = 'uptime'

@description('Where "endpoint down" alerts are sent.')
param alertEmail string

@description('Sites to watch: [{ name, url }]. Names may not contain / \\ # or ?.')
param endpoints array = [
  { name: 'github-status', url: 'https://www.githubstatus.com' }
  { name: 'portfolio', url: 'https://mithil2k2000-tech.github.io/portfolio/' }
]

@description('NCRONTAB, six fields. Every five minutes by default.')
param schedule string = '0 */5 * * * *'

@description('.NET version of the Functions runtime. Must match the project\'s target framework and be one Flex Consumption supports in this region.')
param dotnetVersion string = '10.0'

var suffix = uniqueString(resourceGroup().id)
var storageName = toLower('${take(replace(namePrefix, '-', ''), 11)}${take(suffix, 13)}')
var appName = '${namePrefix}-func-${suffix}'
var deploymentContainer = 'deployments'

// Each endpoint becomes two settings the app binds as Monitor:Endpoints.
var endpointSettings = flatten(map(range(0, length(endpoints)), i => [
  { name: 'Monitor__Endpoints__${i}__Name', value: endpoints[i].name }
  { name: 'Monitor__Endpoints__${i}__Url', value: endpoints[i].url }
]))

// ---- Storage ----------------------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // identity only: there is no key to leak
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deployments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainer
}

// ---- Monitoring -------------------------------------------------------

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

// ---- The function app -------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan-${suffix}'
  location: location
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true // Linux
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: concat([
        // The Functions host's own storage, reached with the identity.
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        // The results table, also through the identity.
        { name: 'Storage__TableEndpoint', value: storage.properties.primaryEndpoints.table }
        { name: 'Monitor__TableName', value: 'uptimeresults' }
        { name: 'MONITOR_SCHEDULE', value: schedule }
      ], endpointSettings)
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer}'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: dotnetVersion
      }
    }
  }
  dependsOn: [deployments]
}

// What the app's identity may do with the storage account, and nothing
// beyond it: blobs (host state, deployment package), queues (the host
// uses them internally), tables (results).
var storageRoles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
]

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in storageRoles: {
    name: guid(storage.id, app.id, role)
    scope: storage
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: app.identity.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

// ---- Alerting ---------------------------------------------------------

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${namePrefix}-alerts-${suffix}'
  location: 'global'
  properties: {
    groupShortName: 'uptime'
    enabled: true
    emailReceivers: [
      { name: 'owner', emailAddress: alertEmail, useCommonAlertSchema: true }
    ]
  }
}

// The function logs a warning that starts "Endpoint down" for every
// failed check. One in the last ten minutes fires this.
resource downAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-endpoint-down-${suffix}'
  location: location
  properties: {
    displayName: 'An endpoint is down'
    description: 'An uptime check failed. The alert lists which endpoint and why.'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [appInsights.id]
    criteria: {
      allOf: [
        {
          query: 'traces | where message startswith "Endpoint down" | project timestamp, message'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

output functionAppName string = app.name
output statusUrl string = 'https://${app.properties.defaultHostName}/api/status'
