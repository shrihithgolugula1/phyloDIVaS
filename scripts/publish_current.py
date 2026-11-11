#!/usr/bin/env python3
"""Export current application source to a clean Git checkout and push a snapshot.

Usage: python3 scripts/publish_current.py --source CHECKOUT --target PUBLIC_CHECKOUT
Use --watch to back up stable edits every 60 seconds. No credentials are saved.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import time

EXCLUDED = {".git", ".vs", "node_modules", "bin", "obj", "build", "dist", ".local-run", "__pycache__", "__screenshots__"}


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()


def files(source):
    names = subprocess.check_output(["git", "-C", str(source), "ls-files", "-co", "--exclude-standard", "-z"]).decode().split("\0")
    return sorted({name for name in names if name and not EXCLUDED.intersection(Path(name).parts)
                   and not name.endswith((".user", ".log", ".zip", ".pfx", ".pem", ".key"))
                   and not Path(name).name.startswith(".env")
                   and not Path(name).name.startswith("results-viewer-audit-")
                   and Path(name).name not in {"local-config.json", "AGENTS.md", "lab-readiness.md"} and (source / name).is_file()
                   and not (source / name).is_symlink()})


def scrub_config(value, parent=""):
    if isinstance(value, dict):
        return {key: scrub_config(item, parent + "." + key) for key, item in value.items()}
    if isinstance(value, list):
        return [scrub_config(item, parent) for item in value]
    if isinstance(value, str) and re.search(r"connectionstrings|apikey|secret|password|accesskey|clientid|userpoolid", parent, re.I):
        return ""
    return value


def snapshot(source):
    result = {}
    for name in files(source):
        data = (source / name).read_bytes()
        if Path(name).name.startswith("appsettings") and name.endswith(".json"):
            data = (json.dumps(scrub_config(json.loads(data)), indent=2) + "\n").encode()
        # No machine-specific directory names are exported.
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            result[name] = data
            continue
        if str(source) in text:
            text = text.replace(str(source), "<project-checkout>")
        if source.name in text:
            text = text.replace(source.name, "phyloDIVaS")
        if re.search(r"-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{25,}|AKIA[A-Z0-9]{16}", text):
            raise RuntimeError(f"Potential credential in {name}; publication stopped.")
        result[name] = text.encode()
    return result


def digest(values):
    h = hashlib.sha256()
    for name, data in sorted(values.items()):
        h.update(name.encode() + b"\0" + data)
    return h.hexdigest()


def publish(source, target, push=True):
    if source == target or source in target.parents or target in source.parents:
        raise RuntimeError("Source and publication checkout must be separate sibling directories.")
    if not (target / ".git").exists():
        raise RuntimeError("Initialize the separate publication checkout first.")
    first = snapshot(source)
    if digest(first) != digest(snapshot(source)):
        raise RuntimeError("Files changed during snapshot; retry after editing settles.")
    # Delete only previously tracked publication files, never source or local config.
    old = git(target, "ls-files").splitlines()
    for name in old:
        if name not in first:
            (target / name).unlink(missing_ok=True)
    for name, data in first.items():
        path = target / name
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists() or path.read_bytes() != data:
            path.write_bytes(data)
    subprocess.run(["git", "-C", str(target), "add", "--all"], check=True)
    if subprocess.run(["git", "-C", str(target), "diff", "--cached", "--quiet"]).returncode:
        subprocess.run(["git", "-C", str(target), "commit", "--quiet", "-m", "Update phyloDIVaS application source"], check=True)
    if push:
        subprocess.run(["git", "-C", str(target), "push", "origin", "main"], check=True, timeout=120)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--target", type=Path, required=True)
    parser.add_argument("--watch", action="store_true")
    args = parser.parse_args()
    source, target = args.source.resolve(), args.target.resolve()
    last = None
    while True:
        try:
            current = digest(snapshot(source))
            if current != last:
                # Watch mode requires a quiet interval, avoiding half-written edits.
                if args.watch:
                    time.sleep(30)
                    if current != digest(snapshot(source)):
                        continue
                publish(source, target)
                last = current
                print("Published current phyloDIVaS source.", flush=True)
        except Exception as error:
            print(f"Backup not completed: {error}", flush=True)
            if not args.watch:
                raise
        if not args.watch:
            return
        time.sleep(60)


if __name__ == "__main__":
    main()
