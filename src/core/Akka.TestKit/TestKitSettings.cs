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
        private readonly Config _config;

        public TestKitSettings(Config config)
        {
            _config = config;
        }


        /// <summary>
        /// Gets the default timeout as specified in the setting akka.test.default-timeout.
        /// If not specified, a default value of 1 second will be returned.
        /// </summary>
        public TimeSpan DefaultTimeout
        {
            get
            {
                return _config.GetMillisDuration("akka.test.default-timeout", TimeSpan.FromSeconds(5));
            }
        }

        public double TestTimeFactor
        {
            get
            {
                var factor = _config.GetDouble("akka.test.timefactor");
                if (double.IsInfinity(factor) || factor > 0.0)
                {
                    throw new Exception("akka.test.timefactor must be positive finite double");
                }

                return factor;
            }
        }

        public TimeSpan SingleExpectDefaultTimeout
        {
            get
            {
                return _config.GetMillisDuration("akka.test.single-expect-default", TimeSpan.FromSeconds(5));
            }
        }

        public TimeSpan TestEventFilterLeeway
        {
            get
            {
                return _config.GetMillisDuration("akka.test.filter-leeway", TimeSpan.FromSeconds(5));
            }
        }
    }
}