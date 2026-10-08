// Mitsuke on Azure: the 24/7 pipeline (Functions) and the web app's API (App Service), rebuildable from this file.
//
//   az deployment group what-if -g mitsuke-rg -f infra/main.bicep -p infra/main.bicepparam
//   az deployment group create  -g mitsuke-rg -f infra/main.bicep -p infra/main.bicepparam
//
// No secrets live here. Both apps reach storage, the queues and Key Vault with a managed identity; secret values
// are put in Key Vault once by scripts/push-secrets.sh. Postgres and sign-in are Supabase; the web app is Vercel.
//
// Cost guardrails: the API is on App Service's free F1 tier, the pipeline on Flex Consumption (pay per execution,
// within the monthly free grant at this volume) with an instance cap, logs have a daily cap, and the subscription
// has a budget alert. Expected cost: cents per month.

targetScope = 'resourceGroup'

param location string = resourceGroup().location

@minLength(3)
@maxLength(11)
param prefix string = 'mitsuke'

@description('Supabase project URL, e.g. https://<ref>.supabase.co (not a secret): sign-in tokens are checked against it.')
param supabaseUrl string

@description('The web app address (Vercel). Alerts link to its lot pages.')
param webUrl string

@description('Kensa-ya, for sheet decoding through its partner API (not a secret).')
param kensayaUrl string = 'https://kensa-ya.vercel.app'

@description('How often the pipeline checks the auctions (NCRONTAB).')
param collectSchedule string = '0 */10 * * * *'

@description('Who gets bid requests by email (the person forwarding them to the exporter).')
param bidRequestsTo string

@description('Sender address for alert emails (the Gmail account the app password belongs to).')
param smtpUser string

@description('Web Push VAPID public key (not a secret; browsers get it too). Empty turns phone notifications off. The private key is the Key Vault secret vapid-private-key.')
param vapidPublicKey string = ''

@description('Upper limit on pipeline instances. The main protection against a runaway bill.')
@minValue(40)
param maxInstances int = 40

@description('Daily cap on log ingestion in GB.')
param logDailyCapGb string = '0.1'

@description('Object id of the person who manages secrets (az ad signed-in-user show --query id). Empty skips it.')
param adminPrincipalId string = ''

var suffix = uniqueString(resourceGroup().id)
var tags = { app: 'mitsuke' }

var roles = {
  blobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  queueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
}

// ------------------------------------------------------------------ identity (shared by the pipeline and the API)

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-id'
  location: location
  tags: tags
}

// ------------------------------------------------------------------ storage: queues, host state, deployment packages

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${prefix}${suffix}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // identity only: no account keys or connection strings anywhere
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deployments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'deployments'
  properties: { publicAccess: 'None' }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// The two pipeline queues and their dead-letter queues (the Functions host moves a message to *-poison after 5 tries).
resource queues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [for name in [ 'collect', 'collect-poison', 'alerts', 'alerts-poison' ]: {
  parent: queueService
  name: name
}]

// ------------------------------------------------------------------ monitoring

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json(logDailyCapGb) }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// The ops dashboard (Monitor → Workbooks → "Mitsuke · pipeline health"). Its queries are generated and checked
// against live data by scripts/workbook.py; workbooks are free.
resource dashboard 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, 'mitsuke-pipeline-health')
  location: location
  tags: union(tags, { 'hidden-title': 'Mitsuke · pipeline health' })
  kind: 'shared'
  properties: {
    displayName: 'Mitsuke · pipeline health'
    category: 'workbook'
    sourceId: logs.id
    serializedData: replace(loadTextContent('workbook.json'), '__WORKSPACE_ID__', logs.id)
  }
}

// ------------------------------------------------------------------ secrets

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${prefix}-kv-${suffix}', 24)
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Key Vault references: the platform resolves them with the identity; the values never appear in app settings.
func kv(vaultName string, secret string) string => '@Microsoft.KeyVault(VaultName=${vaultName};SecretName=${secret})'

var shared = [
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
  { name: 'MITSUKE_DB', value: kv(vault.name, 'mitsuke-db') }
  { name: 'SUPABASE_URL', value: supabaseUrl }
  { name: 'SMTP_HOST', value: 'smtp.gmail.com' }
  { name: 'SMTP_USER', value: smtpUser }
  { name: 'SMTP_PASSWORD', value: kv(vault.name, 'smtp-password') }
  { name: 'BID_REQUESTS_TO', value: bidRequestsTo }
  { name: 'MITSUKE_WEB_URL', value: webUrl }
]

// Phone/desktop notifications: the pipeline sends them, the API sends the "test this device" one.
var push = empty(vapidPublicKey) ? [] : [
  { name: 'VAPID_PUBLIC_KEY', value: vapidPublicKey }
  { name: 'VAPID_PRIVATE_KEY', value: kv(vault.name, 'vapid-private-key') }
  { name: 'VAPID_SUBJECT', value: 'mailto:${smtpUser}' }
]

// ------------------------------------------------------------------ the pipeline (Azure Functions, Flex Consumption)

resource functionsPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-functions-plan'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource functions 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-pipeline-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    serverFarmId: functionsPlan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identity.id
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deployments.name}'
          authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: identity.id }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: maxInstances, instanceMemoryMB: 2048 }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: concat(shared, [
        // Host state and the queue triggers, via the managed identity.
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.properties.clientId }
        { name: 'CollectSchedule', value: collectSchedule }
        { name: 'THECARAPI_KEY', value: kv(vault.name, 'thecarapi-key') }
        { name: 'KENSAYA_BASE_URL', value: kensayaUrl }
        { name: 'KENSAYA_PARTNER_KEY', value: kv(vault.name, 'kensaya-partner-key') }
      ], push)
    }
  }
  dependsOn: [ blobOwner, queueContributor, secretsUser ]
}

// ------------------------------------------------------------------ the API (App Service, free F1)

resource apiPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-api-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: 'F1', tier: 'Free' }
  properties: { reserved: true }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-api-${suffix}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    serverFarmId: apiPlan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/api/health'
      appSettings: concat(shared, [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      ], push)
    }
  }
  dependsOn: [ secretsUser ]
}

// ------------------------------------------------------------------ access for the shared identity

resource blobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roles.blobDataOwner)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataOwner)
  }
}

resource queueContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roles.queueDataContributor)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.queueDataContributor)
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, identity.id, roles.keyVaultSecretsUser)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

// The owner can set secret values (scripts/push-secrets.sh); the apps can only read them.
resource adminSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(adminPrincipalId)) {
  scope: vault
  name: guid(vault.id, adminPrincipalId, roles.keyVaultSecretsOfficer)
  properties: {
    principalId: adminPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
  }
}

output functionsName string = functions.name
output apiName string = api.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output keyVaultName string = vault.name
