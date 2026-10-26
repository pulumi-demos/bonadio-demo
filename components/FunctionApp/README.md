# bonadio-functionapp

Pulumi component that deploys a Linux Azure Function App with a backing storage account, hosting plan and (optionally) Application Insights, plus the resource group if one is not supplied.

Secure defaults: HTTPS only, TLS 1.2+, FTPS disabled, no public blob access and a system-assigned managed identity.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `resourceGroupName` | string | created | Existing resource group. Requires `location` when set. |
| `location` | string | provider location | Azure region. |
| `workerRuntime` | string | `dotnet-isolated` | Functions worker runtime. |
| `linuxFxVersion` | string | `DOTNET-ISOLATED\|10.0` | Linux runtime stack. |
| `skuName` | string | `Y1` | Hosting plan SKU (`Y1` consumption, `EP1`-`EP3` premium, or a dedicated SKU). |
| `skuTier` | string | `Dynamic` | Hosting plan tier matching `skuName`. |
| `enableApplicationInsights` | bool | `true` | Create Application Insights and wire it up. |
| `appSettings` | map of string | none | Extra application settings (override component-managed ones). |
| `tags` | map of string | none | Tags for all resources. |

## Outputs

`resourceGroupName`, `functionAppName`, `defaultHostName`, `url`, `storageAccountName`, `principalId`.

## Example (C#)

```csharp
var func = new FunctionApp("api", new FunctionAppArgs
{
    WorkerRuntime = "dotnet-isolated",
    EnableApplicationInsights = true,
});
```
