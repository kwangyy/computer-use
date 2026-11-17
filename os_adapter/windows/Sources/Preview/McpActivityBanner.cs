// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ComputerUse.Windows
{
    // A separate MCP-host child. Its stdin carries the display name once, then
    // execution IDs (zero keeps an idle marker), or "reset" to hide the marker.
    // EOF ends the MCP-owned child. Only an accepted Esc produces stdout.
    internal sealed class McpActivityBanner : IDisposable
    {
        const int KeyboardLowLevel = 13;
        const int Injected = 0x10;
        const int KeyDown = 0x100, KeyUp = 0x101, SysKeyDown = 0x104, SysKeyUp = 0x105;
        const float TopMargin = 12;
        static readonly int[] Modifiers = { (int)Keys.ShiftKey, (int)Keys.ControlKey, (int)Keys.Menu, (int)Keys.LWin, (int)Keys.RWin };
        readonly string displayName;
        readonly Control dispatcher;
        readonly DesktopDecorationWindow window;
        readonly KeyboardCallback keyboardCallback;
        IntPtr hook;
        Bitmap image;
        Point position;
        long execution;
        bool acceptsStop, escapeDown, consumedEscape;

        McpActivityBanner(string displayName, Control dispatcher)
        {
            this.displayName = displayName;
            this.dispatcher = dispatcher;
            keyboardCallback = OnKey;
            window = new DesktopDecorationWindow(DesktopDecorationWindow.BannerTitle, Paint, excludeFromCapture: true);
        }

        internal static int Run()
        {
            string displayName = Console.ReadLine();
            if (displayName == null)
                return 0;
            Native.InitializeDpiAwareness();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using (var dispatcher = new Control())
            using (var banner = new McpActivityBanner(displayName, dispatcher))
            {
                dispatcher.CreateControl();
                var reader = new Thread(banner.ReadCommands) { IsBackground = true };
                reader.Start();
                Application.Run();
            }
            return 0;
        }

        void ReadCommands()
        {
            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (line == "reset")
                    {
                        dispatcher.BeginInvoke((Action)Reset);
                        continue;
                    }
                    long id;
                    if (!long.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        break;
                    dispatcher.BeginInvoke((Action)(() => SetExecution(id)));
                }
            }
            catch (IOException)
            {
                // The MCP host can close its stream during shutdown.
            }
            catch (InvalidOperationException)
            {
                // The display thread has already closed.
            }
            finally
            {
                try { dispatcher.BeginInvoke((Action)Application.ExitThread); }
                catch (InvalidOperationException) { }
            }
        }

        void SetExecution(long id)
        {
            if (execution == id && image != null)
                return;
            execution = id;
            acceptsStop = false;
            bool hadHook = hook != IntPtr.Zero;
            if (hook == IntPtr.Zero)
            {
                escapeDown = IsDown((int)Keys.Escape);
                hook = SetWindowsHookEx(KeyboardLowLevel, keyboardCallback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    Console.Error.WriteLine("Windows activity banner could not enable Esc ({0}).", Marshal.GetLastWin32Error());
            }
            // Call completion and the next call retain the same visible HWND
            // and artwork. Idle Esc dismisses it without cancelling a call.
            if (image != null && hadHook == (hook != IntPtr.Zero))
            {
                Paint();
                return;
            }
            if (image != null)
                image.Dispose();
            var area = Screen.FromHandle(Native.GetForegroundWindow()).WorkingArea;
            int center = area.Left + area.Width / 2;
            float scale = DesktopDecorationWindow.Scale(new PointF(center, area.Top));
            image = ActivityBannerArtwork.Render(displayName, scale, area.Width / scale, hook != IntPtr.Zero);
            int y = area.Top + (int)Math.Round((TopMargin - ActivityBannerArtwork.ShadowMargin) * scale);
            position = new Point(center - image.Width / 2, y);
            Paint();
        }

        void Paint()
        {
            if (image != null && window.Present(image, position, 255, IntPtr.Zero))
                acceptsStop = hook != IntPtr.Zero;
        }

        void Reset()
        {
            execution = 0;
            acceptsStop = false;
            window.Hide();
            if (image != null)
            {
                image.Dispose();
                image = null;
            }
            UnhookIfHidden();
        }

        IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            // KBDLLHOOKSTRUCT starts with vkCode, scanCode and flags (DWORDs).
            if (code == 0 && Marshal.ReadInt32(data) == (int)Keys.Escape && (Marshal.ReadInt32(data, 8) & Injected) == 0)
            {
                int kind = message.ToInt32();
                if (kind == KeyUp || kind == SysKeyUp)
                {
                    escapeDown = false;
                    if (consumedEscape)
                    {
                        consumedEscape = false;
                        dispatcher.BeginInvoke((Action)UnhookIfHidden);
                        return new IntPtr(1);
                    }
                }
                else if (kind == KeyDown || kind == SysKeyDown)
                {
                    bool firstPress = !escapeDown;
                    escapeDown = true;
                    if (consumedEscape)
                        return new IntPtr(1);
                    if (kind == KeyDown && acceptsStop && firstPress && !Array.Exists(Modifiers, IsDown))
                    {
                        long id = execution;
                        acceptsStop = false;
                        consumedEscape = true;
                        // The hook returns immediately. Window work and the
                        // stop notification run on the message loop afterwards.
                        dispatcher.BeginInvoke((Action)(() => Stop(id)));
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        void Stop(long id)
        {
            if (execution == id)
                Reset();
            try
            {
                Console.WriteLine(id.ToString(CultureInfo.InvariantCulture));
                Console.Out.Flush();
            }
            catch (IOException)
            {
                Application.ExitThread();
            }
        }

        static bool IsDown(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }

        void UnhookIfHidden()
        {
            if (image == null && !consumedEscape)
                Unhook();
        }

        void Unhook()
        {
            if (hook == IntPtr.Zero)
                return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        public void Dispose()
        {
            Unhook();
            window.Dispose();
            if (image != null)
                image.Dispose();
        }

        delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int kind, KeyboardCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
    }
}
