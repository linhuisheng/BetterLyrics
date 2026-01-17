using System;
using System.Runtime.InteropServices;

namespace BetterLyrics.Bridge
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BridgeBuffer
    {
        public readonly IntPtr Data;
        public readonly int Length;

        public BridgeBuffer(IntPtr data, int length)
        {
            Data = data;
            Length = length;
        }
    }
}
