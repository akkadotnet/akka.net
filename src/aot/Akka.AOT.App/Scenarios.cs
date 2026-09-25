//-----------------------------------------------------------------------
// <copyright file="Scenarios.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.AOT.App.Actors;
using Akka.Configuration;
using Akka.Routing;
using static Akka.AOT.App.Program;

namespace Akka.AOT.App;

/// <summary>
/// The rest of the local-ActorSystem surface this canary proves works trimmed and AOT-compiled with
/// Akka.DynamicTypeLoading off: routers (pool, group, and one resolved by name through the
/// Deployer's built-in table - see #8605), Stash/Become, IWithTimers, a small FSM, DeathWatch,
/// PipeTo, and a non-default dispatcher and mailbox.
/// </summary>
internal static class Scenarios
{
    public static async Task RunAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        await RunRoutersAsync(label, system, askTimeout);
        await RunStashAsync(label, system, askTimeout);
        await RunTimerAsync(label, system, askTimeout);
        await RunFsmAsync(label, system, askTimeout);
        await RunDeathWatchAsync(label, system, askTimeout);
        await RunPipeToAsync(label, system, askTimeout);
        await RunDispatcherAndMailboxAsync(label, system, askTimeout);

        Console.WriteLine($"[canary] {label}: routers, stash, timers, FSM, deathwatch, pipeTo and dispatcher/mailbox all resolved");
    }

    private static async Task RunRoutersAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        // Pool router, built from code.
        var pool = system.ActorOf(Props.Create(() => new AotReceiveActor()).WithRouter(new RoundRobinPool(2)),
            "pool-router");
        var poolReply = await pool.Ask<string>($"pool:{label}", askTimeout);
        Require(label, poolReply == $"pool:{label}", $"pool router replied '{poolReply}'");

        // Group router, built from code over two routees this run already owns.
        var routee1 = system.ActorOf(Props.Create(() => new AotReceiveActor()), "group-routee-1");
        var routee2 = system.ActorOf(Props.Create(() => new AotReceiveActor()), "group-routee-2");
        var group = system.ActorOf(
            new RoundRobinGroup(routee1.Path.ToStringWithoutAddress(), routee2.Path.ToStringWithoutAddress()).Props(),
            "group-router");
        var groupReply = await group.Ask<string>($"group:{label}", askTimeout);
        Require(label, groupReply == $"group:{label}", $"group router replied '{groupReply}'");

        // A router resolved by name from HOCON, through Deployer.CreateRouterConfig's built-in table
        // (#8605) rather than a 'new RoundRobinPool(...)' in code.
        var deployer = ((ExtendedActorSystem)system).Provider.Deployer;
        var deploy = deployer.ParseConfig("/hocon-pool-router",
            ConfigurationFactory.ParseString("router = round-robin-pool\nnr-of-instances = 2"));
        deployer.SetDeploy(deploy);
        var hoconPool = system.ActorOf(Props.Create(() => new AotReceiveActor()).WithRouter(FromConfig.Instance),
            "hocon-pool-router");
        var hoconReply = await hoconPool.Ask<string>($"hocon:{label}", askTimeout);
        Require(label, hoconReply == $"hocon:{label}", $"HOCON-deployed router replied '{hoconReply}'");
    }

    private static async Task RunStashAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var stasher = system.ActorOf(Props.Create(() => new AotStashActor()), "stash-actor");

        // stashed while locked; only replayed once "open" flips the behavior.
        var stashedReply = stasher.Ask<string>("echo", askTimeout);
        var openedReply = await stasher.Ask<string>("open", askTimeout);
        Require(label, openedReply == "opened", $"stash actor replied '{openedReply}' to 'open'");

        var replayedReply = await stashedReply;
        Require(label, replayedReply == "open:echo", $"stashed message replayed as '{replayedReply}'");
    }

    private static async Task RunTimerAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var timerActor = system.ActorOf(Props.Create(() => new AotTimerActor()), "timer-actor");
        var reply = await timerActor.Ask<string>("start", askTimeout);
        Require(label, reply == "ticked", $"timer actor replied '{reply}'");
    }

    private static async Task RunFsmAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var fsm = system.ActorOf(Props.Create(() => new AotFsmActor()), "fsm-actor");

        var started = await fsm.Ask<string>("go", askTimeout);
        Require(label, started == "started", $"FSM replied '{started}' to its one transition");

        // now in the Active state - prove the second When() clause runs too.
        var active = await fsm.Ask<string>("ping", askTimeout);
        Require(label, active == "active:ping", $"FSM (now Active) replied '{active}'");
    }

    private static async Task RunDeathWatchAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var target = system.ActorOf(Props.Create(() => new AotReceiveActor()), "watch-target");
        var watcher = system.ActorOf(Props.Create(() => new AotWatcherActor()), "watcher-actor");
        var terminatedReply = watcher.Ask<string>(target, askTimeout);
        system.Stop(target);
        var reply = await terminatedReply;
        Require(label, reply == "terminated:watch-target", $"watcher replied '{reply}'");
    }

    private static async Task RunPipeToAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var piper = system.ActorOf(Props.Create(() => new AotPipeToActor()), "pipeto-actor");
        var reply = await piper.Ask<string>("hello", askTimeout);
        Require(label, reply == "piped:hello", $"PipeTo actor replied '{reply}'");
    }

    /// <summary>
    /// 'default-fork-join-dispatcher' is a non-default dispatcher already in akka.conf (type =
    /// ForkJoinDispatcher), resolved through the built-in alias switch in
    /// Dispatchers.ConfiguratorFrom rather than reflection. Looking it up alone proves nothing -
    /// Lookup() only builds a lazy configurator - so round-trip a real message through an actor
    /// that uses it, which is what actually spins up the ForkJoinExecutor/DedicatedThreadPool.
    ///
    /// 'bounded' is a hard-coded mailbox id in Mailboxes.LookupConfigurator, not the mailbox-type
    /// built-in table - that table is exercised for real by the stash scenario above, whose
    /// IWithUnboundedStash resolves 'unbounded-deque-based' through
    /// BuiltInMessageQueueSemantics/BuiltInMailboxTypes. This just proves an actor built on a
    /// bounded mailbox actually runs.
    /// </summary>
    private static async Task RunDispatcherAndMailboxAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var forkJoinActor = system.ActorOf(
            Props.Create(() => new AotReceiveActor()).WithDispatcher("akka.actor.default-fork-join-dispatcher"),
            "fork-join-actor");
        var forkJoinReply = await forkJoinActor.Ask<string>($"fork-join:{label}", askTimeout);
        Require(label, forkJoinReply == $"fork-join:{label}", $"fork-join dispatcher actor replied '{forkJoinReply}'");

        var boundedActor = system.ActorOf(
            Props.Create(() => new AotReceiveActor()).WithMailbox("bounded"),
            "bounded-mailbox-actor");
        var boundedReply = await boundedActor.Ask<string>($"bounded:{label}", askTimeout);
        Require(label, boundedReply == $"bounded:{label}", $"bounded-mailbox actor replied '{boundedReply}'");
    }
}
