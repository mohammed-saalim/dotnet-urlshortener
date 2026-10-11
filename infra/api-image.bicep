// Returns the image the container app runs now, or the placeholder when the app does not exist yet. A module, because
// an `existing` reference to the app inside the template that also declares it is a circular dependency in ARM (#31).
param name string
param exists bool
param placeholder string

resource existingApi 'Microsoft.App/containerApps@2025-01-01' existing = if (exists) {
  name: name
}

output image string = exists ? existingApi!.properties.template.containers[0].image : placeholder
