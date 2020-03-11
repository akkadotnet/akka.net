//-----------------------------------------------------------------------
// <copyright file="TestKitSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.TestKit
{
    /// <summary>
    /// Contains settings to be used when writing tests with TestKit.
    /// </summary>
    public class TestKitSettings : IExtension
    {
        private readonly TimeSpan _defaultTimeout;
        private readonly TimeSpan _singleExpectDefault;
        private readonly TimeSpan _testEventFilterLeeway;
        private readonly double _timefactor;
        private readonly bool _logTestKitCalls;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestKitSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration to use when setting up the tests.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when a negative value is given for the <c>akka.test.timefactor</c> configuration item.
        /// </exception>
        public TestKitSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TestKitSettings>();

            _defaultTimeout = config.GetTimeSpan("akka.test.default-timeout", null, allowInfinite:false);
            _singleExpectDefault = config.GetTimeSpan("akka.test.single-expect-default", null, allowInfinite: false);
            _testEventFilterLeeway = config.GetTimeSpan("akka.test.filter-leeway", null, allowInfinite: false);
            _timefactor = config.GetDouble("akka.test.timefactor", 0);
            _logTestKitCalls = config.GetBoolean("akka.test.testkit.debug", false);

            if(_timefactor <= 0)
                throw new ConfigurationException($@"Expected a positive value for ""akka.test.timefactor"" but found {_timefactor}");
        }


        /// <summary>
        /// Gets the default timeout as specified in the setting akka.test.default-timeout.
        /// Typically used for Ask-timeouts. It is always finite.
        /// </summary>
        public TimeSpan DefaultTimeout { get { return _defaultTimeout; } }

        /// <summary>Gets the config value "akka.test.single-expect-default". It is always finite.</summary>
        public TimeSpan SingleExpectDefault { get { return _singleExpectDefault; } }

        /// <summary>Gets the config value "akka.test.filter-leeway".  It is always finite.</summary>
        public TimeSpan TestEventFilterLeeway { get { return _testEventFilterLeeway; } }

        /// <summary>
        /// Gets the timefactor by which all values are scaled by.
        /// <para>
        /// The tight timeouts you use during testing on your lightning-fast notebook 
        /// will invariably lead to spurious test failures on the heavily loaded 
        /// CI server. To account for this situation, all maximum durations are 
        /// internally scaled by this factor, which defaults to 1. To change this value
        /// set configuration "akka.test.timefactor" to a positive double/integer.</para>
        /// <para>
        /// You can scale other durations with the same factor by using the
        /// <see cref="TestKitBase.Dilated">Testkit.Dilated</see>
        /// </para>
        /// </summary>
        public double TestTimeFactor { get { return _timefactor; } }

        /// <summary>
        /// If set to <c>true</c> calls to testkit will be logged.
        /// This is enabled by setting the configuration value "akka.test.testkit.debug" to a true.
        /// </summary>
        public bool LogTestKitCalls { get { return _logTestKitCalls; } }
    }
}
