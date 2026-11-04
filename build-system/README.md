# Azure Pipelines Build Files
These `.yaml` files are used by Windows Azure DevOps Pipelines to help execute the following types of builds:

* Pull request validation on Linux (Mono / .NET Core)
* Pull request validation on Windows (.NET Framework / .NET Core)

**NOTE**: NuGet releases are no longer published from Azure DevOps. Pushing a version tag triggers `.github/workflows/publish_nuget.yml`, which publishes to NuGet.org via trusted publishing (OIDC) and creates the GitHub release.