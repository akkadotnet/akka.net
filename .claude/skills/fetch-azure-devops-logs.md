# Fetch Azure DevOps Build Logs

Use this skill when you need to retrieve build logs, test results, or failure details from Azure DevOps CI builds for the Akka.NET project.

## Key Information

- **Organization**: `dotnet`
- **Project**: `Akka.NET` (NOT `public` - this is critical!)
- **Base URL**: `https://dev.azure.com/dotnet/Akka.NET`

## Required Header

Always include this header to prevent authentication redirects on public project data:

```
X-TFS-FedAuthRedirect: Suppress
```

Without this header, requests will redirect to a sign-in page even for publicly accessible data.

## Common API Endpoints

### Get Build Information

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress"
```

### List Build Logs

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/logs?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress"
```

### Get Specific Log Content

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/logs/{logId}?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress"
```

## Finding Test Failures

1. First, get the list of logs and find ones with high line counts (test output logs typically have 1000+ lines):

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/logs?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | jq '.value[] | select(.lineCount > 1000) | {id, lineCount}'
```

2. Then search for failures in those logs:

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/logs/{logId}?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | grep -i "failed\|error" | head -30
```

## Getting Build ID from PR

The build ID can be found in PR check URLs. For example:
- URL: `https://dev.azure.com/dotnet/.../_build/results?buildId=122796`
- Build ID: `122796`

Or use GitHub CLI:

```bash
gh pr checks {pr_number} --repo akkadotnet/akka.net
```

The build ID appears in the `detailsUrl` for each check.

## Example: Full Workflow

```bash
# 1. Get build status
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/122796?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | jq '{buildNumber, result, status}'

# 2. Find large logs (likely test output)
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/122796/logs?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | jq '.value[] | select(.lineCount > 1000) | {id, lineCount}'

# 3. Search for test failures in a specific log
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/122796/logs/122?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | grep -i "Failed.*\[" | head -20
```

## Build Artifacts

Build artifacts contain detailed test results, especially useful for multi-node tests.

### List Available Artifacts

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/artifacts?api-version=7.1" \
  -H "X-TFS-FedAuthRedirect: Suppress" | jq '.value[] | {name}'
```

### Common Artifact Names

| Artifact Name | Description |
|--------------|-------------|
| `net_tests_windows-{buildId}` | .NET unit test results (Windows) |
| `net_tests_linux-{buildId}` | .NET unit test results (Linux) |
| `netfx_tests_windows-{buildId}` | .NET Framework test results |
| `net_mntr_windows-{buildId}` | Multi-node test results |
| `net_7_mntr_FAILED_windows-{buildId}` | **Failed** multi-node test logs (most useful for debugging!) |

### Download an Artifact

```bash
curl -sS "https://dev.azure.com/dotnet/Akka.NET/_apis/build/builds/{buildId}/artifacts?artifactName={artifactName}&api-version=7.1&%24format=zip" \
  -H "X-TFS-FedAuthRedirect: Suppress" -o artifact.zip
```

### Multi-Node Test Failures

For multi-node test failures, the `net_7_mntr_FAILED_windows-{buildId}` artifact contains:
- Individual node logs for each test node
- Barrier synchronization logs
- Full stack traces and timing information

This is much more detailed than what appears in the console logs.

## Notes

- The Test Results API (`_apis/test/runs`) requires authentication and cannot be accessed anonymously
- Build logs, build info, and artifacts are publicly accessible with the suppress header
- Always use `Akka.NET` as the project name, not `public` or the project GUID
- For multi-node test failures, always check the `FAILED` artifact for detailed node-by-node logs
