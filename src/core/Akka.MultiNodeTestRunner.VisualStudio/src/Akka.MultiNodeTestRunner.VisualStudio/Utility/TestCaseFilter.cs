using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Xunit.Runner.VisualStudio
{
    public class TestCaseFilter
    {
        const string DisplayNameString = "DisplayName";
        const string FullyQualifiedNameString = "FullyQualifiedName";

        readonly HashSet<string> knownTraits;
        List<string> supportedPropertyNames;
        ITestCaseFilterExpression filterExpression;
        readonly bool successfullyGotFilter;

        public TestCaseFilter(IRunContext runContext, LoggerHelper logger, string assemblyFileName, HashSet<string> knownTraits)
        {
            this.knownTraits = knownTraits;
            supportedPropertyNames = GetSupportedPropertyNames();

            successfullyGotFilter = GetTestCaseFilterExpression(runContext, logger, assemblyFileName, out filterExpression);
        }

        public bool MatchTestCase(TestCase testCase)
        {
            if (!successfullyGotFilter)
            {
                // Had an error while getting filter, match no testcase to ensure discovered test list is empty
                return false;
            }
            else if (filterExpression == null)
            {
                // No filter specified, keep every testcase
                return true;
            }

            return filterExpression.MatchTestCase(testCase, (p) => PropertyProvider(testCase, p));
        }

        public object PropertyProvider(TestCase testCase, string name)
        {
            // Traits filtering
            if (knownTraits.Contains(name))
            {
                var result = new List<string>();

                foreach (var trait in GetTraits(testCase))
                    if (string.Equals(trait.Key, name, StringComparison.OrdinalIgnoreCase))
                        result.Add(trait.Value);

                if (result.Count > 0)
                    return result.ToArray();
            }
            else
            {
                // Handle the displayName and fullyQualifierNames independently
                if (string.Equals(name, FullyQualifiedNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.FullyQualifiedName;
                if (string.Equals(name, DisplayNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.DisplayName;
            }

            return null;
        }

        bool GetTestCaseFilterExpression(IRunContext runContext, LoggerHelper logger, string assemblyFileName, out ITestCaseFilterExpression filter)
        {
            filter = null;

            try
            {
                filter = runContext.GetTestCaseFilter(supportedPropertyNames, null);
                return true;
            }
            catch (TestPlatformFormatException e)
            {
                logger.LogWarning("{0}: Exception filtering tests: {1}", Path.GetFileNameWithoutExtension(assemblyFileName), e.Message);
                return false;
            }
        }

        List<string> GetSupportedPropertyNames()
        {
            // Returns the set of well-known property names usually used with the Test Plugins (Used Test Traits + DisplayName + FullyQualifiedName)
            if (supportedPropertyNames == null)
            {
                supportedPropertyNames = knownTraits.ToList();
                supportedPropertyNames.Add(DisplayNameString);
                supportedPropertyNames.Add(FullyQualifiedNameString);
            }

            return supportedPropertyNames;
        }

        static IEnumerable<KeyValuePair<string, string>> GetTraits(TestCase testCase)
        {
            var traitProperty = TestProperty.Find("TestObject.Traits");
            if (traitProperty != null)
                return testCase.GetPropertyValue(traitProperty, Enumerable.Empty<KeyValuePair<string, string>>().ToArray());

            return Enumerable.Empty<KeyValuePair<string, string>>();
        }
    }
}
