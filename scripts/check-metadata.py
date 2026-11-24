#!/usr/bin/env python3
"""Checks App Store field lengths in AppStore/SUBMISSION.md."""
import pathlib
import re
import sys

LIMITS = {"Subtitle": 30, "Promotional text": 170, "Keywords": 100, "Description": 4000}
text = pathlib.Path(__file__).resolve().parent.parent.joinpath("AppStore/SUBMISSION.md").read_text()
failed = False
for lang, body in re.findall(r"^## (English \(U\.S\.\)|Russian[^\n]*)\n(.*?)(?=^## )", text, re.S | re.M):
    for field, limit in LIMITS.items():
        m = re.search(r"^\*\*" + re.escape(field) + r"\*\* \(\d+\)\n(.*?)(?=\n\*\*|\Z)", body, re.S | re.M)
        if not m:
            print(f"{lang}: {field} missing"); failed = True; continue
        value = m.group(1).strip()
        ok = len(value) <= limit
        failed |= not ok
        print(f"{'ok ' if ok else 'TOO LONG'} {lang[:7]:8} {field:17} {len(value):5}/{limit}")
sys.exit(1 if failed else 0)
