# azure-appservice

Pulumi component that deploys a Linux Azure App Service (web app) with its App Service plan and, optionally, its resource group.

Secure defaults: HTTPS only, TLS 1.2+, FTPS disabled, HTTP/2 enabled and a system-assigned managed identity.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `resourceGroupName` | string | created | Existing resource group. Requires `location` when set. |
| `location` | string | provider location | Azure region. |
| `skuName` | string | `B1` | App Service plan SKU name. |
| `skuTier` | string | `Basic` | App Service plan SKU tier. |
| `linuxFxVersion` | string | `DOTNETCORE\|10.0` | Linux runtime stack. |
| `alwaysOn` | bool | `true` | Keep the app loaded. |
| `appSettings` | map of string | none | Application settings. |
| `tags` | map of string | none | Tags for all resources. |

## Outputs

`resourceGroupName`, `appName`, `defaultHostName`, `url`, `principalId`.

## Example (C#)

```csharp
var app = new AppService("web", new AppServiceArgs
{
    SkuName = "S1",
    SkuTier = "Standard",
    AppSettings = new InputMap<string> { { "ENVIRONMENT", "dev" } },
});
```
