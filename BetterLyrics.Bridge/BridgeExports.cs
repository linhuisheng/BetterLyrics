using System;
using System.Runtime.InteropServices;

namespace BetterLyrics.Bridge
{
    public static class BridgeExports
    {
        [UnmanagedCallersOnly(EntryPoint = "BL_Initialize")]
        public static int Initialize()
        {
            BridgeState.EnsureConnected();
            return 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_StartMonitoring")]
        public static int StartMonitoring()
        {
            BridgeState.EnsureConnected();
            BridgeState.EnableEvents(true);
            return 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_StopMonitoring")]
        public static int StopMonitoring()
        {
            BridgeState.Stop();
            return 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_RegisterCallback")]
        public static unsafe int RegisterCallback(IntPtr callback)
        {
            BridgeState.Callback = (delegate* unmanaged<IntPtr, int, void>)callback;
            BridgeState.EnsureConnected();
            return 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_UnregisterCallback")]
        public static unsafe int UnregisterCallback()
        {
            BridgeState.Callback = null;
            BridgeState.EnableEvents(false);
            return 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_SearchLyrics")]
        public static BridgeBuffer SearchLyrics(IntPtr titlePtr, IntPtr artistPtr, IntPtr albumPtr)
        {
            var title = Marshal.PtrToStringUTF8(titlePtr) ?? string.Empty;
            var artist = Marshal.PtrToStringUTF8(artistPtr) ?? string.Empty;
            var album = Marshal.PtrToStringUTF8(albumPtr) ?? string.Empty;

            return BridgeState.SearchLyrics(title, artist, album);
        }

        [UnmanagedCallersOnly(EntryPoint = "BL_FreeBuffer")]
        public static void FreeBuffer(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeCoTaskMem(buffer);
        }
    }
}
