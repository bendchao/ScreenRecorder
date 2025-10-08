using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenRecorder
{
    /// <summary>
    /// 键盘钩子类，用于捕获键盘输入事件
    /// </summary>
    public class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<KeyPressedEventArgs>? KeyPressed;

        private LowLevelKeyboardProc? proc;
        private IntPtr hookId = IntPtr.Zero;

        public void Start()
        {
            if (hookId == IntPtr.Zero)
            {
                proc = HookCallback;
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null && proc != null)
                    {
                        hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
                    }
                }
            }
        }

        public void Stop()
        {
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                // 获取修饰键状态
                Keys modifiers = Keys.None;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                    modifiers |= Keys.Shift;
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                    modifiers |= Keys.Control;
                if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt)
                    modifiers |= Keys.Alt;

                // 触发键盘按下事件，传递按键和修饰键信息
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(key, modifiers));
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }

    /// <summary>
    /// 键盘按键事件参数类
    /// </summary>
    public class KeyPressedEventArgs : EventArgs
    {
        public Keys Key { get; private set; }
        public Keys Modifiers { get; private set; }

        public KeyPressedEventArgs(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }
}