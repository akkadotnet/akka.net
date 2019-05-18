# Azure Pipelines Build Files
These `.yaml` files are used by Windows Azure DevOps Pipelines to help execute the following types of builds:

- Pull request validation on Linux (Mono / .NET Core)
- Pull request validation on Windows (.NET Framework / .NET Core)
- NuGet releases with automatic release notes posted to a Github Release repository.

**NOTE**: you will need to change some of the pipeline variables inside the `windows-release.yaml` for your specific project and you will also want to create variable groups with your signing and NuGet push information.