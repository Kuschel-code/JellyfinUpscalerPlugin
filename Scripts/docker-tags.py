"""Resolve Docker publication tags without moving release tags for an RC build."""

import argparse
import os
import re
from pathlib import Path


def compose_tags(version, revision, suffix, channel):
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", version):
        raise ValueError("Expected the checkout's four-part plugin version")
    if not re.fullmatch(r"[0-9a-f]{7,40}", revision):
        raise ValueError("Expected a Git commit revision")
    if suffix not in ("", "-amd", "-intel", "-apple", "-vulkan", "-cpu", "-converter"):
        raise ValueError("Unknown backend suffix")
    image = "kuscheltier/jellyfin-ai-upscaler"
    if channel == "candidate":
        # A candidate never changes Watchtower targets or final version pins.
        return [f"{image}:rc-v{version}-{revision}{suffix}",
                f"{image}:rc-v{version}{suffix}"]
    if channel == "release":
        tags = [f"{image}:docker7{suffix}", f"{image}:docker7-v{version}{suffix}",
                f"{image}:v{version}{suffix}"]
        if not suffix:
            tags.append(f"{image}:latest")
        return tags
    raise ValueError("Publication channel must be candidate or release")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--suffix", default="")
    parser.add_argument("--channel", required=True, choices=("candidate", "release"))
    args = parser.parse_args()
    tags = compose_tags(args.version, args.revision, args.suffix, args.channel)
    output = f"tags={','.join(tags)}\nprimary={tags[0]}\n"
    if os.environ.get("GITHUB_OUTPUT"):
        with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as stream:
            stream.write(output)
    print(output, end="")
