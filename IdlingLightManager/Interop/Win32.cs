using System.Runtime.InteropServices;

namespace IdlingLightManager.Interop;

/// <summary>
/// P/Invoke declarations and related structures for the Win32 API.
/// </summary>
#pragma warning disable CA1060 // Suppressed: this class is intentionally the Win32 interop container
internal static class Win32
{
    /// <summary>
    /// Win32 structure that holds the last mouse/keyboard input information.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
#pragma warning disable SA1307, IDE1006 // Field names are dictated by the Win32 API
        /// <summary>Size of the structure in bytes. Must be set with <c>Marshal.SizeOf</c> before the call.</summary>
        [MarshalAs(UnmanagedType.U4)]
        public uint cbSize;

        /// <summary><c>TickCount</c> (milliseconds) at the time of the last input.</summary>
        [MarshalAs(UnmanagedType.U4)]
        public uint dwTime;
#pragma warning restore SA1307, IDE1006
    }

    /// <summary>
    /// Retrieves the time of the last mouse/keyboard input.
    /// </summary>
    /// <param name="plii">Reference to the <see cref="LASTINPUTINFO"/> structure that receives the result.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// Gets the time elapsed since the last input.
    /// </summary>
    /// <returns>The elapsed time since the last input, or <see cref="TimeSpan.Zero"/> if it could not be retrieved.</returns>
    internal static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // The uint cast handles the 49.7-day TickCount wraparound correctly
        var elapsed = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(elapsed);
    }
}
#pragma warning restore CA1060
