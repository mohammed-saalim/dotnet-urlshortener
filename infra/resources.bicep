// Everything in the resource group. Decisions and their reasons: docs/adr/0006-azure-hosting-and-migrations.md.
param environmentName string
param location string
param tags object
param alertEmail string
param monthlyBudget int
param apiExists bool
param budgetStartDate string

var resourceToken = uniqueString(subscription().id, resourceGroup().id, environmentName)
var appName = 'ca-api-${resourceToken}'
var databaseName = 'urlshortener'
// Built-in role "AcrPull".
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

// One user-assigned identity: it pulls the image from the registry, and it is the SQL server's Entra administrator,
// so the app can connect and run migrations with no password and no manual CREATE USER step (ADR-0006).
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

// Required by the Container Apps environment for console and system logs. Capped: 30 days, 0.5 GB a day.
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.5') }
  }
}

// Operational telemetry (issue #28): workspace-based, so its data lands in the workspace above and the workspace's
// 0.5 GB/day cap applies to it too (the effective cap is the lower of the component's and the workspace's; no component
// cap is set). Client IPs are masked by default (DisableIpMasking is not set).
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
  }
}

// Support workbook (issue #44): four panels over the telemetry above; content in workbook.json (loadJsonContent fails
// the build on malformed JSON). The name must be a GUID: a deterministic one makes a re-provision update this workbook
// instead of adding a copy. The component id is injected as fallbackResourceIds, so workbook.json holds no ids.
resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, 'urlshortener-workbook')
  location: location
  tags: tags
  kind: 'shared'
  properties: {
    category: 'workbook'
    displayName: 'URL shortener support'
    sourceId: appInsights.id
    version: 'Notebook/1.0'
    serializedData: string(union(loadJsonContent('workbook.json'), { fallbackResourceIds: [appInsights.id] }))
  }
}

// Basic: the cheapest tier. No admin user; the app pulls with its managed identity (no registry password anywhere).
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        // Read at deployment time inside ARM; never in the repo, the parameters or azd's environment.
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// The address is known before the app exists, so configuration (base URL, AllowedHosts, probe Host) can use it.
var apiFqdn = '${appName}.${environment.properties.defaultDomain}'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${resourceToken}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    // Container Apps consumption has no fixed outbound IP, so access is public + "Allow Azure services" (below).
    publicNetworkAccess: 'Enabled'
    // Entra-only: SQL authentication is disabled, so there is no SQL admin password at all.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: identity.name
      sid: identity.properties.principalId
      tenantId: tenant().tenantId
    }
  }
}

// The special 0.0.0.0 rule ("Allow Azure services and resources to access this server"). It admits any Azure-hosted
// client, not only this subscription; Entra-only authentication is what keeps them out.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Free offer: 100,000 vCore-seconds and 32 GB a month. AutoPause is the hard cost stop: when the allowance is used up
// the database pauses until next month instead of billing. Smallest serverless size (0.5-1 vCores) makes the allowance
// last longest. No autoPauseDelay: with AutoPause on exhaustion, ARM accepts only the default (60 minutes, #31).
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB, the free-offer data limit
    requestedBackupStorageRedundancy: 'Local'
  }
}

// On the first provision there is no app image yet, so a public placeholder runs until `azd deploy` replaces it.
// On later provisions, keep the image azd last deployed instead of resetting it to the placeholder.
module apiImage 'api-image.bicep' = {
  name: 'api-image'
  params: {
    name: appName
    exists: apiExists
    placeholder: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
  }
}

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: appName
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull]
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        { server: registry.properties.loginServer, identity: identity.id }
      ]
      // The connection string is documented as not secret, but anyone holding it can send telemetry into the
      // resource; as a secret it stays out of the portal's environment-variable view.
      secrets: [
        { name: 'appinsights-connection-string', value: appInsights.properties.ConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage.outputs.image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'PublicUrl__BaseUrl', value: 'https://${apiFqdn}' }
            // Safe only because the ingress is the sole way in; it appends exactly one X-Forwarded-For entry.
            { name: 'PublicUrl__TrustForwardedHeaders', value: 'true' }
            { name: 'AllowedHosts', value: apiFqdn }
            // Read by the app (opt-in exporter) and the distro; unset everywhere else, so nothing else exports.
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
            // Distro 1.6.0 turns OpenTelemetry's url.query redaction off unless this is already set; false keeps the
            // values of any query string a client appends to a short link out of the request telemetry.
            { name: 'OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION', value: 'false' }
            // Keep every trace (the distro default keeps 5 per second), so a traceId a client quotes from an error
            // response can always be found (#33). Revisit if ingestion approaches the workspace's daily cap.
            { name: 'OTEL_TRACES_SAMPLER', value: 'microsoft.fixed_percentage' }
            { name: 'OTEL_TRACES_SAMPLER_ARG', value: '1.0' }
            // Single replica (maxReplicas below), so migrating at startup does not race another instance.
            { name: 'Database__MigrateOnStartup', value: 'true' }
            {
              // Passwordless. User Id is the identity's client id (not a secret): without it the credential looks
              // for a system-assigned identity, which this app does not have.
              name: 'ConnectionStrings__UrlShortener'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};Authentication=Active Directory Default;User Id=${identity.properties.clientId};Encrypt=True;'
            }
          ]
          probes: [
            {
              // Liveness only, on the endpoint that never opens a database connection. A readiness or startup probe
              // on /health/ready would keep the serverless database awake and burn the free allowance. The Host
              // header is the public name, because host filtering (AllowedHosts) answers 400 to any other host.
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                httpHeaders: [
                  { name: 'Host', value: apiFqdn }
                ]
              }
            }
          ]
        }
      ]
      scale: {
        // Scale to zero when idle (the first request after idle pays a cold start). One replica at most: the rate
        // limiter is in memory and the startup migration assumes a single instance.
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// Cost guardrails: email when the free allowance falls below 20 % (20,000 of 100,000 vCore-seconds), and a monthly
// budget for the whole resource group. Both only notify; the free offer's AutoPause is the hard stop.
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${resourceToken}'
  location: 'Global'
  tags: tags
  properties: {
    groupShortName: 'urlcost'
    enabled: true
    emailReceivers: [
      { name: 'engineer', emailAddress: alertEmail, useCommonAlertSchema: true }
    ]
  }
}

resource freeAmountAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-sql-free-amount-${resourceToken}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Azure SQL free offer: less than 20 % of the monthly vCore-seconds left.'
    severity: 2
    enabled: true
    scopes: [database.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'FreeAmountBelow20Percent'
          metricNamespace: 'Microsoft.Sql/servers/databases'
          metricName: 'free_amount_remaining'
          operator: 'LessThan'
          threshold: 20000
          timeAggregation: 'Minimum'
        }
      ]
    }
    actions: [
      { actionGroupId: actionGroup.id }
    ]
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-${environmentName}'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [alertEmail]
      }
    }
  }
}

output registryLoginServer string = registry.properties.loginServer
output apiBaseUrl string = 'https://${apiFqdn}'
