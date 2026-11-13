# Offline validator for the per-language UI tables against LocEnglish.tsv. Usage: python check_loc_tables.py fr de ja ko zh es pt
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CREATOR = os.path.normpath(os.path.join(HERE, ".."))
CYR = re.compile("[Ѐ-ӿ]")
ESC = re.compile(r"\\\\|\\n|\\t|\\r")
PH = re.compile(r"\{\d+\}")
NAMES = {"en": "LocEnglish", "fr": "LocFrench", "de": "LocGerman", "ja": "LocJapanese",
         "ko": "LocKorean", "zh": "LocChinese", "es": "LocSpanish", "pt": "LocPortuguese"}


def rows(path):
    out = []
    with io.open(path, encoding="utf-8") as f:
        for raw in f.read().split("\n"):
            line = raw.rstrip("\r")
            if not line or line[0] == "#":
                continue
            out.append(line)
    return out


def lead(s):
    return s[: len(s) - len(s.lstrip())]


def trail(s):
    return s[len(s.rstrip()):]


def main():
    langs = sys.argv[1:] or ["fr", "de", "ja", "ko", "zh", "es", "pt"]
    en = rows(os.path.join(CREATOR, "LocEnglish.tsv"))
    en_keys = [l.split("\t", 1)[0] for l in en]
    en_vals = {l.split("\t", 1)[0]: l.split("\t", 1)[1] for l in en if "\t" in l}
    fail = 0
    for lang in langs:
        path = os.path.join(CREATOR, NAMES[lang] + ".tsv")
        if not os.path.exists(path):
            print("%s: MISSING FILE" % lang)
            fail += 1
            continue
        problems = []
        lines = rows(path)
        keys = []
        for i, line in enumerate(lines):
            if line.count("\t") != 1:
                problems.append("line %d: %d tabs" % (i + 1, line.count("\t")))
                continue
            k, v = line.split("\t", 1)
            keys.append(k)
            ev = en_vals.get(k)
            if ev is None:
                problems.append("line %d: unknown key" % (i + 1))
                continue
            if lead(v) != lead(ev) or trail(v) != trail(ev):
                problems.append("line %d: edge spaces differ" % (i + 1))
            if PH.findall(v) != PH.findall(ev):
                problems.append("line %d: placeholders differ" % (i + 1))
            if CYR.search(v):
                problems.append("line %d: Cyrillic in value" % (i + 1))
            if "\\" in ESC.sub("", v):
                problems.append("line %d: bad escape" % (i + 1))
            if ev and not v:
                problems.append("line %d: empty value" % (i + 1))
        if keys != en_keys:
            missing = [k for k in en_keys if k not in set(keys)]
            problems.append("key sequence differs (%d missing, %d extra)" % (len(missing), len(keys) - len(en_keys) + len(missing)))
        if problems:
            fail += 1
            print("%s: FAIL (%d rows)" % (lang, len(lines)))
            for p in problems[:40]:
                print("   " + p)
        else:
            print("%s: PASS (%d rows)" % (lang, len(lines)))
    sys.exit(1 if fail else 0)


if __name__ == "__main__":
    main()