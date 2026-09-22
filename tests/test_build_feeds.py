import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('build_feeds', Path(__file__).resolve().parents[1] / 'Scripts/check-build-feeds.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def feeds(version='1.8.3.30'):
    entry = dict(version=version, targetAbi='10.11.8.0', checksum='a' * 32,
                 sourceUrl=f'https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v{version}/plugin.zip')
    return [[dict(guid='f87f700e-679d-43e6-9c7c-b3a410dc3f22', versions=[copy.deepcopy(entry)])] for _ in range(3)]


class BuildFeedTests(unittest.TestCase):
    def test_jellyfin12_candidate_preserves_published_10_feed(self):
        self.assertIn('candidate', module.validate(feeds('1.8.3.31'), '1.8.3.32', '12.0.0.0'))

    def test_jellyfin12_release_requires_its_actual_abi(self):
        data = feeds('1.8.3.32')
        with self.assertRaisesRegex(ValueError, 'targetAbi'):
            module.validate(data, '1.8.3.32', '12.0.0.0')
        for feed in data:
            feed[0]['versions'][0]['targetAbi'] = '12.0.0.0'
        self.assertIn('published entry', module.validate(data, '1.8.3.32', '12.0.0.0'))

    def test_candidate_does_not_require_premature_feed_publication(self):
        self.assertIn('candidate', module.validate(feeds(), '1.8.3.31'))

    def test_released_build_with_matching_feeds(self):
        self.assertIn('published entry', module.validate(feeds('1.8.3.31'), '1.8.3.31'))

    def test_partial_candidate_entry_is_rejected(self):
        data = feeds()
        data[2][0]['versions'].append(feeds('1.8.3.31')[0][0]['versions'][0])
        with self.assertRaisesRegex(ValueError, 'all three'):
            module.validate(data, '1.8.3.31')

    def test_diverging_entry_fields_are_rejected(self):
        for version in ('1.8.3.30', '1.8.3.31'):
            for key, value in [('checksum', 'b' * 32), ('changelog', 'different'), ('sourceUrl', 'https://invalid/plugin.zip')]:
                with self.subTest(version=version, key=key):
                    data = feeds(version); data[1][0]['versions'][0][key] = value
                    with self.assertRaisesRegex(ValueError, 'disagree'):
                        module.validate(data, '1.8.3.31')

    def test_older_build_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'older'):
            module.validate(feeds('1.8.3.32'), '1.8.3.31')

    def test_duplicate_current_entry_is_rejected(self):
        data = feeds('1.8.3.31'); data[0][0]['versions'] *= 2
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            module.validate(data, '1.8.3.31')

    def test_invalid_published_metadata_is_rejected(self):
        for key, value in [('checksum', 'bad'), ('targetAbi', '10.10.0.0'), ('sourceUrl', 'https://invalid/plugin.zip')]:
            with self.subTest(key=key):
                data = feeds()
                for feed in data: feed[0]['versions'][0][key] = value
                with self.assertRaises(ValueError): module.validate(data, '1.8.3.31')

    def test_missing_feed_and_wrong_guid_are_rejected(self):
        with self.assertRaises(ValueError): module.validate(feeds()[:2], '1.8.3.31')
        data = feeds(); data[0][0]['guid'] = 'wrong'
        with self.assertRaises(ValueError): module.validate(data, '1.8.3.31')


if __name__ == '__main__':
    unittest.main()
