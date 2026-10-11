"""Verify actual NuGet identities, not ambiguous filename prefixes such as Rendering.*."""
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
expected = sorted(p.stem for p in (root / 'src').glob('*/*.csproj'))
packages = root / 'artifacts' / 'packages'
by_id: dict[str, list[Path]] = {}

for package in sorted(packages.glob('*.nupkg')):
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None, f'{package.name}: corrupt ZIP'
        specs = [p for p in archive.namelist() if p.endswith('.nuspec')]
        assert len(specs) == 1, f'{package.name}: expected exactly one manifest'
        manifest = ET.fromstring(archive.read(specs[0]))
        ids = [e.text for e in manifest.iter() if e.tag.rsplit('}', 1)[-1] == 'id']
        assert len(ids) == 1 and ids[0], f'{package.name}: invalid package identity'
        by_id.setdefault(ids[0], []).append(package)

assert set(by_id) == set(expected), f'Package inventory differs: missing={set(expected)-set(by_id)}, extra={set(by_id)-set(expected)}'
for name in expected:
    matches = by_id[name]
    assert len(matches) == 1, f'{name}: expected one package, found {matches}'
    with zipfile.ZipFile(matches[0]) as archive:
        assemblies = [p for p in archive.namelist() if p.startswith('lib/') and p.endswith('/' + name + '.dll')]
        assert assemblies, f'{name}: package contains no implementation assembly'
        assert all(archive.getinfo(p).file_size > 0 for p in assemblies)
        if name == 'FastChartsScene.Controls.Uno':
            # NuGet normalizes these Uno platform TFMs with platform version 1.0.
            assert {p.split('/')[1] for p in assemblies} == {'net10.0-browserwasm1.0', 'net10.0-desktop1.0'}, f'{name}: missing target assembly'
        print(f'PASS {name}: {len(assemblies)} target assembly/assemblies')
print(f'RESULT {len(expected)}/{len(expected)} reusable packages verified')
