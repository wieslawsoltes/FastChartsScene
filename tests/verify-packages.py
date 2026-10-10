"""Verify that every reusable project produced a real, nonempty NuGet package."""
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
expected = sorted(p.stem for p in (root / 'src').glob('*/*.csproj'))
packages = root / 'artifacts' / 'packages'
for name in expected:
    matches = list(packages.glob(name + '.*.nupkg'))
    assert len(matches) == 1, f'{name}: expected one package, found {matches}'
    with zipfile.ZipFile(matches[0]) as archive:
        assert archive.testzip() is None, f'{name}: corrupt ZIP'
        files = archive.namelist()
        nuspec = next(p for p in files if p.endswith('.nuspec'))
        metadata = ET.fromstring(archive.read(nuspec))
        package_id = next(e.text for e in metadata.iter() if e.tag.endswith('}id') or e.tag == 'id')
        assert package_id == name, (name, package_id)
        assemblies = [p for p in files if p.startswith('lib/') and p.endswith('/' + name + '.dll')]
        assert assemblies, f'{name}: package contains no implementation assembly'
        assert all(archive.getinfo(p).file_size > 0 for p in assemblies)
        print(f'PASS {name}: {len(assemblies)} target assembly/assemblies')
print(f'RESULT {len(expected)}/{len(expected)} reusable packages verified')
