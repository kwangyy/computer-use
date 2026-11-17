// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;

namespace ComputerUse.Windows
{
    internal static class NativeInputAccessTests
    {
        static Failure IntegrityFailure()
        {
            try { NativeInputAccess.RequireIntegrity(8192, 12288, "foreground window", 27396); }
            catch (Failure error) { return error; }
            throw new Exception("A known higher-integrity receiver must retain the UIPI failure");
        }

        static Failure GlobalInputFailure()
        {
            try { NativeGlobalInput.Require(false); }
            catch (Failure error) { return error; }
            throw new Exception("The existing host authorization boundary must remain intact");
        }

        static void Check(bool condition, string detail)
        {
            if (!condition) throw new Exception(detail);
        }

        static void Assistance(Failure error, string code, string remedy)
        {
            Check(error.Code == code, "Guidance must not change failure categories or route recovery");
            Check(error.Message.Contains("User assistance required: Notify the user now."),
                  "The delivered detail must promptly request user assistance, not hide it in Failure.Next: " + error.Message);
            Check(error.Message.Contains(remedy) && error.Message.Contains("read the target again"),
                  "The delivered detail must explain the concrete user action and safe continuation");
        }

        static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Environment.ExitCode = 1;
            }
        }

        static void Run(string[] args)
        {
            var integrity = IntegrityFailure();
            var desktop = Failure.DesktopUnavailable("capture requires the unlocked interactive desktop");
            var global = GlobalInputFailure();
            Assistance(integrity, "input_unavailable", "administrator");
            Check(integrity.Message.Contains("PID 27396") && integrity.Message.Contains("UIPI") &&
                  integrity.Message.Contains("manually") && integrity.Message.Contains("Do not retry"),
                  "UIPI guidance must preserve the target evidence and offer human completion without futile retries");
            Assistance(desktop, "desktop_unavailable", "UAC/security prompt manually");
            Assistance(global, "permission_required", "authorize global input");

            // The guidance is not an additional admission rule.
            NativeInputAccess.RequireIntegrity(8192, 8192, "same-level window", 42);
            NativeInputAccess.RequireIntegrity(12288, 8192, "lower-level window", 42);
            NativeGlobalInput.Require(true);
            foreach (string code in new[] { "input_unavailable", "observation_unavailable", "execution_unknown" })
                Check(new Failure(code, "ordinary provider failure").Message == "ordinary provider failure",
                      "Unknown input/provider failures must not be classified as permission problems");

            if (args.Length == 1 && args[0] == "--json")
                Console.WriteLine(Wire.Json(Wire.Obj("integrity", integrity.Message, "desktop", desktop.Message,
                                                    "globalInput", global.Message)));
            else
                Console.WriteLine("NativeInputAccessTests: passed (permission assistance and unchanged admission)");
        }
    }
}
