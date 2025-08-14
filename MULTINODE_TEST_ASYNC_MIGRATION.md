# Multi-Node Test Async Migration Guide

## Overview
This guide helps migrate multi-node tests from blocking synchronous calls to async/await patterns to prevent thread pool starvation and test timeouts in CI environments.

## Why This Migration Is Necessary
- **Root Cause**: Blocking `.Wait()` calls on TestConductor operations cause thread pool starvation
- **Symptoms**: 20+ second timeout failures in CI environments
- **Solution**: Replace all blocking calls with proper async/await patterns

## Migration Patterns to Look For

### 1. TestConductor Blocking Calls
**Look for these patterns:**
```csharp
// OLD - Blocking
TestConductor.Exit(role, 0).Wait();
TestConductor.Blackhole(node1, node2, direction).Wait();
TestConductor.PassThrough(node1, node2, direction).Wait();
TestConductor.Throttle(node1, node2, direction, rate).Wait();
TestConductor.Disconnect(node1, node2).Wait();
TestConductor.Shutdown(node, abort).Wait();
TestConductor.RemoveNode(node).Wait();

// NEW - Async
await TestConductor.ExitAsync(role, 0);
await TestConductor.BlackholeAsync(node1, node2, direction);
await TestConductor.PassThroughAsync(node1, node2, direction);
await TestConductor.ThrottleAsync(node1, node2, direction, rate);
await TestConductor.DisconnectAsync(node1, node2);
await TestConductor.ShutdownAsync(node, abort);
await TestConductor.RemoveNodeAsync(node);
```

### 2. Barrier Synchronization
**Look for:**
```csharp
// OLD
EnterBarrier("barrier-name");
EnterBarrier("barrier-1", "barrier-2");

// NEW
await EnterBarrierAsync("barrier-name");
await EnterBarrierAsync("barrier-1", "barrier-2");
```

### 3. RunOn with Async Operations
**Look for:**
```csharp
// OLD
RunOn(() => {
    TestConductor.Exit(role, 0).Wait();
}, roles);

// NEW
await RunOnAsync(async () => {
    await TestConductor.ExitAsync(role, 0);
}, roles);
```

### 4. Within Blocks
**Look for:**
```csharp
// OLD
Within(TimeSpan.FromSeconds(30), () => {
    // operations
    EnterBarrier("done");
});

// NEW
await WithinAsync(TimeSpan.FromSeconds(30), async () => {
    // operations
    await EnterBarrierAsync("done");
});
```

### 5. Test Method Signatures
**Change:**
```csharp
// OLD
[MultiNodeFact]
public void TestName()

// NEW
[MultiNodeFact]
public async Task TestName()
```

### 6. Helper Method Signatures
**Change:**
```csharp
// OLD
public void HelperMethod()

// NEW
public async Task HelperMethod()
```

## Required Imports
Add if missing:
```csharp
using System.Threading.Tasks;
```

## Migration Checklist

### ✅ Completed Tests
- [x] StressSpec
- [x] LeaderElectionSpec
- [x] ClusterAccrualFailureDetectorSpec
- [x] TestConductorSpec (in Remote.Tests.MultiNode)
- [x] RemoteNodeDeathWatchSpec (in Remote.Tests.MultiNode)

### Core Tests - Akka.Cluster.Tests.MultiNode
- [x] AttemptSysMsgRedeliverySpec
- [x] ClientDowningNodeThatIsUnreachableSpec
- [x] ClusterDeathWatchSpec
- [x] ConvergenceSpec
- [x] LeaderDowningAllOtherNodesSpec
- [x] LeaderDowningNodeThatIsUnreachableSpec
- [x] SingletonClusterSpec
- [x] SplitBrainResolverDowningSpec
- [x] SplitBrainSpec
- [x] SurviveNetworkInstabilitySpec
- [ ] UnreachableNodeJoinsAgainSpec *(Not migrated - victim node shutdown pattern incompatible with async)*

### Core Tests - Akka.Cluster.Tests.MultiNode/Routing
- [ ] ClusterRoundRobinSpec

### Core Tests - Akka.Cluster.Tests.MultiNode/SBR (Split Brain Resolver)
- [ ] DownAllIndirectlyConnected5NodeSpec
- [ ] DownAllUnstable5NodeSpec
- [ ] IndirectlyConnected3NodeSpec
- [ ] IndirectlyConnected5NodeSpec
- [ ] LeaseMajority5NodeSpec

