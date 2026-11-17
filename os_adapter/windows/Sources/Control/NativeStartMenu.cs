// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ComputerUse.Windows
{
    internal static class NativeStartMenu
    {
        internal const string StartHost = @"SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe";
        internal const string SearchHost = @"SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchHost.exe";
        internal const string SearchApp = @"SystemApps\Microsoft.Windows.Search_cw5n1h2txyewy\SearchApp.exe";

        internal static bool HasVisibleForeground()
        {
            IntPtr window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero || !Native.IsWindowVisible(window) || Native.IsIconic(window))
                return false;
            uint pid = Native.PID(window);
            if (pid == 0 || pid > Int32.MaxValue)
                return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    // Recent Windows builds use SearchHost for both Start and Search.
                    string host = process.ProcessName;
                    string image = host == "StartMenuExperienceHost" ? StartHost
                                   : host == "SearchHost" ? SearchHost : host == "SearchApp" ? SearchApp : null;
                    if (image == null)
                        return false;
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (String.IsNullOrEmpty(windows) ||
                        !String.Equals(process.MainModule.FileName, Path.Combine(windows, image),
                                       StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            catch (ArgumentException)
            {
                return false; // The candidate process exited while being sampled.
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }

            int cloaked;
            Native.RECT bounds;
            return Native.DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked == 0 &&
                   Native.GetWindowRect(window, out bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top &&
                   Native.GetForegroundWindow() == window && Native.PID(window) == pid;
        }

        internal static bool WaitForVisibleForeground(Func<bool> visible, Request request)
        {
            double until = Math.Min(request.Deadline, Wire.Now + 800);
            while (true)
            {
                request.Check();
                if (visible())
                    return true;
                int remaining = (int)(until - Wire.Now);
                if (remaining <= 0)
                    return false;
                request.Wait(Math.Min(20, remaining));
            }
        }
    }
}
