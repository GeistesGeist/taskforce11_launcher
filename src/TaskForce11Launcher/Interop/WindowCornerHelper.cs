using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskForce11Launcher.Interop;

/// <summary>
/// Meldet randlose Fenster (WindowStyle="None") fuer die abgerundeten Ecken von Windows 11
/// an. Die Abrundung passiert im Compositor - DWM beschneidet das gesamte Fenster
/// einschliesslich unseres eckigen Inhalts, es braucht also keine eigene Clip-Geometrie.
/// Unter Windows 10 und aelter existiert das Attribut nicht; der Aufruf laeuft dort
/// wirkungslos durch.
/// </summary>
internal static class WindowCornerHelper
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void ApplyRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                var preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Reine Optik - darf den Fensterstart niemals aufhalten.
            }
        };
    }
}
