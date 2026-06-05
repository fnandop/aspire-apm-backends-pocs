using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

internal static class AzureContainerAppIngressExtensions
{
    public static IResourceBuilder<ContainerResource> AllowInsecureHttpIngress(
        this IResourceBuilder<ContainerResource> builder)
    {
        return builder.PublishAsAzureContainerApp((_, app) => app.Configuration.Ingress.AllowInsecure = true);
    }

    public static IResourceBuilder<ProjectResource> AllowInsecureHttpIngress(
        this IResourceBuilder<ProjectResource> builder)
    {
        return builder.PublishAsAzureContainerApp((_, app) => app.Configuration.Ingress.AllowInsecure = true);
    }
}
