"""Writes the notices of the third-party packages an app ships with.

Usage: python build/notices.py <project> <output file>
Reads the package list from `dotnet list package --include-transitive` and the licenses from the local package cache.
MIT and BSD ask that the copyright and the license go along with the binaries.
"""
import os
import re
import subprocess
import sys

MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."""

ALLOWED = {"MIT", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0"}


def packages(project):
    out = subprocess.run(["dotnet", "list", project, "package", "--include-transitive"], capture_output=True, text=True, check=True).stdout
    found = set()
    for line in out.splitlines():
        parts = line.split()
        if len(parts) >= 3 and parts[0] == ">":
            found.add((parts[1], parts[-1]))
    return sorted(found, key=lambda p: p[0].lower())


def main():
    project, target = sys.argv[1], sys.argv[2]
    cache = os.path.join(os.path.expanduser("~"), ".nuget", "packages")
    parts = ["Third-party software in this app\n================================\n"]
    failed = []
    for name, version in packages(project):
        folder = os.path.join(cache, name.lower(), version)
        nuspec = open(os.path.join(folder, name.lower() + ".nuspec"), encoding="utf-8-sig").read()
        copyright_line = re.search(r"<copyright>([^<]*)</copyright>", nuspec)
        holder = copyright_line.group(1).strip() if copyright_line else "the authors of " + name
        expression = re.search(r'<license type="expression">([^<]*)</license>', nuspec)
        file_license = re.search(r'<license type="file">([^<]*)</license>', nuspec)
        if expression:
            license_name = expression.group(1).strip()
            text = MIT if license_name == "MIT" else "See https://spdx.org/licenses/" + license_name + ".html"
            if license_name not in ALLOWED:
                failed.append(f"{name} {version}: {license_name}")
            parts.append(f"\n{name} {version}\nLicense: {license_name}\nCopyright: {holder}\n\n{text}\n")
        elif file_license:
            text = open(os.path.join(folder, file_license.group(1)), encoding="utf-8-sig", errors="replace").read().strip()
            parts.append(f"\n{name} {version}\nLicense: see below\n\n{text}\n")
        else:
            failed.append(f"{name} {version}: no license in the package")
    if failed:
        print("Licenses to check by hand:\n" + "\n".join(failed))
        return 1
    open(target, "w", encoding="utf-8", newline="\n").write("".join(parts))
    return 0


if __name__ == "__main__":
    sys.exit(main())
