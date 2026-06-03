using Aspire.Hosting.ApplicationModel;

internal sealed record ObservabilityResources(
    string Backend,
    IResourceBuilder<ContainerResource> OtelCollector);
