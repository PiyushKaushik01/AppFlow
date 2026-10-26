using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AppFlow;

internal static class WindowTheme
{
    private const int ImmersiveDarkMode = 20;

    public static void ApplyDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var enabled = 1;
                if (DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, ImmersiveDarkMode - 1, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
