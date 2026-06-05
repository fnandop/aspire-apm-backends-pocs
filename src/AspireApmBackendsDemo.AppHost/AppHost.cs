var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment(ResourceNames.ContainerAppEnvironment)
    .WithHttpsUpgrade(false);

var paths = AppHostPaths.FromAppHost();
var observability = builder.AddObservability(paths);
var application = builder.AddApplicationComponents(paths, observability);

builder.AddSelectedObservabilityBackend(paths, observability, application);

builder.Build().Run();
