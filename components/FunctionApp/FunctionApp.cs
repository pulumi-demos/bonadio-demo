using Pulumi;
using Pulumi.AzureNative.ApplicationInsights;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Resources = Pulumi.AzureNative.Resources;

namespace Bonadio.Components;

/// <summary>
/// Arguments for the <see cref="FunctionApp"/> component.
/// </summary>
public sealed class FunctionAppArgs : ResourceArgs
{
    /// <summary>
    /// Name of an existing resource group. When omitted, the component creates one.
    /// If provided, <see cref="Location"/> must also be set.
    /// </summary>
    [Input("resourceGroupName")]
    public Input<string>? ResourceGroupName { get; set; }

    /// <summary>
    /// Azure region. Defaults to the provider's configured location when a resource
    /// group is created by the component.
    /// </summary>
    [Input("location")]
    public Input<string>? Location { get; set; }

    /// <summary>
    /// Functions worker runtime: dotnet-isolated, node, python, java or powershell.
    /// Defaults to dotnet-isolated.
    /// </summary>
    [Input("workerRuntime")]
    public Input<string>? WorkerRuntime { get; set; }

    /// <summary>
    /// Linux runtime stack, e.g. DOTNET-ISOLATED|10.0, NODE|22 or PYTHON|3.12.
    /// Defaults to DOTNET-ISOLATED|10.0.
    /// </summary>
    [Input("linuxFxVersion")]
    public Input<string>? LinuxFxVersion { get; set; }

    /// <summary>
    /// Hosting plan SKU name. Y1 (default) is the Linux Consumption plan; use
    /// EP1/EP2/EP3 for Elastic Premium or B1/S1/P1v3 for a dedicated plan.
    /// </summary>
    [Input("skuName")]
    public Input<string>? SkuName { get; set; }

    /// <summary>
    /// Hosting plan SKU tier. Defaults to Dynamic. Use ElasticPremium, Basic, Standard
    /// or PremiumV3 to match <see cref="SkuName"/>.
    /// </summary>
    [Input("skuTier")]
    public Input<string>? SkuTier { get; set; }

    /// <summary>Create an Application Insights instance and wire it up. Defaults to true.</summary>
    public bool EnableApplicationInsights { get; set; } = true;

    [Input("appSettings")]
    private InputMap<string>? _appSettings;

    /// <summary>
    /// Additional application settings. Settings managed by the component
    /// (AzureWebJobsStorage, FUNCTIONS_*) can be overridden here.
    /// </summary>
    public InputMap<string> AppSettings
    {
        get => _appSettings ??= new InputMap<string>();
        set => _appSettings = value;
    }

    [Input("tags")]
    private InputMap<string>? _tags;

    /// <summary>Tags applied to all resources created by the component.</summary>
    public InputMap<string> Tags
    {
        get => _tags ??= new InputMap<string>();
        set => _tags = value;
    }
}

/// <summary>
/// Creates a Linux Azure Function App with a backing storage account, hosting plan
/// and (optionally) Application Insights. Secure defaults: HTTPS only, TLS 1.2+,
/// FTPS disabled, no public blob access and a system-assigned managed identity.
/// </summary>
public sealed class FunctionApp : ComponentResource
{
    /// <summary>Name of the resource group containing the function app.</summary>
    [Output("resourceGroupName")]
    public Output<string> ResourceGroupName { get; private set; }

    /// <summary>Name of the function app.</summary>
    [Output("functionAppName")]
    public Output<string> FunctionAppName { get; private set; }

    /// <summary>Default host name of the function app.</summary>
    [Output("defaultHostName")]
    public Output<string> DefaultHostName { get; private set; }

    /// <summary>HTTPS URL of the function app.</summary>
    [Output("url")]
    public Output<string> Url { get; private set; }

    /// <summary>Name of the backing storage account.</summary>
    [Output("storageAccountName")]
    public Output<string> StorageAccountName { get; private set; }

    /// <summary>Principal ID of the system-assigned managed identity.</summary>
    [Output("principalId")]
    public Output<string> PrincipalId { get; private set; }

