"""python-for-android service entrypoint used by the MFA resource workflow example.

The Android host passes a JSON object through PYTHON_SERVICE_ARGUMENT. Resource projects
may replace this adapter with their own service entrypoint when their Agent needs custom
initialization. The desktop Agent command in interface.json is left unchanged.
"""

from __future__ import annotations

import json
import os
import runpy
import sys
import threading
from pathlib import Path
from typing import Any


def _service_argument() -> dict[str, Any]:
    raw = os.environ.get("PYTHON_SERVICE_ARGUMENT", "")
    if not raw:
        raise RuntimeError("PYTHON_SERVICE_ARGUMENT is empty")
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise TypeError("PYTHON_SERVICE_ARGUMENT must contain a JSON object")
    return value


def _unquote(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
        return value[1:-1]
    return value


def _resolve_script(config: dict[str, Any], root: Path) -> tuple[Path, list[str]]:
    arguments = [_unquote(str(item)) for item in config.get("child_args") or []]
    configured = str(config.get("entrypoint") or "").strip()
    program = _unquote(str(config.get("child_exec") or "").strip())

    if configured:
        script = configured
        trailing_arguments: list[str] = []
    elif program.lower().endswith((".py", ".pyc")):
        script = program
        trailing_arguments = arguments
    else:
        script_index = next(
            (index for index in range(len(arguments) - 1, -1, -1)
             if arguments[index].lower().endswith((".py", ".pyc"))),
            -1,
        )
        if script_index < 0:
            raise RuntimeError(
                "No Python script was found in interface agent.child_exec/child_args. "
                "Set the MFA_ANDROID_PYTHON_ENTRYPOINT repository variable."
            )
        script = arguments[script_index]
        trailing_arguments = arguments[script_index + 1:]

    script_path = Path(script)
    if not script_path.is_absolute():
        script_path = root / script_path
    script_path = script_path.resolve()
    if not script_path.is_file():
        raise FileNotFoundError(f"Android Agent entrypoint does not exist: {script_path}")
    return script_path, trailing_arguments


class _AndroidOutputBridge:
    """Tee complete output lines to MFA's main Android process."""

    def __init__(self, target: Any, config: dict[str, Any]) -> None:
        self._target = target
        self._buffer = ""
        self._lock = threading.Lock()
        self._action = str(config.get("output_action") or "")
        self._package = str(config.get("output_package") or "")

        from jnius import autoclass

        self._intent_type = autoclass("android.content.Intent")
        self._string_type = autoclass("java.lang.String")
        service_type = autoclass("org.kivy.android.PythonService")
        self._context = service_type.mService

    def write(self, value: object) -> int:
        text = str(value)
        written = self._target.write(text)
        with self._lock:
            self._buffer += text
            while "\n" in self._buffer:
                line, self._buffer = self._buffer.split("\n", 1)
                self._send(line.rstrip("\r"))
        return written if isinstance(written, int) else len(text)

    def flush(self) -> None:
        self._target.flush()
        with self._lock:
            if self._buffer:
                self._send(self._buffer.rstrip("\r"))
                self._buffer = ""

    def isatty(self) -> bool:
        return False

    @property
    def encoding(self) -> str:
        return getattr(self._target, "encoding", "utf-8")

    def _send(self, line: str) -> None:
        if not line or not self._action or self._context is None:
            return
        intent = self._intent_type(self._action)
        if self._package:
            intent.setPackage(self._package)
        intent.putExtra("line", self._string_type(line))
        self._context.sendBroadcast(intent)


def _install_output_bridge(config: dict[str, Any]) -> None:
    if not config.get("output_action"):
        return
    sys.stdout = _AndroidOutputBridge(sys.stdout, config)
    sys.stderr = _AndroidOutputBridge(sys.stderr, config)
    os.environ["MFA_ANDROID_OUTPUT_BRIDGED"] = "1"


def main() -> None:
    config = _service_argument()
    _install_output_bridge(config)
    root = Path(str(config["data_root"])).resolve()
    if not root.is_dir():
        raise FileNotFoundError(f"MFA data root does not exist: {root}")

    for path in (root, root / "agent", root / "python"):
        path_text = str(path)
        if path.exists() and path_text not in sys.path:
            sys.path.insert(0, path_text)

    native_library_dir = str(config.get("native_library_dir") or "")
    if native_library_dir:
        os.environ["MAA_LIBRARY_DIR"] = native_library_dir
        os.environ["MAA_FRAMEWORK_LIB_DIR"] = native_library_dir
        old_library_path = os.environ.get("LD_LIBRARY_PATH", "")
        os.environ["LD_LIBRARY_PATH"] = (
            native_library_dir
            if not old_library_path
            else native_library_dir + os.pathsep + old_library_path
        )

    os.environ["MFA_INSTANCE_ID"] = str(config.get("instance_id") or "")
    os.environ["MFA_INSTANCE_NAME"] = str(config.get("instance_name") or "")

    script, trailing_arguments = _resolve_script(config, root)
    client_id = str(config["client_id"])
    os.chdir(root)
    sys.argv = [str(script), *trailing_arguments, client_id]
    runpy.run_path(str(script), run_name="__main__")


if __name__ == "__main__":
    main()
