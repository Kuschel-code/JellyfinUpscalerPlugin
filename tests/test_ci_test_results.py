import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("trx", Path(__file__).resolve().parents[1] / "Scripts/assert-test-results.py")
trx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trx)
GOOD = '''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult outcome="Passed" /></Results>
<ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
</TestRun>'''


class TrxValidatorTests(unittest.TestCase):
    def validate(self, content):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "tests.trx"
            if content is not None:
                path.write_text(content)
            return trx.validate(path)

    def test_success(self):
        self.assertEqual(self.validate(GOOD)["executed"], 1)

    def test_reject_invalid_runs(self):
        cases = [None, "", "<broken", "<TestRun/>",
                 GOOD.replace('executed="1"', 'executed="0"'),
                 GOOD.replace('failed="0"', 'failed="1"'),
                 GOOD.replace('outcome="Completed"', 'outcome="Aborted"'),
                 GOOD.replace('outcome="Passed"', 'outcome="Failed"'),
                 GOOD.replace('<UnitTestResult outcome="Passed" />', ''),
                 GOOD.replace('total="1"', 'total="2"'),
                 GOOD.replace('executed="1"', 'executed="-1"'),
                 GOOD.replace('failed="0"', 'failed="0" error="1"')]
        for content in cases:
            with self.subTest(content=content), self.assertRaises((OSError, ValueError, KeyError, ET.ParseError)):
                self.validate(content)


if __name__ == "__main__":
    unittest.main()
