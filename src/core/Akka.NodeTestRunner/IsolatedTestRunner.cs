// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ITestRunner.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;

namespace Akka.NodeTestRunner
{
    public class IsolatedTestRunner : IDisposable, ITestRunner
    {
        private readonly TestRunner _runner;
        private AppDomain _domain;

        public IsolatedTestRunner()
        {
            var type = typeof (TestRunner);
            var friendlyName = string.Format("TestRunner:{0}", Guid.NewGuid());

            var appDomainSetup = AppDomain.CurrentDomain.SetupInformation;
            appDomainSetup.DisallowBindingRedirects = true;
            appDomainSetup.ConfigurationFile = string.Empty;

            _domain = AppDomain.CreateDomain(
                                             friendlyName,
                AppDomain.CurrentDomain.Evidence,
                appDomainSetup);

            _runner = (TestRunner) _domain.CreateInstanceAndUnwrap(type.Assembly.FullName, type.FullName);
        }

        public void Dispose()
        {
            if (_domain != null)
            {
                AppDomain.Unload(_domain);
                _domain = null;
            }
        }

        public void Run()
        {
            _runner.Run();
        }

        public void InitializeWithTestArgs(string[] testArgs)
        {
            _runner.InitializeWithTestArgs(testArgs);
        }

        public bool NoErrors
        {
            get { return _runner.NoErrors; }
        }
    }
}