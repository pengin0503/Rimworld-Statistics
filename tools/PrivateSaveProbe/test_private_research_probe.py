"""Pure stdlib fixture tests; never include real colony/file identifiers."""
from pathlib import Path
import tempfile
import unittest
from private_research_probe import probe, ProbeError

def xml(keys=('A', 'B'), values=('0', '100'), tech_keys=(), tech_vals=(), version='1.7.4985 rev1269'):
    def li(seq):
        return ''.join('<li>' + str(x) + '</li>' for x in seq)
    def pairs(k, v):
        return '<keys>' + li(k) + '</keys><values>' + li(v) + '</values>'
    return ('<?xml version="1.0" encoding="utf-8"?><savegame><meta><gameVersion>'
        + version + '</gameVersion><modIds><li>ludeon.rimworld</li></modIds></meta>'
        + '<game><researchManager><progress>' + pairs(keys, values)
        + '</progress><techprints>' + pairs(tech_keys, tech_vals)
        + '</techprints><knowledge>' + pairs((), ()) + '</knowledge>'
        + '</researchManager></game></savegame>')

class StructuralResearchProbeTests(unittest.TestCase):
    def with_temp(self, body, expected=None):
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'secret-source.rws'
            f.write_text(body, encoding='utf-8')
            before=(f.stat().st_size, f.stat().st_mtime_ns, f.read_bytes())
            result=probe(f, expected)
            self.assertEqual(before,(f.stat().st_size,f.stat().st_mtime_ns,f.read_bytes()))
            self.assertNotIn('secret-source',str(result))
            self.assertNotIn('A',str(result)) # raw keys not emitted
            return result

    def test_structural_progress_is_not_finished_count(self):
        r=self.with_temp(xml(), '1.7.4985 rev1269')
        self.assertTrue(r['exact_version_match'])
        self.assertTrue(r['source_unchanged'])
        self.assertIsNone(r['completed_projects'])
        self.assertEqual(2,r['research']['progress']['keys'])
        self.assertEqual(1,r['research']['progress']['positive_values'])
        self.assertTrue(r['research']['progress']['structural_valid'])
        self.assertEqual(1,r['mod_count'])

    def test_mismatched_build_never_claims_exact_match(self):
        r=self.with_temp(xml(), '1.7.5000 rev1275')
        self.assertFalse(r['exact_version_match'])
        self.assertIsNone(r['completed_projects'])

    def test_duplicate_key_or_mismatch_fails_closed(self):
        r=self.with_temp(xml(keys=('A','A')))
        self.assertEqual(1,r['research']['progress']['duplicate_keys'])
        self.assertFalse(r['research']['progress']['structural_valid'])
        r=self.with_temp(xml(keys=('X',),values=('0','5')))
        self.assertFalse(r['research']['progress']['structural_valid'])
        self.assertFalse(r['research']['progress']['pair_count_equal'])

    def test_negative_nan_and_invalid_numeric_values(self):
        r=self.with_temp(xml(values=('-1','NaN')))
        self.assertEqual(2,r['research']['progress']['invalid_values'])
        self.assertFalse(r['research']['progress']['structural_valid'])
        r=self.with_temp(xml(values=('foo','Infinity')))
        self.assertEqual(2,r['research']['progress']['invalid_values'])

    def test_bounded_research_lists(self):
        r=self.with_temp(xml(keys=('X','Y'),values=('2','3')))
        self.assertEqual(2,r['research']['progress']['positive_values'])
        self.assertIsNone(r['completed_projects'])

    def test_invalid_xml_is_error(self):
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'private.rws'
            f.write_bytes(b'<savegame><meta>')
            with self.assertRaises(ProbeError):
                probe(f)

    def test_missing_version_is_not_successful_evidence(self):
        r=self.with_temp(xml(version='').replace('<gameVersion></gameVersion>',''))
        self.assertEqual('missing-game-version',r['reader_status'])

if __name__ == '__main__':
    unittest.main()
