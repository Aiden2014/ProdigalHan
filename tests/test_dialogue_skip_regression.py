import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PLUGIN = (ROOT / "Plugin.cs").read_text(encoding="utf-8")
CHAT_BOX = (ROOT / "resources" / "dump" / "CHAT_BOX.cs").read_text(encoding="utf-8")


class DialogueSkipRegressionTests(unittest.TestCase):
    def test_dump_contains_recursive_force_finish_path(self) -> None:
        """The regression must exercise the game's real skip path, not a normal tick."""

        self.assertRegex(
            CHAT_BOX,
            r"(?s)case KEYBOARD\.ACTIVE:.*?TYPER = KEYBOARD\.FORCE_FINISH;\s*APPLY_LETTER\(\);",
        )
        self.assertRegex(
            CHAT_BOX,
            r"(?s)else if \(TYPER == KEYBOARD\.FORCE_FINISH\).*?APPLY_LETTER\(\);",
        )

    def test_next_reflows_slots_after_force_finish_for_all_text_speeds(self) -> None:
        """NEXT must repair every visible slot after recursive APPLY_LETTER unwinds."""

        method = re.search(
            r"public static void CHAT_BOX_NEXT_Postfix_ReflowAfterSkip\(.*?\n    \}",
            PLUGIN,
            re.DOTALL,
        )
        self.assertIsNotNone(method, "NEXT needs a dedicated post-skip reflow hook")
        body = method.group(0)

        self.assertIn("AdjustAllTextPositionsByTextSlots", body)
        self.assertNotIn("IsInstantMode()", body)
