using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

public enum KeyModifier { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public class Hotkey : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event EventHandler HotkeyPressed;
    private int _id;
    private HwndSource _source;

    public Hotkey(Key key, KeyModifier modifier)
    {
        _id = GetHashCode();
        var helper = new WindowInteropHelper(Application.Current.MainWindow);
        _source = HwndSource.FromHwnd(helper.Handle);
        uint mod = (uint)modifier;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        RegisterHotKey(_source.Handle, _id, mod, vk);
    }

    public void Register() => _source.AddHook(HwndHook);
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && (int)wParam == _id)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }
    public void Dispose() { _source.RemoveHook(HwndHook); UnregisterHotKey(_source.Handle, _id); }
}