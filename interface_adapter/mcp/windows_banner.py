# Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

"""Run the Windows activity banner in a subprocess owned by the MCP host."""

import asyncio
import logging
import subprocess

from computer_use.paths import native_driver

DEFAULT_AGENT_DISPLAY_NAME = "Jiuwen"

_LOG = logging.getLogger(__name__)


class WindowsExecutionBanner:
    def __init__(self, display_name):
        self._display_name = " ".join(display_name.splitlines()).strip() or DEFAULT_AGENT_DISPLAY_NAME
        self._active = None
        self._process = self._task = None
        self._closed = False

    def start(self):
        if not self._closed and (self._task is None or self._task.done()):
            self._task = asyncio.create_task(self._run())

    def show(self, execution_id, on_stop):
        if self._closed or (self._active is not None and self._active[0] == execution_id):
            return
        self._active = (execution_id, on_stop)
        self.start()
        self._send(execution_id)

    def hide(self, execution_id):
        if self._active is not None and self._active[0] == execution_id:
            self._active = None
            self._send(0)

    def _send(self, execution_id):
        if self._process is not None:
            self._write(str(execution_id))

    def _write(self, line):
        try:
            self._process.stdin.write((line + "\n").encode("utf-8", errors="replace"))
        except (OSError, RuntimeError):
            _LOG.debug("Windows activity banner closed its control pipe.", exc_info=True)

    async def _run(self):
        process = None
        try:
            driver = native_driver()
            process = await asyncio.create_subprocess_exec(
                driver["command"], *driver["args"], "--mcp-activity-banner",
                stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
            if not self._closed:
                self._process = process
                self._write(self._display_name)
                if self._active is not None:
                    self._send(self._active[0])
                while line := await process.stdout.readline():
                    execution_id = int(line)
                    if self._active is not None and execution_id == self._active[0]:
                        self._active[1](execution_id)
                if not self._closed:
                    _LOG.warning("Windows activity banner exited unexpectedly.")
        except (OSError, RuntimeError, ValueError) as error:
            _LOG.warning("Windows activity banner unavailable: %s", error)
        finally:
            self._process = None
            if process is not None:
                process.stdin.close()
                try:
                    await asyncio.wait_for(process.wait(), 2)
                except TimeoutError:
                    try:
                        process.kill()
                    except ProcessLookupError:
                        pass
                    await process.wait()

    async def close(self):
        self._closed = True
        self._active = None
        if self._process is not None:
            # EOF closes the UI and releases its hook.
            self._process.stdin.close()
        if self._task is not None:
            try:
                await asyncio.wait_for(asyncio.shield(self._task), 2)
            except TimeoutError:
                if self._process is not None and self._process.returncode is None:
                    try:
                        self._process.kill()
                    except ProcessLookupError:
                        pass
                await self._task
