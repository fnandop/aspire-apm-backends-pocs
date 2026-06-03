using Aspire.Hosting.ApplicationModel;

internal sealed record ApplicationResources(
    IResourceBuilder<SqlServerDatabaseResource> Mssql,
    IResourceBuilder<MongoDBDatabaseResource> Mongo,
    IResourceBuilder<PostgresDatabaseResource> Postgres,
    IResourceBuilder<ContainerResource> NodeApi,
    IResourceBuilder<ContainerResource> SpringApi,
    IResourceBuilder<ProjectResource> ApiService,
    IResourceBuilder<ProjectResource> Gateway,
    IResourceBuilder<ContainerResource> ReactUi);
