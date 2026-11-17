// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

namespace ComputerUse.Windows
{
    // Session authority for the global input channel, independent of window
    // activation and object-directed UIA/message operations.
    internal interface INativeGlobalKeyboardAuthority
    {
        void RequireGlobalInput();
    }

    internal static class NativeGlobalInput
    {
        internal static void Require(bool allowed)
        {
            if (!allowed)
                throw Failure.UserAssistance("permission_required", "this route requires global mouse or keyboard input",
                    "Ask the user to authorize global input in the host/session, or complete this step manually. " +
                    "Do not retry this blocked route until authorization changes; then read the target again before continuing.");
        }
    }
}
