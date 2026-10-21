"""Pure encoder checks; no game or native library is loaded."""
import struct
import unittest

from host import elements, encode_elements
from thermal_boundary_cases import thermal_boundary_cases


class ElementWordsTests(unittest.TestCase):
    def test_selected_fields_preserve_the_exact_word_and_other_bytes(self):
        element = elements()[1]
        original = encode_elements([element])
        for field, offset in dict(conductivity=16, solid_surface=24,
                                  liquid_surface=28, gas_surface=32).items():
            for word in (0x80000000, 0x7f800000, 0x7fc01234, 0x7f800001):
                actual = encode_elements([dict(element, float_word_overrides={field: word})])
                expected = bytearray(original)
                struct.pack_into('<I', expected, 4 + offset, word)
                self.assertEqual(actual, bytes(expected))

    def test_empty_overrides_do_not_change_existing_wire_data(self):
        element = elements()[1]
        self.assertEqual(encode_elements([element]),
                         encode_elements([dict(element, float_word_overrides={})]))

    def test_malformed_overrides_fail(self):
        for value in ([], {'id': 1}, {'conductivity': True}, {'conductivity': -1},
                      {'conductivity': 0x100000000}, {'conductivity': 1.0}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                encode_elements([dict(elements()[1], float_word_overrides=value)])

    def test_boundary_catalog_is_unique_and_encodable(self):
        cases = thermal_boundary_cases()
        self.assertEqual(len(cases), 32)
        self.assertEqual(len({case['name'] for case in cases}), 32)
        for case in cases:
            self.assertGreater(len(encode_elements(case['elements'])), 0)


if __name__ == '__main__':
    unittest.main()
