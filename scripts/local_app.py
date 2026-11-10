#!/usr/bin/env python3
"""Run a cloned phyloDIVaS checkout. Python standard library only."""
import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import sys
import time
import urllib.request
import webbrowser

ROOT = Path(__file__).resolve().parents[1]
SERVICES = (
    ("AnalysisAPI", 7040), ("SearchAPI", 7039),
    ("SearchHistoryAPI", 7227), ("RegistrationControllerAPI", 7072),
    ("LoginBffAPI", 7149), ("AccountAPI", 7017), ("FeedbackAPI", 7183),
)
ENV_URLS = {
    "VITE_ANALYSIS_API_URL": "http://localhost:7040",
    "VITE_SEARCH_API_URL": "https://localhost:7039",
    "VITE_SEARCH_HISTORY_API_URL": "https://localhost:7227",
    "VITE_REGISTRATION_API_URL": "https://localhost:7072",
    "VITE_LOGIN_API_URL": "https://localhost:7149",
    "VITE_ACCOUNT_API_URL": "https://localhost:7017",
    "VITE_FEEDBACK_API_URL": "https://localhost:7183",
}


def port_open(port):
    try:
        with socket.create_connection(("localhost", port), timeout=0.3):
            return True
    except OSError:
        return False


def dependencies():
    paths = {name: shutil.which(name) for name in ("dotnet", "node", "npm")}
    missing = [name for name, path in paths.items() if not path]
    if missing:
        raise RuntimeError("Missing prerequisites: " + ", ".join(missing) + ". See README.md.")
    sdk = subprocess.check_output([paths["dotnet"], "--list-sdks"], text=True)
    if not any(line.startswith("8.") for line in sdk.splitlines()):
        raise RuntimeError("Install the .NET 8 SDK (not just the runtime).")
    node = subprocess.check_output([paths["node"], "--version"], text=True).strip()
    if int(node.lstrip("v").split(".")[0]) < 22:
        raise RuntimeError("Install Node.js 22 or newer; this checkout is tested with Node 22.")
    return paths


def environment():
    env = os.environ.copy()
    config = ROOT / "local-config.json"
    if config.exists():
        values = json.loads(config.read_text())
        if not isinstance(values, dict) or any(not isinstance(v, str) for v in values.values()):
            raise RuntimeError("local-config.json must be an object of string environment values.")
        # Galaxy keys belong only in the UI's request-scoped key field.
        if any("galaxy" in key.lower() and "key" in key.lower() for key in values):
            raise RuntimeError("Remove Galaxy keys from local-config.json; enter them in the app instead.")
        env.update(values)
    for key, value in ENV_URLS.items():
        env.setdefault(key, value)
    env["DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE"] = "false"
    return env


def setup(paths, env):
    for name, _ in SERVICES:
        subprocess.run([paths["dotnet"], "restore", str(ROOT / name / (name + ".csproj"))],
                       cwd=ROOT, env=env, check=True)
        subprocess.run([paths["dotnet"], "build", "--no-restore", str(ROOT / name / (name + ".csproj"))],
                       cwd=ROOT, env=env, check=True)
    subprocess.run([paths["npm"], "ci"], cwd=ROOT / "ClientApp", env=env, check=True,
                   shell=os.name == "nt")


def stop(processes):
    for process in reversed(processes):
        if os.name == "nt":
            if process.poll() is None:
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        else:
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
    for process in processes:
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            if os.name != "nt":
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            else:
                process.kill()
            process.wait()


def launch(paths, env, no_browser=False):
    busy = [port for _, port in SERVICES if port_open(port)]
    if port_open(3000):
        busy.append(3000)
    if busy:
        raise RuntimeError("Ports already in use: " + ", ".join(map(str, busy))
                           + ". Stop the existing local app first. Nothing was terminated.")
    if not (ROOT / "ClientApp/node_modules").exists():
        raise RuntimeError("Dependencies missing. Run with --setup first.")
    log_dir = ROOT / ".local-run" / time.strftime("%Y%m%d-%H%M%S")
    log_dir.mkdir(parents=True)
    processes, logs = [], []
    try:
        commands = [(name, [paths["dotnet"], "run", "--no-build", "--no-restore", "--project",
                           str(ROOT / name / (name + ".csproj"))]
                     + ([] if name == "AnalysisAPI" else ["--launch-profile", "https"]), ROOT)
                    for name, _ in SERVICES]
        commands.append(("ClientApp", [paths["npm"], "run", "dev", "--", "--host", "127.0.0.1",
                                       "--port", "3000", "--strictPort"], ROOT / "ClientApp"))
        for name, command, cwd in commands:
            log = (log_dir / (name + ".log")).open("w")
            logs.append(log)
            process = subprocess.Popen(command, cwd=cwd, env=env, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=os.name != "nt",
                                       shell=os.name == "nt" and name == "ClientApp")
            processes.append(process)
        print("Starting eight services. Logs:", log_dir, flush=True)
        deadline = time.monotonic() + 120
        ready = False
        while True:
            for (name, _, _), process in zip(commands, processes):
                if process.poll() is not None:
                    raise RuntimeError(f"{name} exited. Inspect {log_dir / (name + '.log')}")
            if not ready:
                if all(port_open(port) for _, port in SERVICES):
                    try:
                        with urllib.request.urlopen("http://localhost:3000", timeout=2) as response:
                            ready = response.status == 200
                    except OSError:
                        pass
                if ready:
                    print("phyloDIVaS is ready: http://localhost:3000 — Ctrl+C stops these services.", flush=True)
                    if not no_browser:
                        webbrowser.open("http://localhost:3000")
                elif time.monotonic() > deadline:
                    raise RuntimeError(f"Startup exceeded 120 seconds. Inspect logs in {log_dir}.")
            time.sleep(0.5)
    finally:
        stop(processes)
        for log in logs:
            log.close()


def main():
    def terminate(_signal, _frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, terminate)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--setup", action="store_true", help="Restore/build APIs and npm ci before launching")
    parser.add_argument("--check", action="store_true", help="Check prerequisites/configuration without starting services")
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    paths, env = dependencies(), environment()
    if args.check:
        print("Prerequisites and local configuration format OK. Database/AWS access has not been verified.")
        return
    if args.setup:
        setup(paths, env)
    launch(paths, env, args.no_browser)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nStopped phyloDIVaS local services.")
    except (RuntimeError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print("Startup failed:", error, file=sys.stderr)
        sys.exit(1)
