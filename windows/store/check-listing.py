#!/usr/bin/env python3
"""Checks Partner Center field lengths in windows/store/SUBMISSION.md."""
import pathlib
import re
import sys

text = pathlib.Path(__file__).resolve().parent.joinpath("SUBMISSION.md").read_text(encoding="utf-8")
failed = False


def field(name):
    m = re.search(r"^\*\*" + re.escape(name) + r"\*\*[^\n]*\n(.*?)(?=\n\*\*|\n## |\Z)", text, re.S | re.M)
    return m.group(1).strip() if m else None


def check(label, value, limit):
    global failed
    ok = value is not None and len(value) <= limit
    failed |= not ok
    print(f"{'ok ' if ok else 'FAIL'} {label:32} {len(value or ''):5}/{limit}")


check("Short description", field("Short description"), 270)
check("Description", field("Description"), 10000)
check("What's new in this version", field("What's new in this version"), 1500)
features = [line[2:] for line in (field("Product features") or "").splitlines() if line.startswith("- ")]
check("Product features (count)", "x" * len(features), 20)
for f in features:
    check("  feature", f, 200)
terms = [t.strip() for t in (field("Search terms") or "").split("·")]
check("Search terms (count)", "x" * len(terms), 7)
for t in terms:
    check("  term", t, 30)
sys.exit(1 if failed else 0)
