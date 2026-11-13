# Append translated rows (key<TAB>value) to the language table, skipping keys already present.
# Usage: python append_loc.py <lang> <chunkfile>
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CREATOR = os.path.normpath(os.path.join(HERE, ".."))
CYR = re.compile("[Ѐ-ӿ]")
PH = re.compile(r"\{\d+\}")
NAMES = {"en": "LocEnglish", "fr": "LocFrench", "de": "LocGerman", "ja": "LocJapanese",
         "ko": "LocKorean", "zh": "LocChinese", "es": "LocSpanish", "pt": "LocPortuguese"}


def rows(path):
    out = []
    for raw in io.open(path, encoding="utf-8").read().split("\n"):
        l = raw.rstrip("\r")
        if l and l[0] != "#":
            out.append(l)
    return out


def lead(s):
    return s[: len(s) - len(s.lstrip())]


def trail(s):
    return s[len(s.rstrip()):]


def main():
    lang, chunk = sys.argv[1], sys.argv[2]
    target = os.path.join(CREATOR, NAMES[lang] + ".tsv")
    en = {l.split("\t", 1)[0]: l.split("\t", 1)[1] for l in rows(os.path.join(CREATOR, "LocEnglish.tsv")) if "\t" in l}
    existing = [l.split("\t", 1)[0] for l in rows(target) if "\t" in l]
    have = set(existing)
    added = 0
    problems = []
    with io.open(target, "a", encoding="utf-8", newline="") as f:
        for l in rows(chunk):
            if "\t" not in l:
                problems.append("no tab: " + l[:40])
                continue
            k, v = l.split("\t", 1)
            if k in have:
                continue
            ev = en.get(k)
            if ev is None:
                problems.append("unknown key: " + k[:40])
                continue
            if lead(v) != lead(ev) or trail(v) != trail(ev):
                v = lead(ev) + v.strip() + trail(ev)
            if PH.findall(v) != PH.findall(ev):
                problems.append("placeholders: " + k[:40])
            if CYR.search(v):
                problems.append("cyrillic: " + k[:40])
            f.write(k + "\t" + v + "\r\n")
            have.add(k)
            added += 1
    print("%s: appended %d rows, now %d/%d" % (lang, added, len(have), len(en)))
    for p in problems[:30]:
        print("   WARN " + p)


if __name__ == "__main__":
    main()