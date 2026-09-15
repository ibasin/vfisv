"""One-time converter for the two inline C transmission-profile arrays."""
from __future__ import annotations

import pathlib
import re
import sys


def array(source: str, name: str) -> list[float]:
    match = re.search(rf"double\s+{name}\[\d+\]\s*=\s*\{{(.*?)\}};", source, re.S)
    if not match:
        raise RuntimeError(f"Could not find C array {name}")
    return [float(value) for value in match.group(1).replace("\n", " ").split(",") if value.strip()]


def main() -> None:
    source_path = pathlib.Path(sys.argv[1])
    output_path = pathlib.Path(sys.argv[2])
    source = source_path.read_text()
    blocker_wavelength = array(source, "wbd")
    blocker = array(source, "bld")
    front_wavelength = array(source, "wd")
    front = array(source, "fd")
    output_path.mkdir(parents=True, exist_ok=True)
    (output_path / "blocker_profile.csv").write_text(
        "wavelength,transmission\n" + "".join(f"{x:.10g},{y:.10g}\n" for x, y in zip(blocker_wavelength, blocker))
    )
    (output_path / "front_window_profile.csv").write_text(
        "wavelength,transmission\n" + "".join(f"{x:.10g},{y:.10g}\n" for x, y in zip(front_wavelength, front))
    )


if __name__ == "__main__":
    main()
