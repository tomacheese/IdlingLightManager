using System.Runtime.InteropServices;

namespace IdlingLightManager.Interop;

/// <summary>
/// Win32 API の P/Invoke 宣言および関連構造体。
/// </summary>
#pragma warning disable CA1060 // Win32 interop クラスとして適切な配置のため抑制
internal static class Win32
{
    /// <summary>
    /// マウス・キーボードの最終入力情報を格納する Win32 構造体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
#pragma warning disable SA1307 // Win32 API 由来のフィールド名のため変更不可
        /// <summary>構造体のバイトサイズ。呼び出し前に Marshal.SizeOf で設定する必要がある。</summary>
        [MarshalAs(UnmanagedType.U4)]
        public uint cbSize;

        /// <summary>最終入力時の TickCount（ミリ秒）。</summary>
        [MarshalAs(UnmanagedType.U4)]
        public uint dwTime;
#pragma warning restore SA1307
    }

    /// <summary>
    /// マウス・キーボードの最終入力情報を取得する。
    /// </summary>
    /// <param name="plii">取得結果を格納する <see cref="LASTINPUTINFO"/> 構造体への参照。</param>
    /// <returns>成功した場合は <c>true</c>、失敗した場合は <c>false</c>。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// 最終入力からの経過時間を取得する。
    /// </summary>
    /// <returns>最終入力からの経過時間。取得失敗時は <see cref="TimeSpan.Zero"/>。</returns>
    internal static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // uint キャストで TickCount の 49.7 日折り返しを正しく処理する
        var elapsed = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(elapsed);
    }
}
#pragma warning restore CA1060
