namespace Xunit.Runner.VisualStudio
{
    public static class Constants
    {
#if NET452
        public const string ExecutorUri = "executor://xunit/VsTestRunner2/net";
#elif NETCOREAPP
        public const string ExecutorUri = "executor://xunit/VsTestRunner2/netcoreapp";
#else
#error Unknown target platform
#endif
    }
}
