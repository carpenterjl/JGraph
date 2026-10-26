using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace JGraph.Runtime.Native;

/// <summary>
/// The entry points jgrt.dll, the JGraph Runtime's native bridge, calls (compiler and SDK plan,
/// stage K10). jgrt starts .NET in a C program's process, loads this assembly into the default load
/// context and calls <see cref="Start"/>, which teaches the load context where the engine's other
/// assemblies and native libraries are and hands back the table of entry points the mcl functions
/// call. Everything after <see cref="Start"/> is <see cref="NativeCalls"/>'s, and the C++ shared library
/// API's <see cref="NativeCppCalls"/>'s (stage K12).
/// </summary>
/// <remarks>
/// This class names no engine type: its statics and <see cref="Start"/> must load before the
/// assemblies beside this one can be found.
/// </remarks>
internal static unsafe class NativeBridge
{
    private static string? _bin;

    /// <summary>The table jgrt's <c>JgrtManaged</c> is, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Table
    {
        public delegate* unmanaged<char**, int, int> InitializeApplication;
        public delegate* unmanaged<int> TerminateApplication;
        public delegate* unmanaged<char*, int, int> LogFileName;
        public delegate* unmanaged<char*, nint, nint, nint> OpenInstance;
        public delegate* unmanaged<nint, int> CloseInstance;
        public delegate* unmanaged<nint, char*, int, nint*, int, nint*, int> Feval;
        public delegate* unmanaged<int, char*, int, int> LastError;
        public delegate* unmanaged<nint, void> ReleaseValue;
        public delegate* unmanaged<nint, nint> CopyValue;
        public delegate* unmanaged<nint, ulong, char*, nint> GetProperty;
        public delegate* unmanaged<nint, ulong, char*, nint, nint> SetProperty;
        public delegate* unmanaged<nint, void> WaitForFigures;
        public delegate* unmanaged<int, char**, int, char**, int> CppStart;
        public delegate* unmanaged<char*, char**, nint> CppOpen;
        public delegate* unmanaged<nint, char*, nuint, byte*, nuint, nint, nint, int, int, nint, char**, ulong> CppCall;
        public delegate* unmanaged<ulong, int, int> CppCancel;
        public delegate* unmanaged<ulong, void> CppForget;
        public delegate* unmanaged<char*, char**, nint> PyOpen;
    }

    /// <summary>
    /// Readies the engine for jgrt: <paramref name="bin"/> is the runtime's engine folder and
    /// <paramref name="module"/> jgrt.dll itself, whose mx functions the conversions call. Answers 1.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int Start(Table* table, char* bin, nint module)
    {
        _bin = new string(bin);
        AssemblyLoadContext.Default.Resolving += static (context, name) =>
            _bin is { } folder && File.Exists(Path.Combine(folder, name.Name + ".dll"))
                ? context.LoadFromAssemblyPath(Path.Combine(folder, name.Name + ".dll"))
                : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += static (_, name) =>
        {
            string file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
            foreach (string candidate in new[] { Path.Combine(_bin!, file), Path.Combine(_bin!, "native", file) })
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
                {
                    return handle;
                }
            }

            return 0;
        };

        try
        {
            Bind(table, module);
            return 1;
        }
        catch (Exception failure)
        {
            // The boundary with C: an engine that cannot start says why, and jgrt answers false.
            Console.Error.WriteLine("The JGraph Runtime's engine could not start: " + failure.Message);
            return 0;
        }
    }

    // In a method of its own, so the engine's types are first touched after the resolvers are in place.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Bind(Table* table, nint module)
    {
        NativeArrays.Bind(module);
        table->InitializeApplication = &NativeCalls.InitializeApplication;
        table->TerminateApplication = &NativeCalls.TerminateApplication;
        table->LogFileName = &NativeCalls.LogFileName;
        table->OpenInstance = &NativeCalls.OpenInstance;
        table->CloseInstance = &NativeCalls.CloseInstance;
        table->Feval = &NativeCalls.Feval;
        table->LastError = &NativeCalls.LastError;
        table->ReleaseValue = &NativeCalls.ReleaseValue;
        table->CopyValue = &NativeCalls.CopyValue;
        table->GetProperty = &NativeCalls.GetProperty;
        table->SetProperty = &NativeCalls.SetProperty;
        table->WaitForFigures = &NativeCalls.WaitForFigures;
        table->CppStart = &NativeCppCalls.Start;
        table->CppOpen = &NativeCppCalls.Open;
        table->CppCall = &NativeCppCalls.Call;
        table->CppCancel = &NativeCppCalls.Cancel;
        table->CppForget = &NativeCppCalls.Forget;
        table->PyOpen = &NativeCppCalls.PyOpen;
    }
}
