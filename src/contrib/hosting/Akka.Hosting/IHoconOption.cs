// -----------------------------------------------------------------------
//  <copyright file="IOption.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor.Setup;

namespace Akka.Hosting
{
    /// <summary>
    /// <para>
    ///     Standardized interface template for a common HOCON configuration pattern where a configuration takes
    ///     a HOCON config path and the config path contains a class FQCN property and other settings that
    ///     are needed by said class.
    /// </para>
    /// <para>
    ///     The pattern looks like this:
    /// <code>
    ///     # This HOCON property references to a config block below
    ///     akka.discovery.method = akka.discovery.config
    /// 
    ///     akka.discovery.config {
    ///         class = "Akka.Discovery.Config.ConfigServiceDiscovery, Akka.Discovery"
    ///         # other options goes here
    ///     }
    /// </code>
    /// </para>
    /// </summary>
    /// <example>
    /// Example implementation for the pattern described in the summary
    /// <code>
    ///     // The base class for the option
    ///     public abstract class DiscoveryOptionBase : IOption
    ///     { }
    ///
    ///     // The actual option implementation
    ///     public class ConfigOption : DiscoveryOptionBase
    ///     {
    ///         // Actual option implementation here
    ///         public void Apply(AkkaConfigurationBuilder builder)
    ///         {
    ///             // Modifies Akka.NET configuration either via HOCON or setup class
    ///             builder.AddHocon($"akka.discovery.method = {ConfigPath}", HoconAddMode.Prepend);
    ///
    ///             // Rest of configuration goes here
    ///         }
    ///     }
    ///
    ///     // Akka.Hosting extension implementation
    ///     public static AkkaConfigurationBuilder WithDiscovery(
    ///         this AkkaConfigurationBuilder builder,
    ///         DiscoveryOptionBase discOption)
    ///     {
    ///         var setup = new DiscoverySetup();
    /// 
    ///         // gets called here
    ///         discOption.Apply(builder, setup);
    ///     }
    /// </code>
    /// </example>
    public interface IHoconOption
    {
        /// <summary>
        ///     The HOCON value of the HOCON path property
        /// </summary>
        string ConfigPath { get; }
            
        /// <summary>
        ///     The class <see cref="Type"/> that will be used for the HOCON class FQCN value 
        /// </summary>
        Type Class { get; }
            
        /// <summary>
        ///     Apply this option to the <paramref name="builder"/>
        /// </summary>
        /// <param name="builder">
        ///     The <see cref="AkkaConfigurationBuilder"/> to be applied to
        /// </param>
        /// <param name="setup">
        ///     The <see cref="Setup"/> to be applied to, if needed.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when <see cref="Apply"/> requires a setup but it was <c>null</c>
        /// </exception>
        void Apply(AkkaConfigurationBuilder builder, Setup? setup = null);
    }
}