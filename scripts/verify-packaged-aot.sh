#!/usr/bin/env bash
# Exercise the actual Akka NuGet buildTransitive target, rather than manually setting its result.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_root="${1:?Pass an output directory for retained package, publish and run evidence.}"
mkdir -p "$output_root"
output_root="$(cd "$output_root" && pwd)"
case "$output_root/" in
    "$repo_root/"*)
        echo 'Use an output directory outside the repository so consumers cannot inherit its build properties.' >&2
        exit 2
        ;;
esac
run_dir="$(mktemp -d "$output_root/akka-packaged-aot.XXXXXX")"
feed="$run_dir/feed"
version="1.6.0-aot-package-probe.$(date +%s).$$"
mkdir -p "$feed"
echo "Packaged AOT evidence: $run_dir"

dotnet pack "$repo_root/src/core/Akka/Akka.csproj" -c Release \
    -p:Version="$version" -p:PackageVersion="$version" -o "$feed" \
    2>&1 | tee "$run_dir/pack.log"

# Keep the full existing core-canary corpus and watchdog. Generate two fresh consumers outside
# Directory.Build.props: one imports the real package target, one deliberately excludes it.
# Neither project declares Akka.DynamicTypeLoading. The negative control must fail at runtime
# with the exact switch assertion, rather than merely failing to compile or restore.
python3 - "$repo_root" "$run_dir" <<'PY'
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

root, output = map(Path, sys.argv[1:])
source = root / "src/aot/Akka.AOT.App"
configuration = ET.Element("configuration")
sources = ET.SubElement(configuration, "packageSources")
ET.SubElement(sources, "clear")
ET.SubElement(sources, "add", key="candidate", value=str(output / "feed"))
ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
ET.ElementTree(configuration).write(output / "NuGet.Config", encoding="utf-8", xml_declaration=True)

for name in ("consumer", "missing-target-control"):
    destination = output / name
    destination.mkdir()
    for path in source.rglob("*.cs"):
        relative = path.relative_to(source)
        if any(part in ("bin", "obj") for part in relative.parts):
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    project = ET.parse(source / "Akka.AOT.App.csproj")
    for parent in project.getroot():
        for item in list(parent):
            if item.tag in ("ProjectReference", "RuntimeHostConfigurationOption"):
                parent.remove(item)
            elif item.tag == "TargetFramework":
                item.text = "net10.0"
            elif item.tag == "Compile" and item.get("Include"):
                item.set("Include", str((source / item.get("Include").replace("\\", "/")).resolve()))
    properties = ET.SubElement(project.getroot(), "PropertyGroup")
    ET.SubElement(properties, "TreatWarningsAsErrors").text = "true"
    project.write(destination / "Akka.AOT.App.csproj", encoding="utf-8", xml_declaration=True)
PY

for consumer in consumer missing-target-control; do
    dotnet add "$run_dir/$consumer/Akka.AOT.App.csproj" package Akka --version "$version" --no-restore
done

python3 - "$run_dir/missing-target-control/Akka.AOT.App.csproj" <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
path = Path(sys.argv[1])
project = ET.parse(path)
reference = next(item for item in project.iter("PackageReference") if item.get("Include") == "Akka")
reference.set("ExcludeAssets", "buildTransitive")
project.write(path, encoding="utf-8", xml_declaration=True)
PY

dotnet publish "$run_dir/consumer/Akka.AOT.App.csproj" -r linux-x64 -c Release \
    -o "$run_dir/positive-publish" 2>&1 | tee "$run_dir/positive-publish.log"
timeout 300 "$run_dir/positive-publish/Akka.AOT.App" 2>&1 | tee "$run_dir/positive-run.log"
grep -qF '[canary] OK' "$run_dir/positive-run.log"

dotnet publish "$run_dir/missing-target-control/Akka.AOT.App.csproj" -r linux-x64 -c Release \
    -o "$run_dir/negative-publish" 2>&1 | tee "$run_dir/negative-publish.log"
set +e
timeout 300 "$run_dir/negative-publish/Akka.AOT.App" 2>&1 | tee "$run_dir/negative-run.log"
negative_exit="${PIPESTATUS[0]}"
set -e
if [ "$negative_exit" -eq 0 ] || ! grep -qF '[canary] Akka.DynamicTypeLoading must be explicitly disabled.' "$run_dir/negative-run.log"; then
    echo 'The missing-target negative control did not fail at the expected runtime boundary.' >&2
    exit 1
fi
echo '[packaged-aot] OK: shipped target enables the full native core corpus; excluding it fails the runtime control.'
