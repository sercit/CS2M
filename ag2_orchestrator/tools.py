import asyncio
import json
import os
import re
import subprocess
from pathlib import Path
from typing import Annotated, Any, List, Optional, Sequence

from autogen_core import CancellationToken
from autogen_core.tools import FunctionTool

REPO_ROOT = Path("/home/grec-alexander/Documents/CS2M")


async def read_file(path: Annotated[str, "Relative path from repo root"]) -> str:
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        return full_path.read_text(encoding="utf-8")
    except Exception as e:
        return f"[ERROR] Failed to read {path}: {e}"


async def read_multiple_files(paths: Annotated[str, "Comma-separated relative paths"]) -> str:
    results = []
    for p in paths.split(","):
        p = p.strip()
        if not p:
            continue
        content = await read_file(p)
        results.append(f"=== {p} ===\n{content}\n")
    return "\n".join(results)


async def write_file(path: Annotated[str, "Relative path"], content: Annotated[str, "File content"]) -> str:
    full_path = REPO_ROOT / path
    try:
        full_path.parent.mkdir(parents=True, exist_ok=True)
        full_path.write_text(content, encoding="utf-8")
        return f"[OK] Written {len(content)} chars to {path}"
    except Exception as e:
        return f"[ERROR] Failed to write {path}: {e}"


async def edit_file(path: Annotated[str, "Relative path"], old_string: Annotated[str, "Exact text to replace"], new_string: Annotated[str, "Replacement text"]) -> str:
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        content = full_path.read_text(encoding="utf-8")
        if old_string not in content:
            return f"[ERROR] old_string not found in {path}"
        full_path.write_text(content.replace(old_string, new_string, 1), encoding="utf-8")
        return f"[OK] Replaced in {path}"
    except Exception as e:
        return f"[ERROR] Failed to edit {path}: {e}"


async def regex_replace(path: Annotated[str, "Relative path"], pattern: Annotated[str, "Regex pattern"], replacement: Annotated[str, "Replacement string"]) -> str:
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        content = full_path.read_text(encoding="utf-8")
        new_content, count = re.subn(pattern, replacement, content)
        if count == 0:
            return f"[WARN] Pattern matched 0 times in {path}"
        full_path.write_text(new_content, encoding="utf-8")
        return f"[OK] Replaced {count} occurrence(s) in {path}"
    except Exception as e:
        return f"[ERROR] Regex replace failed: {e}"


async def search_code(query: Annotated[str, "Search query"], file_pattern: Annotated[str, "Glob pattern, e.g. *.cs or **/*.tsx"] = "*") -> str:
    result = subprocess.run(
        ["rg", "-n", "--no-heading", "-C", "2", query, "--glob", file_pattern],
        cwd=REPO_ROOT, capture_output=True, text=True, timeout=30
    )
    if not result.stdout:
        return f"[INFO] No matches for '{query}'"
    return result.stdout[-5000:] if len(result.stdout) > 5000 else result.stdout


async def list_files(pattern: Annotated[str, "Glob pattern, e.g. **/*.cs"]) -> str:
    matches = sorted(REPO_ROOT.glob(pattern))
    if not matches:
        return f"[INFO] No files match '{pattern}'"
    return "\n".join(str(m.relative_to(REPO_ROOT)) for m in matches)


async def run_command(command: Annotated[str, "Shell command"]) -> str:
    try:
        result = subprocess.run(command, shell=True, cwd=REPO_ROOT, capture_output=True, text=True, timeout=60)
        output = result.stdout or ""
        if result.stderr:
            output += "\nSTDERR:\n" + result.stderr
        if result.returncode != 0:
            output += f"\n[EXIT CODE {result.returncode}]"
        return output[-8000:] if len(output) > 8000 else output
    except Exception as e:
        return f"[ERROR] Command failed: {e}"


async def build_check() -> str:
    return await run_command("dotnet build CS2M.sln")


async def test_check() -> str:
    return await run_command("dotnet test CS2M.Test/CS2M.Test.csproj")


async def git_status() -> str:
    status = await run_command("git status --short")
    diff_stat = await run_command("git diff --stat")
    return f"=== Git Status ===\n{status}\n=== Diff Stats ===\n{diff_stat}"


ALL_TOOLS = [
    FunctionTool(read_file, description="Read a single file. Path: 'CS2M/Networking/LocalPlayer.cs'"),
    FunctionTool(read_multiple_files, description="Read multiple files at once. Paths: 'a.cs,b.cs'"),
    FunctionTool(write_file, description="Write content to a file. Creates dirs if needed."),
    FunctionTool(edit_file, description="Replace FIRST occurrence of exact text in a file."),
    FunctionTool(regex_replace, description="Regex replace ALL matches in a file."),
    FunctionTool(search_code, description="Search code with ripgrep. Query + optional file glob."),
    FunctionTool(list_files, description="List files matching a glob pattern."),
    FunctionTool(run_command, description="Run shell commands (git, dotnet, npm, etc.)."),
    FunctionTool(build_check, description="Run dotnet build to check compilation."),
    FunctionTool(test_check, description="Run dotnet test (NUnit)."),
    FunctionTool(git_status, description="Show git status and diff stats."),
]
