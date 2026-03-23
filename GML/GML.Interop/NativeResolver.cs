using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GML.Interop
{
    internal static class NativeResolver
    {
        static NativeResolver()
        {
            // Intercept P/Invoke loads for this assembly.
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), Resolve);
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "GML_Library.dll", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero; // default resolution for anything else

            string baseDir = AppContext.BaseDirectory;
            // Candidate locations (relative to WPF output and repo layout)
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "GML_Library.dll"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "GML_Library", "x64", "Debug", "GML_Library.dll")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "x64", "Debug", "GML_Library.dll")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "GML_Library", "x64", "Release", "GML_Library.dll")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "x64", "Release", "GML_Library.dll")),
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    try { return NativeLibrary.Load(p); } catch { /* try next */ }
                }
            }
            return IntPtr.Zero; // let runtime throw the DllNotFoundException with default search
        }
    }
}