### Core Tests - Akka.Remote.Tests.MultiNode
- [ ] RemoteNodeRestartGateSpec
- [ ] RemoteNodeShutdownAndComesBackSpec
- [ ] RemoteReDeploymentSpec
- [ ] RemoteRestartedQuarantinedSpec

### Contrib Tests - Akka.Cluster.Sharding.Tests.MultiNode
- [ ] ClusterShardCoordinatorDowning2Spec
- [ ] ClusterShardCoordinatorDowningSpec
- [ ] ClusterShardingFailureSpec
- [ ] ClusterShardingRememberEntitiesNewExtractorSpec
- [ ] ClusterShardingRememberEntitiesSpec
- [ ] ClusterShardingSingleShardPerEntitySpec
- [ ] ClusterShardingSpec

### Contrib Tests - Akka.Cluster.Tools.Tests.MultiNode
- [ ] ClusterClient/ClusterClientDiscoverySpec
- [ ] ClusterClient/ClusterClientSpec
- [ ] PublishSubscribe/DistributedPubSubMediatorSpec
- [x] PublishSubscribe/DistributedPubSubRestartSpec
- [ ] Singleton/ClusterSingletonManagerDownedSpec
- [ ] Singleton/ClusterSingletonManagerSpec

### Tests That May Need EnterBarrier -> EnterBarrierAsync Migration
Additional tests that use EnterBarrier but may not have TestConductor blocking calls still need to be converted for consistency. Run this to find them:
```bash
find src -name "*.cs" -path "*Tests.MultiNode*" -exec grep -l "EnterBarrier(" {} \;
```

## Migration Steps

1. **Add async Task import**
   ```csharp
   using System.Threading.Tasks;
   ```

2. **Convert test method signature**
   - Change `public void` to `public async Task`

3. **Find and replace blocking patterns**
   - Search for `.Wait()` calls
   - Search for `EnterBarrier(`
   - Search for `Within(`
   - Search for `RunOn(` with async operations inside

4. **Update method calls**
   - Add `await` keyword before async calls
   - Change method names to async versions (add `Async` suffix)
   - Update lambdas to `async` when needed

5. **Update helper methods**
   - Convert any helper methods that now contain async calls
   - Propagate async/await up the call chain

6. **Build and verify**
   ```bash
   dotnet build src/core/Akka.Cluster.Tests.MultiNode/Akka.Cluster.Tests.MultiNode.csproj -c Release
   ```

7. **Run tests (example)**
   ```bash
   dotnet test src/core/Akka.Cluster.Tests.MultiNode/Akka.Cluster.Tests.MultiNode.csproj \
     -c Release --filter "FullyQualifiedName~YourTestName" --framework net8.0
   ```

## Common Pitfalls to Avoid

1. **Don't use ConfigureAwait(false) in tests**
   - Tests should maintain their synchronization context

2. **Don't use GetAwaiter().GetResult()**
   - This is just as bad as .Wait() for blocking

3. **Ensure all async operations are awaited**
   - Missing awaits can cause race conditions

4. **Watch for nested RunOn calls**
   - Inner RunOn may need to become RunOnAsync if it contains async operations

5. **Don't forget lambda async modifiers**
   ```csharp
   // Wrong
   ReportResult(() => { await SomeAsync(); });
   
   // Right
   ReportResult(async () => { await SomeAsync(); });
   ```

## Verification Commands

Check for remaining blocking calls:
```bash
# Find .Wait() calls
grep -r "\.Wait()" src --include="*.cs" | grep -i multinode

# Find EnterBarrier calls
grep -r "EnterBarrier(" src --include="*.cs" | grep -i multinode

# Find TestConductor blocking calls
grep -r "TestConductor\.[A-Z].*\.Wait()" src --include="*.cs"
```

## Git Commit Message Template
```
Convert [TestName] to async

- Convert main test method to async Task
- Replace TestConductor.[Method]().Wait() with await TestConductor.[Method]Async()
- Replace EnterBarrier with EnterBarrierAsync
- Use RunOnAsync for async operations
- Use WithinAsync for async timing constraints
- Add using System.Threading.Tasks
```

## Notes
- This migration improves test reliability by preventing thread pool starvation
- Tests should run faster and more reliably in CI environments
- The async APIs provide better cancellation support via CancellationToken