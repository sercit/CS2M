import asyncio
import json
import os
import re
import subprocess
from pathlib import Path
from typing import Annotated, Any, List, Optional, Sequence

from autogen_agentchat.agents import AssistantAgent
from autogen_agentchat.conditions import MaxMessageTermination, TextMentionTermination
from autogen_agentchat.teams import SelectorGroupChat
from autogen_agentchat.ui import Console
from autogen_core import CancellationToken
from autogen_core import FunctionCall
from autogen_core.models import (
    AssistantMessage,
    ChatCompletionClient,
    CreateResult,
    FunctionExecutionResultMessage,
    LLMMessage,
    ModelCapabilities,
    ModelInfo,
    RequestUsage,
    SystemMessage,
    UserMessage,
)
from autogen_core.tools import FunctionTool
from autogen_ext.models.openai import OpenAIChatCompletionClient

REPO_ROOT = Path("/home/grec-alexander/Documents/CS2M")
API_KEY = os.environ.get("DEEPSEEK_API_KEY", "sk-sYJm9aHB5W8X1dXteZYd0vS2lksaks6jPX1xLKawg4tUKnrPHuV0ioarHUMDOpDa")


class DeepSeekOpenAIClient(ChatCompletionClient):
    """Custom client for DeepSeek V4 that preserves reasoning_content across turns."""

    def __init__(self, model: str, base_url: str, api_key: str):
        self._client = OpenAIChatCompletionClient(
            model=model,
            base_url=base_url,
            api_key=api_key,
            model_info={
                "json_output": False,
                "function_calling": True,
                "vision": False,
                "family": "unknown",
                "structured_output": False,
            },
        )
        self._reasoning_store: dict[str, str] = {}
        self._actual_usage = RequestUsage(prompt_tokens=0, completion_tokens=0)
        self._total_usage = RequestUsage(prompt_tokens=0, completion_tokens=0)

    @property
    def capabilities(self) -> ModelCapabilities:
        return self._client.capabilities

    @property
    def actual_usage(self) -> RequestUsage:
        return self._actual_usage

    @property
    def total_usage(self) -> RequestUsage:
        return self._total_usage

    @property
    def model_info(self) -> ModelInfo:
        return self._client.model_info

    def remaining_tokens(self, messages: Sequence[LLMMessage], *, tools: Sequence[Any] = []) -> int:
        return 128000

    async def create_stream(
        self,
        messages: Sequence[LLMMessage],
        *,
        tools: Sequence[Any] = [],
        tool_choice: Any = "auto",
        json_output: Optional[bool | type] = None,
        extra_create_args: Any = {},
        cancellation_token: Optional[CancellationToken] = None,
    ):
        result = await self.create(
            messages, tools=tools, tool_choice=tool_choice,
            json_output=json_output, extra_create_args=extra_create_args,
            cancellation_token=cancellation_token,
        )
        yield result

    def _to_openai_dict(self, message: LLMMessage) -> dict | list[dict]:
        if isinstance(message, SystemMessage):
            return {"role": "system", "content": message.content}
        elif isinstance(message, UserMessage):
            return {"role": "user", "content": message.content, "name": message.source}
        elif isinstance(message, AssistantMessage):
            msg: dict[str, Any] = {"role": "assistant", "content": message.content or ""}
            reasoning = self._reasoning_store.get("__last_reasoning__")
            if reasoning:
                msg["reasoning_content"] = reasoning
            if message.thought:
                msg["reasoning_content"] = message.thought
            if isinstance(message.content, list):
                tool_calls = []
                for fc in message.content:
                    tool_calls.append({
                        "id": fc.id,
                        "type": "function",
                        "function": {"name": fc.name, "arguments": fc.arguments},
                    })
                msg["tool_calls"] = tool_calls
                msg["content"] = None
            return msg
        elif isinstance(message, FunctionExecutionResultMessage):
            if message.content:
                return [
                    {"role": "tool", "tool_call_id": r.call_id, "content": r.content}
                    for r in message.content
                ]
            return [{"role": "tool", "tool_call_id": "", "content": ""}]
        else:
            raise ValueError(f"Unknown message type: {type(message)}")

    async def create(
        self,
        messages: Sequence[LLMMessage],
        *,
        tools: Sequence[Any] = [],
        tool_choice: Any = "auto",
        json_output: Optional[bool | type] = None,
        extra_create_args: Any = {},
        cancellation_token: Optional[CancellationToken] = None,
    ) -> CreateResult:
        client = self._client._client
        openai_messages: list[dict] = []
        for m in messages:
            result = self._to_openai_dict(m)
            if isinstance(result, list):
                openai_messages.extend(result)
            else:
                openai_messages.append(result)

        tools_dicts = []
        for tool in tools:
            schema = None
            if hasattr(tool, "schema"):
                schema = dict(tool.schema)
            elif isinstance(tool, dict):
                schema = dict(tool)
            if schema:
                if "type" not in schema:
                    tools_dicts.append({"type": "function", "function": schema})
                else:
                    tools_dicts.append(schema)

        kwargs: dict[str, Any] = {
            "model": "deepseek-v4-flash",
            "messages": openai_messages,
            "stream": False,
            **dict(extra_create_args),
        }
        if tools_dicts:
            kwargs["tools"] = tools_dicts
            kwargs["tool_choice"] = tool_choice if tool_choice != "auto" else "auto"

        future = asyncio.ensure_future(client.chat.completions.create(**kwargs))
        if cancellation_token is not None:
            cancellation_token.link_future(future)

        result = await future
        choice = result.choices[0]

        usage = RequestUsage(
            prompt_tokens=getattr(result.usage, "prompt_tokens", 0) if result.usage else 0,
            completion_tokens=getattr(result.usage, "completion_tokens", 0) if result.usage else 0,
        )

        content: Any = choice.message.content or ""
        thought: str | None = None
        finish_reason = choice.finish_reason

        extra = getattr(choice.message, "model_extra", None) or {}
        reasoning = extra.get("reasoning_content")
        if reasoning is not None:
            thought = reasoning

        if choice.message.tool_calls and len(choice.message.tool_calls) > 0:
            if choice.message.content:
                thought = choice.message.content
            content = []
            for tc in choice.message.tool_calls:
                content.append(FunctionCall(
                    id=tc.id,
                    arguments=tc.function.arguments,
                    name=tc.function.name,
                ))
            finish_reason = "function_calls"
        else:
            finish_reason = choice.finish_reason

        if thought:
            self._reasoning_store["__last_reasoning__"] = thought

        response = CreateResult(
            finish_reason=finish_reason or "stop",
            content=content,
            usage=usage,
            cached=False,
            thought=thought,
        )

        self._total_usage = RequestUsage(
            prompt_tokens=self._total_usage.prompt_tokens + usage.prompt_tokens,
            completion_tokens=self._total_usage.completion_tokens + usage.completion_tokens,
        )
        self._actual_usage = self._total_usage
        return response

    async def count_tokens(self, messages, tools=None, cancellation_token=None):
        return await self._client.count_tokens(messages, tools=tools, cancellation_token=cancellation_token)

    async def close(self):
        await self._client.close()


