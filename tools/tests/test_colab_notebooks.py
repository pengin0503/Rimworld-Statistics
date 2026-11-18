"""Validate checked-in Colab notebooks without Colab or third-party packages."""

import ast
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NOTEBOOKS = sorted((ROOT / "notebooks").glob("*.ipynb"))


class ColabNotebookTests(unittest.TestCase):
    def test_notebook_structure_and_python_syntax(self):
        self.assertGreaterEqual(len(NOTEBOOKS), 3)
        for notebook_path in NOTEBOOKS:
            with self.subTest(notebook=notebook_path.name):
                notebook = json.loads(notebook_path.read_text(encoding="utf-8"))
                self.assertEqual(notebook["nbformat"], 4)
                self.assertGreaterEqual(notebook["nbformat_minor"], 5)
                self.assertEqual(notebook["metadata"]["kernelspec"]["name"], "python3")
                self.assertGreaterEqual(len(notebook["cells"]), 6)
                ids = set()
                code_count = 0
                for cell in notebook["cells"]:
                    self.assertIn(cell["cell_type"], ("markdown", "code"))
                    self.assertIsInstance(cell.get("source"), list)
                    self.assertNotIn(cell.get("id"), ids)
                    ids.add(cell.get("id"))
                    if cell["cell_type"] == "code":
                        code_count += 1
                        self.assertIsNone(cell["execution_count"])
                        self.assertEqual(cell["outputs"], [])
                        ast.parse("".join(cell["source"]), filename=str(notebook_path))
                self.assertGreaterEqual(code_count, 3)


if __name__ == "__main__":
    unittest.main()
