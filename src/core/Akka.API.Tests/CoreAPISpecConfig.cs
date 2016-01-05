using ApprovalTests.Reporters;
#if(DEBUG)
[assembly: UseReporter(typeof(DiffReporter), typeof(AllFailingTestsClipboardReporter))]
#else
[assembly: UseReporter(typeof(DiffReporter))]
#endif