MODEL_CLIENT = DeepSeekOpenAIClient(
    model="deepseek-v4-flash",
    base_url="https://opencode.ai/zen/go/v1",
    api_key=API_KEY,
)

SELECTOR_CLIENT = DeepSeekOpenAIClient(
    model="deepseek-v4-flash",
    base_url="https://opencode.ai/zen/go/v1",
    api_key=API_KEY,
)


# =============================================================================
# TOOLS
# =============================================================================

async def read_file(path: Annotated[str, "Relative path from repo root"]) -> str:
    """Read a single file from the repository."""
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        return full_path.read_text(encoding="utf-8")
    except Exception as e:
        return f"[ERROR] Failed to read {path}: {e}"


async def read_multiple_files(paths: Annotated[str, "Comma-separated relative paths"]) -> str:
    """Read multiple files at once. Pass comma-separated paths like 'CS2M/Mod.cs,CS2M/Log.cs'."""
    results = []
    for p in paths.split(","):
        p = p.strip()
        if not p:
            continue
        content = await read_file(p)
        results.append(f"=== {p} ===\n{content}\n")
    return "\n".join(results)


async def write_file(path: Annotated[str, "Relative path from repo root"], content: Annotated[str, "File content"]) -> str:
    """Write content to a file. Creates parent directories if needed."""
    full_path = REPO_ROOT / path
    try:
        full_path.parent.mkdir(parents=True, exist_ok=True)
        full_path.write_text(content, encoding="utf-8")
        return f"[OK] Written {len(content)} chars to {path}"
    except Exception as e:
        return f"[ERROR] Failed to write {path}: {e}"


