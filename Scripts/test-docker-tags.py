"""Behavioral checks for the tags the real Docker workflow publishes."""

import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("docker_tags", Path(__file__).with_name("docker-tags.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DockerTagsTests(unittest.TestCase):
    def test_candidates_cannot_move_any_release_tag(self):
        for suffix in ("", "-amd", "-intel", "-apple", "-vulkan", "-cpu", "-converter"):
            with self.subTest(suffix=suffix):
                tags = module.compose_tags("1.8.3.31", "97c0181", suffix, "candidate")
                self.assertEqual([tag.split(":")[1] for tag in tags],
                                 [f"rc-v1.8.3.31-97c0181{suffix}", f"rc-v1.8.3.31{suffix}"])
                self.assertTrue(set(tags).isdisjoint(module.compose_tags("1.8.3.31", "97c0181", suffix, "release")))

    def test_release_tags_preserve_backend_and_latest_mapping(self):
        for suffix in ("", "-amd", "-intel", "-apple", "-vulkan", "-cpu", "-converter"):
            with self.subTest(suffix=suffix):
                tags = module.compose_tags("1.8.3.31", "97c0181", suffix, "release")
                expected = [f"docker7{suffix}", f"docker7-v1.8.3.31{suffix}", f"v1.8.3.31{suffix}"]
                if not suffix:
                    expected.append("latest")
                self.assertEqual([tag.split(":")[1] for tag in tags], expected)

    def test_new_commit_has_a_distinct_candidate_pin(self):
        self.assertNotEqual(module.compose_tags("1.8.3.31", "97c0181", "-cpu", "candidate")[0],
                            module.compose_tags("1.8.3.31", "1234567", "-cpu", "candidate")[0])

    def test_invalid_input_fails_closed(self):
        for args in (("latest", "97c0181", "", "candidate"),
                     ("1.8.3.31", "unknown", "", "candidate"),
                     ("1.8.3.31", "97c0181", "-cuda", "candidate"),
                     ("1.8.3.31", "97c0181", "", "typo")):
            with self.subTest(args=args), self.assertRaises(ValueError):
                module.compose_tags(*args)


if __name__ == "__main__":
    unittest.main()
