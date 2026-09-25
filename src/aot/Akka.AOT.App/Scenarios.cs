//-----------------------------------------------------------------------
// <copyright file="Scenarios.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.AOT.App.Actors;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Routing;

namespace Akka.AOT.App;

/// <summary>
/// The rest of the local-ActorSystem surface this canary proves works trimmed and AOT-compiled with
/// Akka.DynamicTypeLoading off: routers (pool, group, and one resolved by name through the
/// Deployer's built-in table - see #8605), Stash/Become, IWithTimers, a small FSM, DeathWatch,
/// PipeTo, and a non-default dispatcher/mailbox that ship in akka.conf itself.
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
        AssertDispatcherAndMailbox(label, system);

        Console.WriteLine($"[canary] {label}: routers, stash, timers, FSM, deathwatch, pipeTo and HOCON dispatcher/mailbox all resolved");
    }

    private static async Task RunRoutersAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        // Pool router, built from code.
        var pool = system.ActorOf(Props.Create(() => new AotReceiveActor()).WithRouter(new RoundRobinPool(2)),
            "pool-router");
        var poolReply = await pool.Ask<string>($"pool:{label}", askTimeout);
        Canary.Require(label, poolReply == $"pool:{label}", $"pool router replied '{poolReply}'");

        // Group router, built from code over two routees this run already owns.
        var routee1 = system.ActorOf(Props.Create(() => new AotReceiveActor()), "group-routee-1");
        var routee2 = system.ActorOf(Props.Create(() => new AotReceiveActor()), "group-routee-2");
        var group = system.ActorOf(
            new RoundRobinGroup(routee1.Path.ToStringWithoutAddress(), routee2.Path.ToStringWithoutAddress()).Props(),
            "group-router");
        var groupReply = await group.Ask<string>($"group:{label}", askTimeout);
        Canary.Require(label, groupReply == $"group:{label}", $"group router replied '{groupReply}'");

        // A router resolved by name from HOCON, through Deployer.CreateRouterConfig's built-in table
        // (#8605) rather than a 'new RoundRobinPool(...)' in code.
        var deployer = ((ExtendedActorSystem)system).Provider.Deployer;
        var deploy = deployer.ParseConfig("/hocon-pool-router",
            ConfigurationFactory.ParseString("router = round-robin-pool\nnr-of-instances = 2"));
        deployer.SetDeploy(deploy);
        var hoconPool = system.ActorOf(Props.Create(() => new AotReceiveActor()).WithRouter(FromConfig.Instance),
            "hocon-pool-router");
        var hoconReply = await hoconPool.Ask<string>($"hocon:{label}", askTimeout);
        Canary.Require(label, hoconReply == $"hocon:{label}", $"HOCON-deployed router replied '{hoconReply}'");
    }

    private static async Task RunStashAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var stasher = system.ActorOf(Props.Create(() => new AotStashActor()), "stash-actor");

        // stashed while locked; only replayed once "open" flips the behavior.
        var stashedReply = stasher.Ask<string>("echo", askTimeout);
        var openedReply = await stasher.Ask<string>("open", askTimeout);
        Canary.Require(label, openedReply == "opened", $"stash actor replied '{openedReply}' to 'open'");

        var replayedReply = await stashedReply;
        Canary.Require(label, replayedReply == "open:echo", $"stashed message replayed as '{replayedReply}'");
    }

    private static async Task RunTimerAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var timerActor = system.ActorOf(Props.Create(() => new AotTimerActor()), "timer-actor");
        var reply = await timerActor.Ask<string>("start", askTimeout);
        Canary.Require(label, reply == "ticked", $"timer actor replied '{reply}'");
    }

    private static async Task RunFsmAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var fsm = system.ActorOf(Props.Create(() => new AotFsmActor()), "fsm-actor");
        var reply = await fsm.Ask<string>("go", askTimeout);
        Canary.Require(label, reply == "started", $"FSM replied '{reply}' to its one transition");
    }

    private static async Task RunDeathWatchAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var target = system.ActorOf(Props.Create(() => new AotReceiveActor()), "watch-target");
        var watcher = system.ActorOf(Props.Create(() => new AotWatcherActor()), "watcher-actor");
        var reply = await watcher.Ask<string>(target, askTimeout);
        Canary.Require(label, reply == "terminated:watch-target", $"watcher replied '{reply}'");
    }

    private static async Task RunPipeToAsync(string label, ActorSystem system, TimeSpan askTimeout)
    {
        var piper = system.ActorOf(Props.Create(() => new AotPipeToActor()), "pipeto-actor");
        var reply = await piper.Ask<string>("hello", askTimeout);
        Canary.Require(label, reply == "piped:hello", $"PipeTo actor replied '{reply}'");
    }

    /// <summary>
    /// Both ids already ship in akka.conf: 'default-fork-join-dispatcher' is a non-default
    /// dispatcher (type = ForkJoinDispatcher), and 'bounded' is the built-in bounded mailbox
    /// shortcut. Neither needs any HOCON this app supplies itself.
    /// </summary>
    private static void AssertDispatcherAndMailbox(string label, ActorSystem system)
    {
        const string forkJoinId = "akka.actor.default-fork-join-dispatcher";
        var forkJoinDispatcher = system.Dispatchers.Lookup(forkJoinId);
        Canary.Require(label, forkJoinDispatcher.Id == forkJoinId,
            $"dispatcher '{forkJoinId}' resolved to id '{forkJoinDispatcher.Id}'");

        var boundedMailbox = system.Mailboxes.Lookup("bounded");
        Canary.Require(label, boundedMailbox is BoundedMailbox,
            $"mailbox 'bounded' resolved to [{boundedMailbox.GetType().FullName}]");
    }
}