    public FunctionApp(string name, FunctionAppArgs args, ComponentResourceOptions? options = null)
        : base("bonadio:azure:FunctionApp", name, options)
    {
        var tags = args.Tags;
        var workerRuntime = args.WorkerRuntime ?? "dotnet-isolated";

        Output<string> resourceGroupName;
        Output<string> location;

        if (args.ResourceGroupName is null)
        {
            var resourceGroup = new Resources.ResourceGroup($"{name}-rg", new Resources.ResourceGroupArgs
            {
                Location = args.Location,
                Tags = tags,
            }, new CustomResourceOptions { Parent = this });

            resourceGroupName = resourceGroup.Name;
            location = resourceGroup.Location;
        }
        else
        {
            if (args.Location is null)
            {
                throw new System.ArgumentException(
                    "Location is required when ResourceGroupName is provided.", nameof(args));
            }

            resourceGroupName = args.ResourceGroupName.ToOutput();
            location = args.Location.ToOutput();
        }

        // Storage account required by the Functions host (state, triggers, content).
        var storage = new StorageAccount($"{name}sa", new StorageAccountArgs
        {
            ResourceGroupName = resourceGroupName,
            Location = location,
            Kind = Pulumi.AzureNative.Storage.Kind.StorageV2,
            Sku = new SkuArgs { Name = SkuName.Standard_LRS },
            MinimumTlsVersion = MinimumTlsVersion.TLS1_2,
            AllowBlobPublicAccess = false,
            EnableHttpsTrafficOnly = true,
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });

        var storageKeys = ListStorageAccountKeys.Invoke(new ListStorageAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroupName,
            AccountName = storage.Name,
        });

        var storageConnectionString = Output.Tuple(storage.Name, storageKeys).Apply(t =>
            Output.CreateSecret(
                $"DefaultEndpointsProtocol=https;AccountName={t.Item1};AccountKey={t.Item2.Keys[0].Value};EndpointSuffix=core.windows.net"));

        var plan = new AppServicePlan($"{name}-plan", new AppServicePlanArgs
        {
            ResourceGroupName = resourceGroupName,
            Location = location,
            Kind = "linux",
            Reserved = true, // required for Linux plans
            Sku = new SkuDescriptionArgs
            {
                Name = args.SkuName ?? "Y1",
                Tier = args.SkuTier ?? "Dynamic",
            },
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });

        // Component-managed settings, merged under any user supplied settings.
        var baseSettings = new InputMap<string>
        {
            { "AzureWebJobsStorage", storageConnectionString },
            { "FUNCTIONS_EXTENSION_VERSION", "~4" },
            { "FUNCTIONS_WORKER_RUNTIME", workerRuntime },
        };

        // Resources cannot be created inside Apply, so this is a plain bool.
        if (args.EnableApplicationInsights)
        {
            var insights = new Component($"{name}-ai", new ComponentArgs
            {
                ResourceGroupName = resourceGroupName,
                Location = location,
                Kind = "web",
                ApplicationType = ApplicationType.Web,
                Tags = tags,
            }, new CustomResourceOptions { Parent = this });

            baseSettings.Add("APPLICATIONINSIGHTS_CONNECTION_STRING", insights.ConnectionString);
        }

        var appSettings = Output.Tuple(baseSettings.ToOutput(), args.AppSettings.ToOutput()).Apply(t =>
        {
            var merged = new Dictionary<string, string>(t.Item1);
            foreach (var kv in t.Item2)
            {
                merged[kv.Key] = kv.Value;
            }

            return merged.Select(kv => new NameValuePairArgs { Name = kv.Key, Value = kv.Value }).ToList();
        });

        var app = new WebApp($"{name}-func", new WebAppArgs
        {
            ResourceGroupName = resourceGroupName,
            Location = location,
            Kind = "functionapp,linux",
            ServerFarmId = plan.Id,
            HttpsOnly = true,
            Identity = new ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.Web.ManagedServiceIdentityType.SystemAssigned,
            },
            SiteConfig = new SiteConfigArgs
            {
                LinuxFxVersion = args.LinuxFxVersion ?? "DOTNET-ISOLATED|10.0",
                Http20Enabled = true,
                MinTlsVersion = "1.2",
                FtpsState = "Disabled",
                AppSettings = appSettings,
            },
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });

        ResourceGroupName = resourceGroupName;
        FunctionAppName = app.Name;
        DefaultHostName = app.DefaultHostName;
        Url = app.DefaultHostName.Apply(h => $"https://{h}");
        StorageAccountName = storage.Name;
        PrincipalId = app.Identity.Apply(i => i!.PrincipalId);

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["resourceGroupName"] = ResourceGroupName,
            ["functionAppName"] = FunctionAppName,
            ["defaultHostName"] = DefaultHostName,
            ["url"] = Url,
            ["storageAccountName"] = StorageAccountName,
            ["principalId"] = PrincipalId,
        });
    }
}
