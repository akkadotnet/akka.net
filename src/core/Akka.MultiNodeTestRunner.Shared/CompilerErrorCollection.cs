using System;
using System.Collections.Generic;
using System.Reflection;

#if CORECLR
namespace System.CodeDom.Compiler
{
    public class CompilerErrorCollection : List<CompilerError>
    {
    }

    public class CompilerError
    {
        public string ErrorText { get; set; }
        public bool IsWarning { get; set; }
    }
}
#endif