async def edit_file(path: Annotated[str, "Relative path"], old_string: Annotated[str, "Exact text to replace"], new_string: Annotated[str, "Replacement text"]) -> str:
    """Replace the FIRST occurrence of old_string with new_string in a file."""
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        content = full_path.read_text(encoding="utf-8")
        if old_string not in content:
            return f"[ERROR] old_string not found in {path}"
        new_content = content.replace(old_string, new_string, 1)
        full_path.write_text(new_content, encoding="utf-8")
        return f"[OK] Replaced one occurrence in {path}"
    except Exception as e:
        return f"[ERROR] Failed to edit {path}: {e}"


async def regex_replace(path: Annotated[str, "Relative path"], pattern: Annotated[str, "Regex pattern"], replacement: Annotated[str, "Replacement string"]) -> str:
    """Replace all occurrences matching a regex pattern in a file."""
    full_path = REPO_ROOT / path
    if not full_path.exists():
        return f"[ERROR] File not found: {path}"
    try:
        content = full_path.read_text(encoding="utf-8")
        new_content, count = re.subn(pattern, replacement, content)
        if count == 0:
            return f"[WARN] Pattern '{pattern}' matched 0 times in {path}"
        full_path.write_text(new_content, encoding="utf-8")
        return f"[OK] Replaced {count} occurrence(s) in {path}"
    except Exception as e:
        return f"[ERROR] Regex replace failed in {path}: {e}"


async def search_code(query: Annotated[str, "Search query"], file_pattern: Annotated[str, "Glob pattern, e.g. *.cs or **/*.tsx"] = "*") -> str:
    """Search for code across the repository using ripgrep."""
    result = subprocess.run(
        ["rg", "-n", "--no-heading", "-C", "2", query, "--glob", file_pattern],
        cwd=REPO_ROOT, capture_output=True, text=True, timeout=30
    )
    if not result.stdout:
        return f"[INFO] No matches for '{query}'"
    return result.stdout[-5000:] if len(result.stdout) > 5000 else result.stdout


async def list_files(pattern: Annotated[str, "Glob pattern, e.g. **/*.cs"]) -> str:
    """List files matching a glob pattern."""
    matches = sorted(REPO_ROOT.glob(pattern))
    if not matches:
        return f"[INFO] No files match '{pattern}'"
    return "\n".join(str(m.relative_to(REPO_ROOT)) for m in matches)


async def run_command(command: Annotated[str, "Shell command"]) -> str:
    """Run a shell command in the repo root. Use for git, dotnet, npm, etc."""
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
    """Run dotnet build to verify C# compilation."""
    return await run_command("dotnet build CS2M.sln")


async def test_check() -> str:
    """Run the NUnit test project."""
    return await run_command("dotnet test CS2M.Test/CS2M.Test.csproj")


