import csv
import contextlib
import io
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts.merge_translation_csv import (
    MigrationError,
    main,
    migrate,
    normalize_structural_key,
)


def write_csv(path: Path, rows: list[list[str]], encoding: str = "utf-8") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding=encoding, newline="") as handle:
        csv.writer(handle).writerows(rows)


def read_csv(path: Path) -> list[list[str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.reader(handle))


class MigrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.resources = Path(self.temporary_directory.name) / "resources"
        self.resources.mkdir()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_structural_normalization_unifies_ellipsis_stars_and_whitespace(self) -> None:
        expected = "shopitem-purchase--... i'm concerned."

        self.assertEqual(
            normalize_structural_key("ShopItem-Purchase--. . .*I'M CONCERNED."),
            expected,
        )
        self.assertEqual(
            normalize_structural_key("ShopItem-Purchase--...*I'm   concerned."),
            expected,
        )
        self.assertEqual(
            normalize_structural_key("ShopItem-Purchase--…  I'm concerned."),
            expected,
        )

    def test_structural_match_migrates_ellipsis_variant(self) -> None:
        old_key = "ShopItem-Purchase--. . .*I'M CONCERNED."
        new_key = "ShopItem-Purchase--...*I'm concerned."
        write_csv(self.resources / "item.csv", [[old_key, old_key, "…*我很担心。"]])
        write_csv(self.resources / "item-24023703.csv", [[new_key, new_key]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "item-24023703.csv"),
            [[new_key, new_key, "…*我很担心。"]],
        )
        self.assertEqual((summary.exact, summary.normalized), (0, 1))
        self.assertEqual(summary.matched, 1)
        self.assertEqual((summary.old_only, summary.new_only), (0, 0))

    def test_matches_first_column_with_casefold_and_replaces_translation(self) -> None:
        write_csv(
            self.resources / "speech.csv",
            [["SCENE-HELLO, \"TRAVELER\"!", "HELLO, \"TRAVELER\"!", "你好，旅人！"]],
            encoding="utf-8-sig",
        )
        write_csv(
            self.resources / "speech-24023703.csv",
            [["Scene-Hello, \"Traveler\"!", "Hello, \"Traveler\"!", "stale", "discard"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["Scene-Hello, \"Traveler\"!", "Hello, \"Traveler\"!", "你好，旅人！"]],
        )
        self.assertEqual(summary.matched, 1)
        self.assertEqual(summary.old_only, 0)
        self.assertEqual(summary.new_only, 0)
        self.assertTrue(
            (self.resources / "speech-24023703.csv").read_bytes().startswith(b"\xef\xbb\xbf")
        )

    def test_matching_ignores_extra_leading_bom_in_old_key(self) -> None:
        old_path = self.resources / "speech.csv"
        write_csv(
            old_path,
            [["KEY-BOM-CONTEXT-LONG", "Old display", "旧译文"]],
            encoding="utf-8-sig",
        )
        old_path.write_bytes(b"\xef\xbb\xbf" + old_path.read_bytes())
        new_rows = [["key-bom-context-long", "New display"]]
        write_csv(self.resources / "speech-24023703.csv", new_rows)

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["key-bom-context-long", "New display", "旧译文"]],
        )
        self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (1, 0, 0))
        self.assertEqual((summary.old_only, summary.new_only), (0, 0))

    def test_matching_uses_only_first_column_not_display_text(self) -> None:
        old_rows = [
            ["SAME-KEY", "Old display text", "matched translation"],
            ["OLD-ONLY", "Shared display text", "unmatched translation"],
        ]
        new_rows = [
            ["same-key", "New display text"],
            ["NEW-ONLY", "Shared display text"],
        ]
        write_csv(self.resources / "speech.csv", old_rows)
        write_csv(self.resources / "speech-24023703.csv", new_rows)

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [
                ["same-key", "New display text", "matched translation"],
                ["NEW-ONLY", "Shared display text", ""],
            ],
        )
        self.assertEqual((summary.exact, summary.normalized), (1, 0))
        self.assertEqual((summary.old_only, summary.new_only), (1, 1))
        self.assertEqual(
            read_csv(self.resources / "old" / "speech.csv"),
            [["OLD-ONLY", "Shared display text", "unmatched translation"]],
        )
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"),
            [["NEW-ONLY", "Shared display text"]],
        )

    def test_repeated_new_keys_receive_the_same_translation(self) -> None:
        write_csv(self.resources / "speaker.csv", [["SISKA", "SISKA", "西斯卡"]])
        write_csv(
            self.resources / "speaker-24023703.csv",
            [["Siska", "Siska"], ["SISKA", "SISKA"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speaker-24023703.csv"),
            [["Siska", "Siska", "西斯卡"], ["SISKA", "SISKA", "西斯卡"]],
        )
        self.assertEqual(summary.matched, 2)

    def test_rejects_allch_only_inputs_as_no_migration_inputs(self) -> None:
        write_csv(
            self.resources / "dialogue-ALLCH.csv",
            [["KEY", "Old text", "旧文本"]],
        )
        new_path = self.resources / "dialogue-ALLCH-24023703.csv"
        write_csv(new_path, [["KEY", "New text", "stale"]])
        before_migration = new_path.read_bytes()

        with self.assertRaisesRegex(MigrationError, "No .* files found"):
            migrate(self.resources)

        self.assertEqual(new_path.read_bytes(), before_migration)
        self.assertFalse((self.resources / "old" / "dialogue-ALLCH.csv").exists())
        self.assertFalse((self.resources / "new" / new_path.name).exists())

        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            exit_code = main(["--resources-dir", str(self.resources)])

        self.assertEqual(exit_code, 1)
        self.assertIn("No *-24023703.csv files found", stderr.getvalue())

    def test_migrates_normal_pair_without_changing_allch_pair(self) -> None:
        write_csv(self.resources / "speech.csv", [["KEY", "Text", "译文"]])
        write_csv(self.resources / "speech-24023703.csv", [["key", "Text"]])
        write_csv(
            self.resources / "dialogue-ALLCH.csv",
            [["ALLCH", "Old text", "旧文本"]],
        )
        allch_new_path = self.resources / "dialogue-ALLCH-24023703.csv"
        write_csv(allch_new_path, [["ALLCH", "New text", "stale"]])
        allch_old_bytes = (self.resources / "dialogue-ALLCH.csv").read_bytes()
        allch_new_bytes = allch_new_path.read_bytes()

        summary = migrate(self.resources)

        self.assertEqual(summary.files, 1)
        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["key", "Text", "译文"]],
        )
        self.assertEqual(
            (self.resources / "dialogue-ALLCH.csv").read_bytes(), allch_old_bytes
        )
        self.assertEqual(allch_new_path.read_bytes(), allch_new_bytes)
        self.assertFalse((self.resources / "old" / "dialogue-ALLCH.csv").exists())
        self.assertFalse((self.resources / "new" / allch_new_path.name).exists())

    def test_structural_matching_ignores_stars_but_keeps_other_punctuation(self) -> None:
        old_rows = [
            ["SPACE*KEY", "Text", "space"],
            ["PUNCT-KEY", "Text", "punctuation"],
        ]
        new_rows = [
            ["space   key", "Text"],
            ["punct.key", "Text"],
        ]
        write_csv(self.resources / "speech.csv", old_rows)
        write_csv(self.resources / "speech-24023703.csv", new_rows)

        summary = migrate(self.resources)

        self.assertEqual((summary.exact, summary.normalized), (0, 1))
        self.assertEqual((summary.old_only, summary.new_only), (1, 1))
        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["space   key", "Text", "space"], ["punct.key", "Text", ""]],
        )
        self.assertEqual(
            read_csv(self.resources / "old" / "speech.csv"),
            [["PUNCT-KEY", "Text", "punctuation"]],
        )
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"),
            [["punct.key", "Text"]],
        )

    def test_exact_match_takes_precedence_over_structural_candidate(self) -> None:
        write_csv(
            self.resources / "speech.csv",
            [
                ["CTX-HELLO*WORLD", "Text", "exact"],
                ["CTX-HELLO WORLD", "Text", "fallback"],
            ],
        )
        write_csv(
            self.resources / "speech-24023703.csv",
            [["ctx-hello*world", "Text"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["ctx-hello*world", "Text", "exact"]],
        )
        self.assertEqual((summary.exact, summary.normalized), (1, 0))
        self.assertEqual(summary.old_only, 1)

    def test_normalized_old_key_collision_stays_unmatched(self) -> None:
        old_rows = [
            ["CTX-. . .*READY", "Text", "first"],
            ["CTX-… READY", "Text", "second"],
        ]
        new_rows = [["ctx-... ready", "Text"]]
        write_csv(self.resources / "speech.csv", old_rows)
        write_csv(self.resources / "speech-24023703.csv", new_rows)

        summary = migrate(self.resources)

        self.assertEqual((summary.exact, summary.normalized), (0, 0))
        self.assertEqual((summary.old_only, summary.new_only), (2, 1))
        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["ctx-... ready", "Text", ""]],
        )
        self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), old_rows)
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"),
            new_rows,
        )

    def test_semantic_you_to_i_change_stays_unmatched(self) -> None:
        old_key = (
            "ShopItem-Purchase--CLEATED BOOTS*EQUIPPED!"
            "*YOU WILL NOW DEAL*MORE DAMAGE."
        )
        new_key = (
            "ShopItem-Purchase--Cleated Boots equipped!"
            "*I will now deal more damage."
        )
        write_csv(
            self.resources / "item.csv",
            [[old_key, old_key, "防滑钉鞋*已装备！*你现在可以造成*更多的伤害。"]],
        )
        write_csv(self.resources / "item-24023703.csv", [[new_key, new_key]])

        summary = migrate(self.resources)

        self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 0))
        self.assertEqual((summary.old_only, summary.new_only), (1, 1))
        self.assertEqual(
            read_csv(self.resources / "item-24023703.csv"),
            [[new_key, new_key, ""]],
        )

    def test_fuzzy_matching_accepts_small_punctuation_edit(self) -> None:
        old_key = (
            "ELITE_FEAT-CHECK-AURA-INCREDIBLE. . .*I.*IS THERE REALLY A"
            "*CHANCE?*THERE MAY BE A*WAY.*LISTEN CLOSE TO*THIS FORTUNE."
        )
        new_key = (
            "ELITE_FEAT-CHECK-Aura-Incredible...*I-*Is there really a chance?"
            "*There may be a way.*Listen close to this fortune."
        )
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "旧占卜"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "旧占卜"]],
        )
        self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 1))
        self.assertEqual((summary.old_only, summary.new_only), (0, 0))

    def test_fuzzy_matching_accepts_unique_short_spelling_edit(self) -> None:
        old_key = "SPELL-CONTEXT-A LONG SPELLING MISTAKE IN THIS MESSAGE"
        new_key = "SPELL-CONTEXT-A LONG SPELING MISTAKE IN THIS MESSAGE"
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "拼写译文"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "拼写译文"]],
        )
        self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 0, 1))

    def test_generated_index_match_fills_translation(self) -> None:
        old_key = "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT"
        new_key = "Caroline/<MeetEvent>d__26-MoveNext-Caroline-Hey, you!*Help me out"
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "旧译文"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "旧译文"]],
        )
        self.assertEqual(
            (summary.exact, summary.normalized, summary.generated, summary.fuzzy),
            (0, 0, 1, 0),
        )
        self.assertEqual((summary.old_only, summary.new_only), (0, 0))

    def test_generated_index_collision_stays_unmatched(self) -> None:
        old_rows = [
            [
                "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT",
                "Old 25",
                "译文 25",
            ],
            [
                "Caroline/<MeetEvent>d__26-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT",
                "Old 26",
                "译文 26",
            ],
        ]
        new_key = "Caroline/<MeetEvent>d__27-MoveNext-Caroline-Hey, you!*Help me out"
        write_csv(self.resources / "speech.csv", old_rows)
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"), [[new_key, "New", ""]]
        )
        self.assertEqual(summary.generated, 0)
        self.assertEqual((summary.old_only, summary.new_only), (2, 1))

    def test_generated_index_match_reuses_unique_translation(self) -> None:
        old_key = "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT"
        new_keys = [
            "Caroline/<MeetEvent>d__26-MoveNext-Caroline-Hey, you!*Help me out",
            "Caroline/<MeetEvent>d__27-MoveNext-Caroline-Hey, you!*Help me out",
        ]
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "旧译文"]])
        write_csv(
            self.resources / "speech-24023703.csv",
            [[new_key, "New"] for new_key in new_keys],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "旧译文"] for new_key in new_keys],
        )
        self.assertEqual(summary.generated, 2)
        self.assertEqual(
            summary.matched,
            summary.exact + summary.normalized + summary.generated + summary.fuzzy,
        )
        self.assertEqual((summary.old_only, summary.new_only), (0, 0))

    def test_structural_match_consumes_old_row_before_generated_index_matching(self) -> None:
        old_key = (
            "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY. . .*HELP ME OUT"
        )
        structural_new_key = (
            "Caroline/<MeetEvent>d__25-MoveNext-Caroline-Hey...*Help me out"
        )
        generated_new_key = (
            "Caroline/<MeetEvent>d__26-MoveNext-Caroline-Hey...*Help me out"
        )
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "结构译文"]])
        write_csv(
            self.resources / "speech-24023703.csv",
            [[structural_new_key, "Structural"], [generated_new_key, "Generated"]],
        )

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [
                [structural_new_key, "Structural", "结构译文"],
                [generated_new_key, "Generated", ""],
            ],
        )
        self.assertEqual(
            (summary.exact, summary.normalized, summary.generated, summary.fuzzy),
            (0, 1, 0, 0),
        )
        self.assertEqual((summary.old_only, summary.new_only), (0, 1))

    def test_fuzzy_matching_rejects_keys_shorter_than_minimum_length(self) -> None:
        old_key = "A-12345678901234567"
        new_key = "A-12345678901234568"
        write_csv(self.resources / "item.csv", [[old_key, "Old", "不应匹配"]])
        write_csv(self.resources / "item-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "item-24023703.csv"),
            [[new_key, "New", ""]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 1, 1))

    def test_fuzzy_matching_rejects_different_first_hyphen_prefixes(self) -> None:
        old_key = "OLD-CONTEXT-A LONG SHARED MESSAGE THAT WOULD OTHERWISE MATCH"
        new_key = "NEW-CONTEXT-A LONG SHARED MESSAGE THAT WOULD OTHERWISE MATCH"
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "不应匹配"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", ""]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 1, 1))

    def test_fuzzy_matching_rejects_edit_distance_greater_than_eight(self) -> None:
        old_key = "DISTANCE-" + "A" * 300
        new_key = "DISTANCE-" + "B" * 9 + "A" * 291
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "不应匹配"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", ""]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 1, 1))

    def test_fuzzy_matching_rejects_candidate_below_ratio_threshold(self) -> None:
        old_key = "RATIO-" + "A" * 200
        new_key = "RATIO-" + "B" * 8 + "A" * 192
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "不应匹配"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", ""]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 1, 1))

    def test_fuzzy_matching_accepts_eight_edits_above_ratio_threshold(self) -> None:
        old_key = "RATIO-" + "A" * 300
        new_key = "RATIO-" + "B" * 8 + "A" * 292
        write_csv(self.resources / "speech.csv", [[old_key, "Old", "应匹配"]])
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "应匹配"]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (1, 0, 0))

    def test_fuzzy_matching_rejects_ambiguous_candidates(self) -> None:
        old_rows = [
            ["AMBIG-CONTEXT-A LONG MESSAGE WITH ONE", "Old A", "甲"],
            ["AMBIG-CONTEXT-B LONG MESSAGE WITH ONE", "Old B", "乙"],
        ]
        new_key = "AMBIG-CONTEXT-C LONG MESSAGE WITH ONE"
        write_csv(self.resources / "speech.csv", old_rows)
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", ""]],
        )
        self.assertEqual((summary.fuzzy, summary.old_only, summary.new_only), (0, 2, 1))
        self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), old_rows)
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"),
            [[new_key, "New"]],
        )

    def test_structural_match_wins_over_fuzzy_candidate(self) -> None:
        structural_old_key = (
            "PRIORITY-CONTEXT-. . .*THIS IS A LONG ENOUGH MESSAGE FOR MATCHING"
        )
        fuzzy_old_key = (
            "PRIORITY-CONTEXT-... THIS IS A LONG ENOUGH MESSAGE FOR MATCHINX"
        )
        new_key = "priority-context-... this is a long enough message for matching"
        write_csv(
            self.resources / "speech.csv",
            [
                [structural_old_key, "Structural", "结构匹配"],
                [fuzzy_old_key, "Fuzzy", "不应选中"],
            ],
        )
        write_csv(self.resources / "speech-24023703.csv", [[new_key, "New"]])

        summary = migrate(self.resources)

        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [[new_key, "New", "结构匹配"]],
        )
        self.assertEqual((summary.exact, summary.normalized, summary.fuzzy), (0, 1, 0))
        self.assertEqual((summary.old_only, summary.new_only), (1, 0))

    def test_writes_original_old_only_and_new_only_rows(self) -> None:
        old_only = ["OLD-SCENE", "Removed text", "旧剧情"]
        new_only = ["NEW-SCENE", "Added text"]
        write_csv(self.resources / "speech.csv", [old_only])
        write_csv(self.resources / "speech-24023703.csv", [new_only])

        summary = migrate(self.resources)

        self.assertEqual(read_csv(self.resources / "old" / "speech.csv"), [old_only])
        self.assertEqual(
            read_csv(self.resources / "new" / "speech-24023703.csv"), [new_only]
        )
        self.assertTrue(
            (self.resources / "old" / "speech.csv").read_bytes().startswith(b"\xef\xbb\xbf")
        )
        self.assertTrue(
            (self.resources / "new" / "speech-24023703.csv").read_bytes().startswith(
                b"\xef\xbb\xbf"
            )
        )
        self.assertEqual(
            read_csv(self.resources / "speech-24023703.csv"),
            [["NEW-SCENE", "Added text", ""]],
        )
        self.assertEqual((summary.old_only, summary.new_only), (1, 1))

    def test_missing_old_translation_is_empty_and_second_run_is_idempotent(self) -> None:
        write_csv(self.resources / "item.csv", [["ITEM-0", ""]])
        write_csv(self.resources / "item-24023703.csv", [["item-0", ""]])

        migrate(self.resources)
        first_run = (self.resources / "item-24023703.csv").read_bytes()
        migrate(self.resources)

        self.assertEqual((self.resources / "item-24023703.csv").read_bytes(), first_run)
        self.assertEqual(read_csv(self.resources / "item-24023703.csv"), [["item-0", "", ""]])
        self.assertEqual(read_csv(self.resources / "old" / "item.csv"), [])
        self.assertEqual(read_csv(self.resources / "new" / "item-24023703.csv"), [])

    def test_empty_generated_csvs_contain_only_utf8_bom(self) -> None:
        write_csv(self.resources / "empty.csv", [])
        write_csv(self.resources / "empty-24023703.csv", [])

        migrate(self.resources)

        for path in (
            self.resources / "empty-24023703.csv",
            self.resources / "old" / "empty.csv",
            self.resources / "new" / "empty-24023703.csv",
        ):
            with self.subTest(path=path):
                self.assertEqual(path.read_bytes(), b"\xef\xbb\xbf")

    def test_replacement_failure_rolls_back_every_output(self) -> None:
        write_csv(self.resources / "speech.csv", [["KEY", "Old", "译文"]])
        new_path = self.resources / "speech-24023703.csv"
        write_csv(new_path, [["key", "New", "stale"]])
        old_report_path = self.resources / "old" / "speech.csv"
        write_csv(old_report_path, [["PRIOR", "Old report", "保留"]])
        new_report_path = self.resources / "new" / new_path.name
        original_new_bytes = new_path.read_bytes()
        original_old_report_bytes = old_report_path.read_bytes()
        real_replace = os.replace
        replacement_attempts: list[Path] = []
        failure_injected = False

        def fail_second_replacement(
            source: os.PathLike[str], target: os.PathLike[str]
        ) -> None:
            nonlocal failure_injected
            target_path = Path(target)
            if target_path in {new_path, old_report_path, new_report_path}:
                replacement_attempts.append(target_path)
                if len(replacement_attempts) == 2 and not failure_injected:
                    failure_injected = True
                    raise OSError("injected replacement failure")
            real_replace(source, target)

        with mock.patch("os.replace", side_effect=fail_second_replacement):
            with self.assertRaisesRegex(MigrationError, "injected replacement failure"):
                migrate(self.resources)

        self.assertEqual(replacement_attempts[:2], [new_path, old_report_path])
        self.assertEqual(new_path.read_bytes(), original_new_bytes)
        self.assertEqual(old_report_path.read_bytes(), original_old_report_bytes)
        self.assertFalse(new_report_path.exists())
        self.assertEqual(
            {
                path.relative_to(self.resources)
                for path in self.resources.rglob("*")
                if path.is_file()
            },
            {
                Path("speech.csv"),
                Path("speech-24023703.csv"),
                Path("old/speech.csv"),
            },
        )

    def test_rollback_failure_preserves_recovery_backup_and_continues(self) -> None:
        write_csv(self.resources / "speech.csv", [["KEY", "Old", "译文"]])
        new_path = self.resources / "speech-24023703.csv"
        write_csv(new_path, [["key", "New", "stale"]])
        old_report_path = self.resources / "old" / "speech.csv"
        write_csv(old_report_path, [["PRIOR", "Old report", "保留"]])
        new_report_path = self.resources / "new" / new_path.name
        original_new_bytes = new_path.read_bytes()
        original_old_report_bytes = old_report_path.read_bytes()
        real_replace = os.replace
        commit_failure_injected = False

        def fail_commit_and_restore(
            source: os.PathLike[str], target: os.PathLike[str]
        ) -> None:
            nonlocal commit_failure_injected
            source_path = Path(source)
            target_path = Path(target)
            if (
                source_path.suffix == ".tmp"
                and target_path == old_report_path
                and not commit_failure_injected
            ):
                commit_failure_injected = True
                raise OSError("injected commit failure")
            if source_path.suffix == ".bak" and target_path == new_path:
                raise OSError("injected restore failure")
            real_replace(source, target)

        with mock.patch("os.replace", side_effect=fail_commit_and_restore):
            with self.assertRaises(MigrationError) as raised:
                migrate(self.resources)

        recovery_backups = list(self.resources.rglob("*.bak"))
        self.assertEqual(len(recovery_backups), 1)
        recovery_backup = recovery_backups[0]
        self.assertEqual(recovery_backup.read_bytes(), original_new_bytes)
        self.assertIn(str(recovery_backup.resolve()), str(raised.exception))
        self.assertIn("injected commit failure", str(raised.exception))
        self.assertIn("injected restore failure", str(raised.exception))
        self.assertEqual(old_report_path.read_bytes(), original_old_report_bytes)
        self.assertFalse(new_report_path.exists())
        self.assertEqual(list(self.resources.rglob("*.tmp")), [])

    def test_backup_and_staged_cleanup_failures_are_both_reported(self) -> None:
        write_csv(self.resources / "speech.csv", [["KEY", "Old", "译文"]])
        write_csv(self.resources / "speech-24023703.csv", [["key", "New"]])
        real_unlink = Path.unlink

        def fail_staged_cleanup(path: Path, *args: object, **kwargs: object) -> None:
            if path.suffix == ".tmp":
                raise OSError("injected staged cleanup failure")
            real_unlink(path, *args, **kwargs)

        with mock.patch(
            "shutil.copyfile", side_effect=OSError("injected backup failure")
        ):
            with mock.patch.object(Path, "unlink", autospec=True, side_effect=fail_staged_cleanup):
                with self.assertRaises(MigrationError) as raised:
                    migrate(self.resources)

        self.assertIn("injected backup failure", str(raised.exception))
        self.assertIn("injected staged cleanup failure", str(raised.exception))

    def test_rejects_csv_parse_and_decode_errors(self) -> None:
        for directory_name, invalid_bytes in (
            ("parse-error", b'"unterminated'),
            ("decode-error", b"\xff"),
        ):
            with self.subTest(directory_name=directory_name):
                resources = self.resources / directory_name
                resources.mkdir()
                (resources / "speech.csv").write_bytes(invalid_bytes)
                write_csv(resources / "speech-24023703.csv", [["KEY", "Text"]])

                with self.assertRaisesRegex(MigrationError, "Cannot read CSV"):
                    migrate(resources)


class ValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.resources = Path(self.temporary_directory.name) / "resources"
        self.resources.mkdir()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_rejects_duplicate_old_keys_before_writing(self) -> None:
        write_csv(
            self.resources / "speech.csv",
            [["KEY", "One", "一"], ["key", "Two", "二"]],
        )
        new_path = self.resources / "speech-24023703.csv"
        write_csv(new_path, [["Key", "One"]])
        original_new_bytes = new_path.read_bytes()

        with self.assertRaisesRegex(MigrationError, "duplicates first-column key"):
            migrate(self.resources)

        self.assertEqual(new_path.read_bytes(), original_new_bytes)
        self.assertFalse((self.resources / "old").exists())
        self.assertFalse((self.resources / "new").exists())

    def test_rejects_any_invalid_pair_before_writing_valid_pairs(self) -> None:
        valid_new = self.resources / "a-24023703.csv"
        write_csv(self.resources / "a.csv", [["A", "A", "甲"]])
        write_csv(valid_new, [["a", "A"]])
        original_valid_bytes = valid_new.read_bytes()
        write_csv(self.resources / "b.csv", [["BROKEN"]])
        write_csv(self.resources / "b-24023703.csv", [["B", "B"]])

        with self.assertRaisesRegex(MigrationError, "fewer than two columns"):
            migrate(self.resources)

        self.assertEqual(valid_new.read_bytes(), original_valid_bytes)
        self.assertFalse((self.resources / "old").exists())
        self.assertFalse((self.resources / "new").exists())

    def test_rejects_missing_pair_and_empty_input_directory(self) -> None:
        with self.assertRaisesRegex(MigrationError, "No .* files found"):
            migrate(self.resources)

        write_csv(self.resources / "speech-24023703.csv", [["KEY", "Text"]])
        with self.assertRaisesRegex(MigrationError, "Missing previous-version CSV"):
            migrate(self.resources)


