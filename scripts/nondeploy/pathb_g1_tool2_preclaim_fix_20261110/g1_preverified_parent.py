#!/usr/bin/env python3
"""Run the immutable original Parent with a network-free tool-byte provider."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import re
import sys

from g1_preclaim_tools import copy_preverified


def load_parent(root: Path):
    sys.path.insert(0, str(root))
    path = root / "p3_kudu_cloudshell_parent.py"
    spec = importlib.util.spec_from_file_location("_fv_g1_original_parent", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("PARENT_IMPORT_HOLD")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", required=True)
    parser.add_argument("--tool-cache", required=True)
    parser.add_argument("--authorization", required=True)
    parser.add_argument("--distribution", required=True)
    parser.add_argument("--artifact-sha256", required=True)
    args = parser.parse_args()
    root = Path(args.candidate_root).resolve()
    cache = Path(args.tool_cache).resolve()
    parent = load_parent(root)
    cfg = json.loads((root / "executor_config.json").read_text("utf-8"))
    tools = cfg["tools"]
    parent.download_once = lambda url, path, expected: copy_preverified(
        tools, cache, url, Path(path), expected)
    namespace = argparse.Namespace(
        live=True, authorization=args.authorization,
        distribution=args.distribution,
        artifact_sha256=args.artifact_sha256)
    return parent.execute(namespace)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        print("P3_KUDU_LIVE=HOLD")
        print("STOP_STAGE=PREVERIFIED_TOOLS_HANDOFF_HOLD")
        print("AUTO_RETRY=FORBIDDEN")
        raise SystemExit(1)