async def git_status() -> str:
    """Show git status and diff stats."""
    status = await run_command("git status --short")
    diff_stat = await run_command("git diff --stat")
    return f"=== Git Status ===\n{status}\n=== Diff Stats ===\n{diff_stat}"


TOOLS = [
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

# =============================================================================
# SYSTEM PROMPTS
# =============================================================================

REPO_CONTEXT = """You are working on CS2M — a multiplayer mod for Cities: Skylines 2.

TECH STACK:
- C# .NET Framework 4.7.2 (backend, Unity ECS, Harmony patching)
- React 18 + TypeScript + SCSS (frontend UI via game modding SDK)
- LiteNetLib (UDP networking)
- MessagePack (binary serialization)
- NUnit (testing)

CODE STYLE:
- C#: PascalCase for types/methods, camelCase for locals, _prefix for private fields
- 4-space indent, CRLF line endings per .editorconfig
- Use `readonly` where possible, prefer `var` for local variables
- React: functional components, hooks, CSS modules with kebab-case class names
- Always add null checks for reference types from external APIs

ERROR HANDLING:
- Log errors via `Log.Error()` / `Log.Warn()` (Colossal.Logging)
- Never silently swallow exceptions — always log or rethrow
- Validate preconditions at method entry and return early on failure
- State machine transitions must be guarded by status checks

TESTING REQUIREMENTS:
- Every new public method should have at least one test
- Use Arrange-Act-Assert pattern
- Mock external dependencies (LiteNetLib, Unity ECS)
- MessagePack serialization round-trips must be verified

ARCHITECTURE PATTERNS:
- Commands: `CommandBase` -> `CommandHandler<T>` dispatch via `CommandInternal`
- Networking: `NetworkInterface` singleton -> `NetworkManager` -> LiteNetLib
- Player state machine: INACTIVE -> GET_SERVER_INFO -> NAT_CONNECT -> DIRECT_CONNECT -> CONNECTION_ESTABLISHED -> WAITING_TO_JOIN -> DOWNLOADING_MAP -> LOADING_MAP -> PLAYING
- UI: React components trigger C# bindings -> `UISystem` handles logic
"""

BASE_RULES = """
WORKFLOW:
1. INVESTIGATE: Read relevant files BEFORE editing. Use search_code and read_file.
2. PLAN: Briefly describe your approach before making changes.
3. EXECUTE: Make minimal, focused changes. One concern per edit.
4. VERIFY: Run build_check() after C# changes. Run test_check() if tests exist.
5. REPORT: Summarize what you changed and why.

RULES:
- Do NOT ask for permission — just do the work.
- When done, say "TASK COMPLETE: <summary>".
- If stuck after 2 attempts, say "STUCK: <issue>" and ask the Architect.
- Never change interfaces without checking all callers.
- Keep backwards compatibility unless explicitly told otherwise.
"""

ARCHITECT_PROMPT = REPO_CONTEXT + """
You are the SENIOR ARCHITECT and TEAM LEAD for CS2M.

YOUR RESPONSIBILITIES:
1. BREAK DOWN complex tasks into subtasks for specialized agents.
2. REVIEW code changes for correctness, style, and architecture fit.
3. COORDINATE agent handoffs — ensure context is preserved.
4. MAKE FINAL DECISIONS on trade-offs and approach.

WHEN DELEGATING:
- Say "ASSIGN @AgentName: <clear task with specific files>"
- Include acceptance criteria (e.g., "must pass build_check()")

WHEN REVIEWING:
- Check for: null safety, state machine consistency, memory leaks, thread safety
- Verify error handling is present
- Confirm tests cover new logic
- Say "APPROVED" or "NEEDS CHANGES: <specific feedback>"

WHEN DONE:
- Say "TERMINATE" to end the session.
""" + BASE_RULES

NETWORKING_PROMPT = REPO_CONTEXT + """
You are the NETWORKING EXPERT for CS2M.

YOUR DOMAIN:
- CS2M/Networking/ (all files)
- CS2M/Commands/ (serialization, dispatch, handlers)
- CS2M/API/Commands/ (protocol definitions)

EXPERTISE:
- LiteNetLib: NetManager, NetPeer, DeliveryMethod, NAT punch-through
- UDP networking: packet fragmentation, MTU, reliable vs unreliable
- State machines: LocalPlayer status transitions, preconditions
- Binary serialization: MessagePack, type graphs, version compatibility
- Security: command filtering by connection state, password auth

FOCUS AREAS:
- Fix TODOs in NetworkManager (UPnP, port checks, disconnect handling)
- Implement missing state machine handlers
- Add command validation (filter commands by player status)
- Fix SlicedPacketStream bugs
- Improve NAT punch-through reliability
""" + BASE_RULES

UI_PROMPT = REPO_CONTEXT + """
You are the UI/FRONTEND EXPERT for CS2M.

YOUR DOMAIN:
- CS2M.UI/src/ (all React/TS components)
- CS2M/UI/ (C# UI bindings: UISystem.cs, ChatPanel.cs)
- lang/ (localization JSON files)

EXPERTISE:
- React 18 functional components, hooks, JSX
- CSS Modules: proper module reference syntax (styles.className, NOT string literals)
- CS2 modding SDK: trigger/value bindings between C# and React
- Webpack 5, TypeScript strict mode, SCSS
- Game UI patterns: panels, menus, input fields, chat systems

FOCUS AREAS:
- Fix chat.tsx CSS module references (broken string literals)
- Add password input fields to host/join menus
- Add input validation (IP format, port range, username length)
- Add missing localization keys for player statuses
- Implement player list / server management UI
- Fix the brittle children.length==5 check in main-menu.tsx
""" + BASE_RULES

BACKEND_PROMPT = REPO_CONTEXT + """
You are the C# BACKEND EXPERT for CS2M.

YOUR DOMAIN:
- CS2M.BaseGame/ (building sync, game integration)
- CS2M/Helpers/ (SaveLoadHelper, SlicedPacketStream, ReflectionHelper)
- CS2M/Mods/ (ModSupport, ModCompat, DlcCompat)

EXPERTISE:
- Harmony 2.x: patching game methods, prefixes/postfixes/transpilers
- Unity ECS: SystemBase, EntityQuery, ComponentSystemBase
- C# .NET 4.7.2: async/await, generics, reflection, serialization
- Game modding: intercepting game state changes, replaying actions

FOCUS AREAS:
- Implement BuildingCreateHandler (currently empty stub)
- Fill in BuildingSystem.OnUpdate (query exists but does nothing)
- Fix SlicedPacketStream.Clear() read offset bug
- Add error handling to SaveLoadHelper.LoadGame()
- Populate ModCompat compatibility databases
- Implement BaseGameConnection.RegisterHandlers()
""" + BASE_RULES

TEST_PROMPT = REPO_CONTEXT + """
You are the TESTING EXPERT for CS2M.

YOUR DOMAIN:
- CS2M.Test/ (all test files)
- Test infrastructure and mocking

EXPERTISE:
- NUnit 4.x: asserts, test fixtures, parameterized tests
- Mocking: Moq or hand-rolled mocks for LiteNetLib/Unity types
- MessagePack serialization testing
- State machine transition testing
- Code coverage analysis

FOCUS AREAS:
- Add tests for SlicedPacketStream (read/write/seek/clear)
- Add state machine transition tests for LocalPlayer
- Add command handler dispatch tests
- Add serialization round-trip tests for all CommandBase subclasses
- Add networking lifecycle tests (connect -> join -> disconnect)
- Mock game assemblies so tests can run without the game installed
""" + BASE_RULES

SECURITY_PROMPT = REPO_CONTEXT + """
You are the SECURITY EXPERT for CS2M.

YOUR DOMAIN:
- All networking security concerns
- Input validation and sanitization
- Command filtering and authorization
- Password handling and auth flows

EXPERTISE:
- Network security: command injection, replay attacks, spoofing
- Input validation: regex, length limits, whitelist approaches
- Auth: password hashing, token validation, session management
- Game anti-cheat: server authority, client prediction validation

FOCUS AREAS:
- Fix NetworkManager: filter commands by player connection state (line 216 TODO)
- Add password field to UI (currently hardcoded to empty string)
- Add input validation for IP, port, username fields
- Review command handler access control
- Add rate limiting for chat messages
- Ensure world transfer only happens after successful auth
""" + BASE_RULES

DATA_PROMPT = REPO_CONTEXT + """
You are the DATA & SERIALIZATION EXPERT for CS2M.

YOUR DOMAIN:
- CS2M/Commands/CommandInternal.cs (serialization pipeline)
- CS2M/Helpers/SaveLoadHelper.cs (save/load)
- CS2M/Helpers/SlicedPacketStream.cs (packet streaming)
- CS2M/Util/MessagePackExtensions.cs (type graph)

EXPERTISE:
- MessagePack: format, resolvers, type registration, version compatibility
- Binary protocols: packet framing, chunking, streaming
- Unity serialization: ISerializable, StreamBinaryReader patches
- Data integrity: checksums, validation, corruption detection

FOCUS AREAS:
- Fix SlicedPacketStream read offset reset bug (line 168-169)
- Add error handling to CommandInternal.Deserialize()
- Fix MessagePack type graph rebuild on assembly changes
- Add packet integrity checks to world transfer
- Optimize slice size calculation for MTU
- Document the serialization format
""" + BASE_RULES

HARMONY_PROMPT = REPO_CONTEXT + """
You are the UNITY/HARMONY EXPERT for CS2M.

YOUR DOMAIN:
- CS2M.BaseGame/Injections/ (Harmony patches)
- CS2M.BaseGame/Systems/ (ECS systems)
- CS2M/Helpers/ReflectionHelper.cs

EXPERTISE:
- Harmony 2.2: PatchClassProcessor, MethodInfo patches, transpilers
- Unity ECS: EntityManager, ComponentData, SystemBase, EntityQuery
- Colossal Framework: game-specific APIs, modding hooks
- Reflection: assembly scanning, type discovery, dynamic invocation

FOCUS AREAS:
- Implement BuildingCreate injection (currently empty)
- Fill BuildingSystem.OnUpdate with actual building sync logic
- Add Harmony patches for other game systems (roads, zones, etc.)
- Fix BaseGameConnection.RegisterHandlers() (empty stub)
- Ensure patches are reversible on mod unload
- Add ECS system update ordering
""" + BASE_RULES

BUILD_PROMPT = REPO_CONTEXT + """
You are the BUILD & DEVOPS EXPERT for CS2M.

YOUR DOMAIN:
- Build scripts, CI/CD, packaging
- CS2M.sln, all .csproj files
- Webpack config, npm scripts

EXPERTISE:
- MSBuild: project references, NuGet packages, ILRepack
- Webpack 5: loaders, plugins, dev/prod configs
- CI/CD: GitHub Actions, build matrices, artifact publishing
- Packaging: mod distribution, version management

FOCUS AREAS:
- Verify all project references are correct
- Check ILRepack configuration for single-assembly output
- Fix webpack externals for cs2/* modules
- Add CI workflow for automated builds and tests
- Optimize build times
- Ensure build works on clean checkout
""" + BASE_RULES

DOCS_PROMPT = REPO_CONTEXT + """
You are the DOCUMENTATION & LOCALIZATION EXPERT for CS2M.

YOUR DOMAIN:
- README.md, docs/
- lang/*.json (en-US, de-DE, pl-PL)
- Inline code comments and XML docs

EXPERTISE:
- Technical writing: clear, concise, accurate
- Localization: i18n patterns, key naming, fallback chains
- API documentation: XML docs, docstrings, examples
- User-facing docs: setup guides, troubleshooting

FOCUS AREAS:
- Add missing localization keys (player statuses, chat messages)
- Fill empty option descriptions in lang files
- Write setup/contributing docs
- Add inline docs to public APIs
- Ensure all UI strings are localized (no hardcoded English)
- Translate new keys to de-DE and pl-PL
""" + BASE_RULES

# =============================================================================
# AGENTS
# =============================================================================

AGENTS = [
    AssistantAgent(
        name="Architect",
        description="Senior architect. Plans work, reviews code, coordinates agents, makes final decisions.",
        system_message=ARCHITECT_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="NetworkingExpert",
        description="C# networking expert. LiteNetLib, UDP, state machines, command serialization, NAT punch-through.",
        system_message=NETWORKING_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="UIExpert",
        description="React/TypeScript expert. CS2 UI modding, CSS modules, localization, C#<->TS bindings.",
        system_message=UI_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="BackendExpert",
        description="C# backend expert. Harmony patching, Unity ECS, building sync, save/load.",
        system_message=BACKEND_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="TestExpert",
        description="Testing expert. NUnit, mocking, serialization tests, state machine tests, coverage.",
        system_message=TEST_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="SecurityExpert",
        description="Security expert. Input validation, command filtering, auth, network security, anti-cheat.",
        system_message=SECURITY_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="DataExpert",
        description="Data & serialization expert. MessagePack, packet streaming, save/load, binary protocols.",
        system_message=DATA_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="HarmonyExpert",
        description="Unity/Harmony expert. ECS systems, game method patching, reflection, mod integration.",
        system_message=HARMONY_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="BuildExpert",
        description="Build & DevOps expert. MSBuild, webpack, CI/CD, packaging, ILRepack.",
        system_message=BUILD_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
    AssistantAgent(
        name="DocsExpert",
        description="Documentation & localization expert. Technical writing, i18n, README, inline docs.",
        system_message=DOCS_PROMPT,
        model_client=MODEL_CLIENT,
        tools=TOOLS,
    ),
]

SELECTOR_PROMPT = (
    "You are a team coordinator selecting the next agent to speak.\n\n"
    "{roles}\n\n"
    "Conversation so far:\n{history}\n\n"
    "Select the single most appropriate agent from: {participants}\n"
    "Return ONLY the agent name, nothing else.\n\n"
    "GUIDELINES:\n"
    "- Architect: for planning, reviewing, delegating, or when task is complete\n"
    "- NetworkingExpert: for networking, connections, state machines, commands\n"
    "- UIExpert: for React components, CSS, localization, menus, chat\n"
    "- BackendExpert: for C# logic, Harmony patches, ECS, building sync\n"
    "- TestExpert: for writing tests, coverage, mocks\n"
    "- SecurityExpert: for validation, auth, filtering, security review\n"
    "- DataExpert: for serialization, save/load, packet streaming\n"
    "- HarmonyExpert: for Unity ECS systems, game integration, patches\n"
    "- BuildExpert: for build scripts, CI, packaging, webpack\n"
    "- DocsExpert: for documentation, localization, README\n"
)


async def run_groupchat(task: str, max_rounds: int = 60):
    termination = TextMentionTermination("TERMINATE") | MaxMessageTermination(max_rounds)

    team = SelectorGroupChat(
        AGENTS,
        model_client=SELECTOR_CLIENT,
        termination_condition=termination,
        allow_repeated_speaker=True,
        selector_prompt=SELECTOR_PROMPT,
    )

    stream = team.run_stream(task=task)
    await Console(stream)


if __name__ == "__main__":
    task = "Audit the repo and report all critical bugs and stubs."
    if len(os.sys.argv) > 1:
        task = os.sys.argv[1]
    asyncio.run(run_groupchat(task))
