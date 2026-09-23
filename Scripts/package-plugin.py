#!/usr/bin/env python3
"""Package only the runtime payload from dotnet publish; never upload a release."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

RUNTIME_FILES = (
    'JellyfinUpscalerPlugin.dll', 'FFMpegCore.dll', 'CliWrap.dll',
    'Instances.dll', 'SixLabors.ImageSharp.dll', 'meta.json',
)


def package(publish, output, project):
    versions = [ET.parse(project).findtext('.//' + key) for key in ('Version', 'AssemblyVersion', 'FileVersion')]
    if not versions[0] or len(set(versions)) != 1:
        raise ValueError('Project versions disagree')
    for name in RUNTIME_FILES:
        path = publish / name
        if not path.is_file() or not path.stat().st_size:
            raise ValueError('Missing publish runtime file: ' + name)
    meta = json.loads((publish / 'meta.json').read_text())
    if meta['version'] != versions[0]:
        raise ValueError('Published meta.json version differs from project')
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in RUNTIME_FILES:
            archive.write(publish / name, name)
    with zipfile.ZipFile(output) as archive:
        if set(archive.namelist()) != set(RUNTIME_FILES) or archive.testzip() is not None:
            raise ValueError('ZIP content or integrity check failed')
        for name in RUNTIME_FILES:
            if archive.read(name) != (publish / name).read_bytes():
                raise ValueError('ZIP differs from publish: ' + name)
    return {'version': versions[0], 'files': list(RUNTIME_FILES),
            'md5': hashlib.md5(output.read_bytes()).hexdigest(), 'bytes': output.stat().st_size}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('publish', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--project', type=Path, default=Path('JellyfinUpscalerPlugin.csproj'))
    args = parser.parse_args()
    print(json.dumps(package(args.publish, args.output, args.project), indent=2))
