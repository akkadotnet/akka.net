//-----------------------------------------------------------------------
// <copyright file="AkkaFeatures.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Akka.Util
{
    /// <summary>
    /// Trimming and Native AOT feature switches for Akka.NET.
    /// </summary>
    internal static class AkkaFeatures
    {
        private const string DynamicTypeLoadingSwitch = "Akka.DynamicTypeLoading";

        /// <summary>
        /// Controls whether Akka.NET may turn a type name that came out of HOCON into a
        /// <see cref="Type"/> at runtime through <see cref="Type.GetType(string)"/> and friends.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The switch defaults to <c>true</c> when it is not set, so an ordinary JIT application behaves
        /// exactly as it does today: every configurable type name is resolved by reflection, and any type
        /// the host can load is fair game.
        /// </para>
        /// <para>
        /// A trimmed or Native AOT application turns it off in its project file:
        /// <code>
        /// &lt;ItemGroup&gt;
        ///   &lt;RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="false" Trim="true" /&gt;
        /// &lt;/ItemGroup&gt;
        /// </code>
        /// With <c>Trim="true"</c> the trimmer substitutes this property with the constant <c>false</c>, so
        /// every reflection branch guarded by it becomes unreachable and gets removed - which is what keeps
        /// the <c>IL2026</c>/<c>IL3050</c> warnings out of the publish and the unused types out of the
        /// binary. In exchange, only the type names Akka.NET knows at compile time resolve; anything else
        /// raises a <see cref="Akka.Configuration.ConfigurationException"/> that names the HOCON setting, the
        /// value it could not resolve, and the switch. Use <see cref="NotBuiltIn"/> to build that message.
        /// </para>
        /// <para>
        /// Turning it off is also useful on the JIT, where it acts as a strict mode: the application fails
        /// fast on a HOCON type name that would not survive trimming, instead of finding out at publish time.
        /// </para>
        /// <para>
        /// On the JIT the value is read from <see cref="AppContext"/> on every call rather than cached, so a
        /// spec (or an application doing its own bootstrapping) can flip it with
        /// <see cref="AppContext.SetSwitch(string,bool)"/> and have the next resolution see the new value.
        /// Every call site is cold - actor system startup or configuration reload - so the lookup is not on
        /// any hot path. In a trimmed app that declared the switch with <c>Trim="true"</c> none of that
        /// applies: the getter has already been replaced by a constant at publish time, and
        /// <see cref="AppContext.SetSwitch(string,bool)"/> at runtime has no effect on it.
        /// </para>
        /// <para>
        /// Every <c>BuiltIn*</c> table carries exactly two keys per type - the bare <c>Ns.T</c> and the
        /// <c>Ns.T, Akka</c> form - and the lookup runs the configured value through
        /// <see cref="TypeExtensions.StripAssemblyIdentity"/> first. That is how a full
        /// <see cref="Type.AssemblyQualifiedName"/>, which Akka.Hosting writes into HOCON, matches the second
        /// key regardless of the version, culture or public key token it names. Never add a third key spelled
        /// <c>typeof(T).AssemblyQualifiedName</c>: it roots nothing the table does not already root, and it
        /// only ever matches the version of the build that produced it.
        /// </para>
        /// <para>
        /// A call site must not <c>Trim()</c> the value it reads: Akka's HOCON parser already strips leading
        /// and trailing whitespace from every string it hands back, in every form - quoted, unquoted,
        /// triple-quoted, concatenated and substituted - and a whitespace-only value comes back empty or
        /// null. Trimming again is dead code that only hides where the value came from. Nor may a call site
        /// trim the table KEYS: a trimmed key can collide with one that is already in the dictionary, which
        /// turns a collection initializer into a runtime <see cref="ArgumentException"/>.
        /// </para>
        /// <para>
        /// Every call site repeats the same four-arm shape - user Setup, built-in table, guarded reflection
        /// fallback, throw. Do NOT factor that into a shared helper that takes the fallback as a delegate:
        /// constructing a delegate that points at a <see cref="RequiresUnreferencedCodeAttribute"/> method
        /// happens outside the guarded branch, so the trimmer stops treating the reflection code as
        /// unreachable and <c>IL2026</c> comes back at every site.
        /// </para>
        /// <para>
        /// The two attribute families do different jobs and both are needed.
        /// <see cref="FeatureSwitchDefinitionAttribute"/> is what lets ILLink/ILC substitute this getter with
        /// a constant and delete the branches behind it. <see cref="FeatureGuardAttribute"/> is what stops
        /// the Roslyn trim/AOT analyzer reporting <c>IL2026</c>/<c>IL3050</c> at every guarded call site -
        /// measured: with only the switch definition, a call site inside <c>if (IsDynamicTypeLoadingSupported)</c>
        /// still warns.
        /// </para>
        /// <para>
        /// The cost is that the analyzer cannot verify a <see cref="FeatureGuardAttribute"/> whose value comes
        /// from <see cref="AppContext"/>: it only accepts a constant <c>false</c> or another recognized check
        /// such as <c>RuntimeFeature.IsDynamicCodeSupported</c>, so it reports <c>IL4000</c> ("return value
        /// does not match FeatureGuardAttribute") on this property. That is a complaint about what the
        /// analyzer can prove, not about whether the guard works - ILC does substitute the getter and does
        /// drop the branches. Akka.dll therefore does not turn on <c>EnableTrimAnalyzer</c>/
        /// <c>EnableAotAnalyzer</c> today. Whoever turns them on has to deal with those two <c>IL4000</c>s
        /// first, and note that passing <c>-p:PublishAot=true</c> on a command line enables both analyzers
        /// for every project in the build graph as a side effect.
        /// </para>
        /// </remarks>
        [FeatureSwitchDefinition(DynamicTypeLoadingSwitch)]
        [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
        [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
        internal static bool IsDynamicTypeLoadingSupported =>
            !AppContext.TryGetSwitch(DynamicTypeLoadingSwitch, out var isSupported) || isSupported;

        /// <summary>
        /// Builds the <see cref="Akka.Configuration.ConfigurationException"/> message for a HOCON type name
        /// that is not one of Akka.NET's built-in values while dynamic type loading is switched off. Every
        /// site shares this builder so the wording cannot drift.
        /// </summary>
        /// <param name="setting">The HOCON path that carried the value, e.g. <c>akka.scheduler.implementation</c>.</param>
        /// <param name="value">The type name that could not be resolved.</param>
        /// <param name="alternative">What the user should reach for instead, e.g. "one of the built-in schedulers".</param>
        internal static string NotBuiltIn(string setting, string value, string alternative)
            => $"[{setting}] [{value}] is not built in and dynamic type loading is disabled. " +
               $"Use {alternative} or enable the [{DynamicTypeLoadingSwitch}] feature switch.";
    }
}
