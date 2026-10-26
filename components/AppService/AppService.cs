using Pulumi;
using Resources = Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;

namespace Bonadio.Components;

/// <summary>
/// Arguments for the <see cref="AppService"/> component.
/// </summary>
public sealed class AppServiceArgs : ResourceArgs
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

    /// <summary>App Service plan SKU name, e.g. B1, S1, P1v3. Defaults to B1.</summary>
    [Input("skuName")]
    public Input<string>? SkuName { get; set; }

    /// <summary>App Service plan SKU tier, e.g. Basic, Standard, PremiumV3. Defaults to Basic.</summary>
    [Input("skuTier")]
    public Input<string>? SkuTier { get; set; }

    /// <summary>Linux runtime stack, e.g. DOTNETCORE|10.0 or NODE|22-lts. Defaults to DOTNETCORE|10.0.</summary>
    [Input("linuxFxVersion")]
    public Input<string>? LinuxFxVersion { get; set; }

    /// <summary>Keep the app loaded (not supported on Free/Shared SKUs). Defaults to true.</summary>
    [Input("alwaysOn")]
    public Input<bool>? AlwaysOn { get; set; }


    /// <summary>Application settings (environment variables) for the web app.</summary>
    [Input("appSettings")]
    public InputMap<string>? AppSettings { get; set; }


    /// <summary>Tags applied to all resources created by the component.</summary>
    [Input("tags")]
    public InputMap<string>? Tags { get; set; }
}

/// <summary>
/// Creates a Linux Azure App Service (web app) with its App Service plan and,
/// optionally, its resource group. Secure defaults: HTTPS only, TLS 1.2+,
/// FTPS disabled and a system-assigned managed identity.
/// </summary>
public sealed class AppService : ComponentResource
{
    /// <summary>Name of the resource group containing the app.</summary>
    [Output("resourceGroupName")]
    public Output<string> ResourceGroupName { get; private set; }

    /// <summary>Name of the web app.</summary>
    [Output("appName")]
    public Output<string> AppName { get; private set; }

    /// <summary>Default host name of the web app.</summary>
    [Output("defaultHostName")]
    public Output<string> DefaultHostName { get; private set; }

    /// <summary>HTTPS URL of the web app.</summary>
    [Output("url")]
    public Output<string> Url { get; private set; }

    /// <summary>Principal ID of the system-assigned managed identity.</summary>
    [Output("principalId")]
    public Output<string> PrincipalId { get; private set; }

    public AppService(string name, AppServiceArgs args, ComponentResourceOptions? options = null)
        : base("azure-appservice:index:AppService", name, options)
    {
        var tags = args.Tags ?? new InputMap<string>();

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

        var plan = new AppServicePlan($"{name}-plan", new AppServicePlanArgs
        {
            ResourceGroupName = resourceGroupName,
            Location = location,
            Kind = "linux",
            Reserved = true, // required for Linux plans
            Sku = new SkuDescriptionArgs
            {
                Name = args.SkuName ?? "B1",
                Tier = args.SkuTier ?? "Basic",
            },
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });

        var appSettings = (args.AppSettings ?? new InputMap<string>()).ToOutput().Apply(settings =>
            settings.Select(kv => new NameValuePairArgs { Name = kv.Key, Value = kv.Value }).ToList());

        var app = new WebApp($"{name}-app", new WebAppArgs
        {
            ResourceGroupName = resourceGroupName,
            Location = location,
            Kind = "app,linux",
            ServerFarmId = plan.Id,
            HttpsOnly = true,
            Identity = new ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.Web.ManagedServiceIdentityType.SystemAssigned,
            },
            SiteConfig = new SiteConfigArgs
            {
                LinuxFxVersion = args.LinuxFxVersion ?? "DOTNETCORE|10.0",
                AlwaysOn = args.AlwaysOn ?? true,
                Http20Enabled = true,
                MinTlsVersion = "1.2",
                FtpsState = "Disabled",
                AppSettings = appSettings,
            },
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });

        ResourceGroupName = resourceGroupName;
        AppName = app.Name;
        DefaultHostName = app.DefaultHostName;
        Url = app.DefaultHostName.Apply(h => $"https://{h}");
        PrincipalId = app.Identity.Apply(i => i!.PrincipalId);

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["resourceGroupName"] = ResourceGroupName,
            ["appName"] = AppName,
            ["defaultHostName"] = DefaultHostName,
            ["url"] = Url,
            ["principalId"] = PrincipalId,
        });
    }
}
