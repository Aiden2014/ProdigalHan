import csv
import contextlib
import io
import tempfile
import unittest
from pathlib import Path

from scripts.merge_translation_csv import MigrationError, main, migrate


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

    def test_skips_allch_csv_pairs(self) -> None:
        write_csv(
            self.resources / "dialogue-ALLCH.csv",
            [["KEY", "Old text", "旧文本"]],
        )
        new_path = self.resources / "dialogue-ALLCH-24023703.csv"
        write_csv(new_path, [["KEY", "New text", "stale"]])
        before_migration = new_path.read_bytes()

        summary = migrate(self.resources)

        self.assertEqual(summary.files, 0)
        self.assertEqual(new_path.read_bytes(), before_migration)
        self.assertFalse((self.resources / "old" / "dialogue-ALLCH.csv").exists())
        self.assertFalse((self.resources / "new" / new_path.name).exists())

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
            write_csv(resources / "speech.csv", [["KEY", "Text", "译文"]])
            write_csv(resources / "speech-24023703.csv", [["key", "Text"]])
            stdout = io.StringIO()

            with contextlib.redirect_stdout(stdout):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 0)
            self.assertIn("files=1 matched=1 old_only=0 new_only=0", stdout.getvalue())

    def test_main_returns_nonzero_and_prints_validation_error(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            resources = Path(directory) / "resources"
            resources.mkdir()
            stderr = io.StringIO()

            with contextlib.redirect_stderr(stderr):
                exit_code = main(["--resources-dir", str(resources)])

            self.assertEqual(exit_code, 1)
            self.assertIn("No *-24023703.csv files found", stderr.getvalue())
