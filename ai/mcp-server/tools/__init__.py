"""Re-exports for Political Mayhem MCP tools."""

from __future__ import annotations

import sys
from pathlib import Path

_MCP_DIR = Path(__file__).resolve().parent.parent
if str(_MCP_DIR) not in sys.path:
    sys.path.insert(0, str(_MCP_DIR))

from server import TOOL_REGISTRY, call_tool_safely, get_openai_tools_schema  # noqa: E402

__all__ = ["TOOL_REGISTRY", "call_tool_safely", "get_openai_tools_schema"]
