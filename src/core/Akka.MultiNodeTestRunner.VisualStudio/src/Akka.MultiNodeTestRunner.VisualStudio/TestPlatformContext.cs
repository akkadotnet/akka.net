namespace Xunit.Runner.VisualStudio
{
    /// <summary>
    /// Provides contextual information on a test run/discovery based on runsettings
    /// or the invocation (execution, discovery).
    /// </summary>
    public struct TestPlatformContext
    {
        /// <summary>
        /// Indicates if VSTestCase object must have FileName or LineNumber information.
        /// </summary>
        public bool RequireSourceInformation { get; set; }

        /// <summary>
        /// Indicates if XunitTestCase needs to be serialized in VSTestCase instance.
        /// </summary>
        /// <remarks>XunitTestCase must be serialized if the test case is sent back for xunit to execute.
        /// E.g. in case of Test Discovery from IDE, user can later select and try to run the test.
        /// </remarks>
        public bool RequireXunitTestProperty { get; set; }
    }
}