class CliTests(unittest.TestCase):
    def test_main_reports_totals(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            write_csv(
                resources / "speech.csv",
                [
                    ["KEY", "Text", "译文"],
                    ["SECOND*KEY", "Text", "第二条"],
                    [
                        "Caroline/<MeetEvent>d__25-MoveNext-CAROLINE-HEY, YOU!*HELP ME OUT",
                        "Text",
                        "编号",
                    ],
                    [
                        "FUZZY-CONTEXT-A LONG SPELLING MISTAKE IN THIS MESSAGE",
                        "Text",
                        "模糊",
                    ],
                ],
            )
            write_csv(
                resources / "speech-24023703.csv",
                [
                    ["key", "Text"],
                    ["second key", "Text"],
                    [
                        "Caroline/<MeetEvent>d__26-MoveNext-Caroline-Hey, you!*Help me out",
                        "Text",
                    ],
                    ["FUZZY-CONTEXT-A LONG SPELING MISTAKE IN THIS MESSAGE", "Text"],
                ],
            )
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 0)
            self.assertIn(
                "speech-24023703.csv: exact=1 normalized=1 generated=1 fuzzy=1 old_only=0 new_only=0",
                stdout.getvalue(),
            )
            self.assertIn(
                "files=1 exact=1 normalized=1 generated=1 fuzzy=1 old_only=0 new_only=0",
                stdout.getvalue(),
            )

    def test_main_returns_nonzero_and_prints_validation_error(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 1)
            self.assertIn("No *-24023703.csv files found", stderr.getvalue())

    def test_main_returns_nonzero_for_transaction_failure_without_traceback(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            write_csv(resources / "speech.csv", [["KEY", "Text", "译文"]])
            new_path = resources / "speech-24023703.csv"
            write_csv(new_path, [["key", "Text", "stale"]])
            original_new_bytes = new_path.read_bytes()
            real_replace = os.replace
            failure_injected = False

            def fail_first_replacement(
                source: os.PathLike[str], target: os.PathLike[str]
            ) -> None:
                nonlocal failure_injected
                if not failure_injected:
                    failure_injected = True
                    raise OSError("injected CLI replacement failure")
                real_replace(source, target)

            stderr = io.StringIO()
            with mock.patch("os.replace", side_effect=fail_first_replacement):
                with contextlib.redirect_stderr(stderr):
                    exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 1)
            self.assertIn("injected CLI replacement failure", stderr.getvalue())
            self.assertNotIn("Traceback", stderr.getvalue())
            self.assertEqual(new_path.read_bytes(), original_new_bytes)
