using System;
using System.Runtime.InteropServices;

namespace BetterLyrics.HeadlessHost
{
    internal static class WindowsAppSdkBootstrapper
    {
        private const uint WindowsAppSdkVersion_1_8 = 0x00010008;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            var hr = MddBootstrapInitialize(WindowsAppSdkVersion_1_8, null, IntPtr.Zero);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            _initialized = true;
        }

        public static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            MddBootstrapShutdown();
            _initialized = false;
        }

        [DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode)]
        private static extern int MddBootstrapInitialize(uint majorMinorVersion, string? versionTag, IntPtr options);

        [DllImport("Microsoft.WindowsAppRuntime.dll")]
        private static extern void MddBootstrapShutdown();
    }
}
