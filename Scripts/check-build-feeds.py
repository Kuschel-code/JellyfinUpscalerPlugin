"""Check feed consistency without advertising an unpublished build as a release."""
import argparse
import json
from pathlib import Path
import re

FEEDS = ('manifest.json', 'repository-jellyfin.json', 'repository-simple.json')


def version_parts(version):
    if not isinstance(version, str) or not re.fullmatch(r'\d+(?:\.\d+){3}', version):
        raise ValueError('Expected a four-part version')
    return tuple(map(int, version.split('.')))


def validate(feeds, build_version):
    build = version_parts(build_version)
    if len(feeds) != 3:
        raise ValueError('All three feeds are required')
    latest = []
    present = []
    for feed in feeds:
        if len(feed) != 1 or feed[0].get('guid') != 'f87f700e-679d-43e6-9c7c-b3a410dc3f22':
            raise ValueError('Unexpected plugin GUID')
        entries = feed[0]['versions']
        if not entries:
            raise ValueError('Empty feed')
        latest.append(entries[0])
        matches = [e for e in entries if e['version'] == build_version]
        if len(matches) > 1:
            raise ValueError('Duplicate build version')
        present.append(matches)
    if any(entry != latest[0] for entry in latest[1:]):
        raise ValueError('Latest feed entries disagree')
    if any(entry != present[0] for entry in present[1:]):
        raise ValueError('Build version must be identical in all three feeds or absent from all')
    entry = latest[0]
    published = version_parts(entry['version'])
    if published > build:
        raise ValueError('Build is older than the published feed')
    if present[0] and present[0][0] != entry:
        raise ValueError('Build version must be the latest feed entry when present')
    if entry.get('targetAbi') != '10.11.8.0':
        raise ValueError('Unexpected feed targetAbi')
    if not re.fullmatch('[0-9a-fA-F]{32}', entry.get('checksum', '')):
        raise ValueError('Expected a real release MD5 in the published feed')
    tag = entry['version'][:-2] if published[-1] == 0 else entry['version']
    prefix = f'https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v{tag}/'
    url = entry.get('sourceUrl', '')
    if not url.startswith(prefix) or not re.fullmatch(r'[A-Za-z0-9_.-]+\.zip', url[len(prefix):]):
        raise ValueError('Feed URL must identify its published version')
    return 'published entry consistent' if published == build else 'candidate: feeds correctly retain the previous release'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('.'))
    args = parser.parse_args()
    meta = json.loads((args.root / 'meta.json').read_text())
    print(validate([json.loads((args.root / name).read_text()) for name in FEEDS], meta['version']))
