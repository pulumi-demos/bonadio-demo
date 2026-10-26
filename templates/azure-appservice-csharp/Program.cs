using Bonadio.Components;
using Pulumi;

return await Deployment.RunAsync(() =>
{
    var config = new Config();
    var skuName = config.Get("skuName") ?? "B1";
    var skuTier = config.Get("skuTier") ?? "Basic";
    var linuxFxVersion = config.Get("linuxFxVersion") ?? "DOTNETCORE|10.0";

    var tags = new InputMap<string>
    {
        { "project", Deployment.Instance.ProjectName },
        { "stack", Deployment.Instance.StackName },
    };

    // Creates the resource group, App Service plan and web app.
    var appService = new AppService("web", new AppServiceArgs
    {
        SkuName = skuName,
        SkuTier = skuTier,
        LinuxFxVersion = linuxFxVersion,
        AppSettings =
        {
            { "ENVIRONMENT", Deployment.Instance.StackName },
        },
        Tags = tags,
    });

    return new Dictionary<string, object?>
    {
        ["resourceGroupName"] = appService.ResourceGroupName,
        ["appName"] = appService.AppName,
        ["url"] = appService.Url,
    };
});
