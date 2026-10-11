// Entry point for azd (subscription scope): one resource group, everything else in resources.bicep.
// Decisions: docs/adr/0006-azure-hosting-and-migrations.md.
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('azd environment name; names the resource group and seeds resource names.')
param environmentName string

@minLength(1)
@description('Azure region for every resource.')
param location string

// Deliberately not in main.parameters.json and without a default, so azd prompts for it and stores it only in the
// local, gitignored .azure/<env>/ folder. It never enters the repo.
@description('Email address that receives the cost alerts (Azure SQL free amount, monthly budget).')
param alertEmail string

@description('Monthly budget for the resource group, in the billing currency (USD assumed). Notifies at 80 % of actual cost.')
param monthlyBudget int = 10

@description('Set by azd (SERVICE_API_RESOURCE_EXISTS): true once the container app exists, so provisioning keeps the deployed image.')
param apiExists bool = false

// utcNow is only allowed as a parameter default. A budget's start date must be the first day of a month.
@description('Budget start date; defaults to the first day of the current month.')
param budgetStartDate string = utcNow('yyyy-MM-01')

var tags = { 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    alertEmail: alertEmail
    monthlyBudget: monthlyBudget
    apiExists: apiExists
    budgetStartDate: budgetStartDate
  }
}

// azd reads these into the environment: the registry to push to, and the app's address.
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.registryLoginServer
output API_BASE_URL string = resources.outputs.apiBaseUrl
