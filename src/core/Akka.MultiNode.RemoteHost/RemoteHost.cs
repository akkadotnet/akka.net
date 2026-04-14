using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.MultiNode.RemoteHost
{
    public static class RemoteHost
    {
        #region Static functions
        public static (Process, Task) RunProcessAsync(
            Func<string[], Task<int>> action, 
            string[] args, 
            Action<RemoteHostOptions> configure = null,
            CancellationToken token = default)
            => Start(GetMethodInfo(action), args ?? throw new ArgumentNullException(nameof(args)), configure, token);

        private static (Process process, Task exitedTask) Start(
            MethodInfo method,
            string[] args,
            Action<RemoteHostOptions> configure,
            CancellationToken token = default)
        {
            var process = new Process();
            RemoteHostOptions options;
            try
            {
                options = new RemoteHostOptions(process.StartInfo);
                ConfigureProcessStartInfoForMethodInvocation(method, args, options.StartInfo);
                configure?.Invoke(options);
            }
            catch
            {
                process.Dispose();
                throw;
            }
            
            var tcs = new TaskCompletionSource<bool>();
            if (token != default)
            {
                token.Register(() =>
                {
                    try
                    {
                        process.Kill();
                    } catch{}
                });
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_1, _2) =>
            {
                options.OnExit(process);

                tcs.SetResult(true);
                process.Dispose();
            };

            if (options.OutputDataReceived != null)
            {
                process.OutputDataReceived += options.OutputDataReceived;
                options.StartInfo.RedirectStandardOutput = true;
            }

            if (options.ErrorDataReceived != null)
            {
                process.ErrorDataReceived += options.ErrorDataReceived;
                options.StartInfo.RedirectStandardError = true;
            }

            process.Start();
                
            if (options.OutputDataReceived != null)
            {
                process.BeginOutputReadLine();
            }

            if (options.ErrorDataReceived != null)
            {
                process.BeginErrorReadLine();
            }

            return (process, tcs.Task);            
        }

        private static void ConfigureProcessStartInfoForMethodInvocation(
            MethodInfo method,
            string[] args,
            ProcessStartInfo psi)
        {
            if (method.ReturnType != typeof(void) &&
                method.ReturnType != typeof(int) &&
                method.ReturnType != typeof(Task<int>))
            {
                throw new ArgumentException("method has an invalid return type", nameof(method));
            }
            if (method.GetParameters().Length > 1)
            {
                throw new ArgumentException("method has more than one argument argument", nameof(method));
            }
            if (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType != typeof(string[]))
            {
                throw new ArgumentException("method has non string[] argument", nameof(method));
            }

            // If we need the host (if it exists), use it, otherwise target the console app directly.
            var t = method.DeclaringType;
            var a = t.GetTypeInfo().Assembly;
            var programArgs = Utils.Paste(new [] { a.FullName, t.FullName, method.Name });
            var functionArgs = Utils.Paste(args);
            var fullArgs = HostArguments + " " + " " + programArgs + " " + functionArgs;

            psi.FileName = HostFilename;
            psi.Arguments = fullArgs;
        }

        private static MethodInfo GetMethodInfo(Delegate d)
        {
            // RemoteInvoke doesn't support marshaling state on classes associated with
            // the delegate supplied (often a display class of a lambda).  If such fields
            // are used, odd errors result, e.g. NullReferenceExceptions during the remote
            // execution.  Try to ward off the common cases by proactively failing early
            // if it looks like such fields are needed.
            if (d.Target != null)
            {
                // The only fields on the type should be compiler-defined (any fields of the compiler's own
                // making generally include '<' and '>', as those are invalid in C# source).  Note that this logic
                // may need to be revised in the future as the compiler changes, as this relies on the specifics of
                // actually how the compiler handles lifted fields for lambdas.
                var targetType = d.Target.GetType();
                var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var fi in fields)
                {
                    if (fi.Name.IndexOf('<') == -1)
                    {
                        throw new ArgumentException($"Field marshaling is not supported by {nameof(RemoteHost)}: {fi.Name}", "method");
                    }
                }
            }

            return d.GetMethodInfo();
        }

        static RemoteHost()
        {
            HostFilename = "dotnet";
            
            var execFunctionAssembly = typeof(RemoteHost).Assembly.Location;
            var entryAssemblyWithoutExtension = Path.Combine(
                Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location));
            var appArguments = GetApplicationArguments();

            var runtimeConfigFile = GetApplicationArgument(appArguments, "--runtimeconfig");
            if (runtimeConfigFile == null)
            {
                runtimeConfigFile = entryAssemblyWithoutExtension + ".runtimeconfig.json";
            }

            var depsFile = GetApplicationArgument(appArguments, "--depsfile");
            if (depsFile == null)
            {
                depsFile = entryAssemblyWithoutExtension + ".deps.json";
            }

            HostArguments = Utils.Paste(new [] { "exec", "--runtimeconfig", runtimeConfigFile, "--depsfile", depsFile, execFunctionAssembly });
        }

        private static string GetApplicationArgument(string[] arguments, string name)
        {
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i].ToLowerInvariant() == name)
                {
                    return arguments[i + 1];
                }
            }
            return null;
        }

        private static string[] GetOSXCommandLineArguments()
        {
            // The following logic is based on https://gist.github.com/nonowarn/770696
            // Set up the mib array and the query for process maximum args size
            var mib = new int[3];
            var mibLength = 2;
            mib[0] = MACOS_CTL_KERN;
            mib[1] = MACOS_KERN_ARGMAX;

            var size = IntPtr.Size / 2;
            var argmax = 0;
            var argv = new List<string>();

            var mibHandle = GCHandle.Alloc(mib, GCHandleType.Pinned);
            try
            {
                var mibPtr = mibHandle.AddrOfPinnedObject();

                // Get the process args size
                SysCtl(mibPtr, mibLength, ref argmax, ref size, IntPtr.Zero, 0);

                // Get the PID so we can query this process' args
                var pid = Process.GetCurrentProcess().Id;

                // Now read the process args into the allocated space
                var procargs = Marshal.AllocHGlobal(argmax);
                try
                {
                    mib[0] = MACOS_CTL_KERN;
                    mib[1] = MACOS_KERN_PROCARGS2;
                    mib[2] = pid;
                    mibLength = 3;

                    SysCtl(mibPtr, mibLength, procargs, ref argmax, IntPtr.Zero, 0);

                    // The memory block we're reading is a series of null-terminated strings
                    // that looks something like this:
                    //
                    // | argc      | <int> is always 4 bytes long even on 64bit architectures
                    // | exec_path | ... \0\0\0\0 * ?
                    // | argv[0]   | ... \0
                    // | argv[1]   | ... \0
                    // | argv[2]   | ... \0
                    //   ...
                    // | env[0]    | ... \0  (VALUE = SOMETHING\0)

                    // Read argc
                    var argc = Marshal.ReadInt32(procargs);

                    // Skip over argc
                    var argvPtr = IntPtr.Add(procargs, sizeof(int));

                    // Skip over exec_path
                    var offset = 0;
                    while (Marshal.ReadByte(argvPtr, offset) != 0) { offset++; }
                    while (Marshal.ReadByte(argvPtr, offset) == 0) { offset++; }
                    argvPtr = IntPtr.Add(argvPtr, offset);

                    // Start reading argv
                    for (var i = 0; i < argc; i++)
                    {
                        offset = 0;
                        // Keep reading bytes until we find a null-terminated string
                        while (Marshal.ReadByte(argvPtr, offset) != 0) { offset++; }
                        var arg = Marshal.PtrToStringAnsi(argvPtr, offset);
                        argv.Add(arg);

                        // Move pointer to the start of the next arg (= currentArg + \0)
                        argvPtr = IntPtr.Add(argvPtr, offset + sizeof(byte));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(procargs);
                }
            }
            finally
            {
                mibHandle.Free();
            }

            return argv.ToArray();
        }

        private static string[] GetApplicationArguments()
        {
            // Environment.GetCommandLineArgs doesn't include arguments passed to the runtime.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return File.ReadAllText($"/proc/{Process.GetCurrentProcess().Id}/cmdline").Split(new[] { '\0' });
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ptr = GetCommandLine();
                var commandLine = Marshal.PtrToStringAuto(ptr);
                return CommandLineToArgs(commandLine);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return GetOSXCommandLineArguments();
            }

            throw new PlatformNotSupportedException($"{nameof(GetApplicationArguments)} is unsupported on this platform");
        }

        private const int MACOS_CTL_KERN = 1;
        private const int MACOS_KERN_ARGMAX = 8;
        private const int MACOS_KERN_PROCARGS2 = 49;

        [DllImport("libc",
            EntryPoint = "sysctl",
            CallingConvention = CallingConvention.Cdecl,
            CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int SysCtl(IntPtr mib, int mibLength, ref int oldp, ref int oldlenp, IntPtr newp, int newlenp);

        [DllImport("libc",
            EntryPoint = "sysctl",
            CallingConvention = CallingConvention.Cdecl,
            CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int SysCtl(IntPtr mib, int mibLength, IntPtr oldp, ref int oldlenp, IntPtr newp, int newlenp);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetCommandLine();

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        public static string[] CommandLineToArgs(string commandLine)
        {
            var argv = CommandLineToArgvW(commandLine, out var argc);
            if (argv == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception();
            try
            {
                var args = new string[argc];
                for (var i = 0; i < args.Length; i++)
                {
                    var p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    args[i] = Marshal.PtrToStringUni(p);
                }

                return args;
            }
            finally
            {
                Marshal.FreeHGlobal(argv);
            }
        }

        private static readonly string HostFilename;
        private static readonly string HostArguments;

        #endregion
        
        public const string CommandName = "remotehost";
        public static int UnhandledExceptionExitCode = 128 + 6; // SIGABRT exit code

        public static bool IsExecFunctionCommand(string[] args)
            => args.Length >= 1 && args[0] == CommandName;

        /// <summary>
        /// Provides an entry point in a new process that will load a specified method and invoke it.
        /// </summary>
        public static class Program
        {
            public static int Main(string[] args)
            {
                var argsLength = args.Length;
                var argIdx = 0;
                // Strip CommandName.
                if (argsLength > 0 && args[0] == CommandName)
                {
                    argsLength--;
                    argIdx++;
                }

                // The program expects to be passed the target assembly name to load, the type
                // from that assembly to find, and the method from that assembly to invoke.
                // Any additional arguments are passed as strings to the method.
                if (argsLength < 3)
                {
                    Console.Error.WriteLine("Usage: {0} assemblyName typeName methodName [additionalArgs]", typeof(Program).GetTypeInfo().Assembly.GetName().Name);
                    Environment.Exit(-1);
                    return -1;
                }

                var assemblyName = args[argIdx++];
                var typeName = args[argIdx++];
                var methodName = args[argIdx++];
                var additionalArgs = args.SubArray(3);

                // Load the specified assembly, type, and method, then invoke the method.
                // The program's exit code is the return value of the invoked method.
                object instance = null;
                var exitCode = 0;
                try
                {
                    // Create the class if necessary
                    var a = Assembly.Load(new AssemblyName(assemblyName));
                    var t = a.GetType(typeName);
                    var mi = t.GetTypeInfo().GetDeclaredMethod(methodName);
                    if (!mi.IsStatic)
                    {
                        instance = Activator.CreateInstance(t);
                    }

                    // Invoke the method
                    object result;
                    if (mi.GetParameters().Length == 0)
                    {
                        result = mi.Invoke(instance, null);
                    }
                    else
                    {
                        result = mi.Invoke(instance, new object[] { additionalArgs });
                    }

                    if (result is Task<int> task)
                    {
                        exitCode = task.GetAwaiter().GetResult();
                    }
                    else if (result is int exit)
                    {
                        exitCode = exit;
                    }
                }
                catch (Exception exc)
                {
                    if (exc is TargetInvocationException && exc.InnerException != null)
                        exc = exc.InnerException;

                    Console.Error.Write("Unhandled exception: ");
                    Console.Error.WriteLine(exc);

                    exitCode = UnhandledExceptionExitCode;
                }
                finally
                {
                    (instance as IDisposable)?.Dispose();
                }

                return exitCode;
            }
        }

        private static T[] SubArray<T>(this T[] data, int index)
        {
            var length = data.Length - index;
            if (length == 0)
            {
                return Array.Empty<T>();
            }
            var result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }
    }
}