using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>
    /// Raw Win32 access to PHD2's window, menu and the Guiding Assistant dialog's controls.
    ///
    /// PHD2's Guiding Assistant is not exposed through PHD2's RPC API at all, so this is driven
    /// exactly the way a person would drive it: by finding its window, walking its Win32 menu
    /// (wxWidgets uses real HMENUs, which is why a UI-Automation Descendants search from the main
    /// window never finds "Tools" - a menu is not a child window), and posting BM_CLICK /
    /// WM_COMMAND messages to controls addressed by class, caption and position rather than by
    /// screen coordinates. None of this needs a rendered display, so it works with the desktop
    /// session locked or disconnected.
    /// </summary>
    internal static class NativeMethods {

        [DllImport("user32.dll")]
        public static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32.dll")]
        public static extern uint GetMenuItemID(IntPtr hMenu, int nPos);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetMenuStringW(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int nMaxCount, uint uFlag);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public const int GWL_STYLE = -16;

        // Button style bits (low nibble = button type). Unlike a caption, this is not translated.
        public const int BS_TYPEMASK = 0x0F;
        public const int BS_CHECKBOX = 0x02;
        public const int BS_AUTOCHECKBOX = 0x03;
        public const int BS_3STATE = 0x05;
        public const int BS_AUTO3STATE = 0x06;
        public const int BS_DEFPUSHBUTTON = 0x01;

        public const uint BM_CLICK = 0x00F5;
        public const uint BM_GETCHECK = 0x00F0;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_COMMAND = 0x0111;
        public const uint MF_BYPOSITION = 0x400;
    }
}
