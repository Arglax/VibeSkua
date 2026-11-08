"""Builds MVS Auto Enhance's Ultra data from the r/AQW "Simplified Bosses Guide" sheet.

Each boss tab pairs a class with a set written "Weapon | Class / Helm | Cape", for example
"Dauntless | Lucky / Anima | Vainglory" or "Lucky Aweblast | Lucky / Base Forge | Penitence".
For each class the set used in the most fights wins (ties: the first seen). Farming and Bossing
come from AUQW's CoreAdvanced.cs at run time (MvsAutoEnhance.cs), so they follow its updates.

Run:  python tools/build_enhance_data.py
Writes MoVibeSkua.Android/Bot/mvs-enhance-ultra.json
"""
import csv, io, json, re, sys, urllib.request
from collections import Counter, defaultdict
from datetime import date
from pathlib import Path

SHEET = "1yytxo6x0XlvbTIYLLD6TCEF_v1EKV59e_N-gas9Fi8Y"
OUT = Path(__file__).resolve().parent.parent / "MoVibeSkua.Android" / "Bot" / "mvs-enhance-ultra.json"

TYPES = ["Fighter", "Thief", "Hybrid", "Wizard", "Healer", "SpellBreaker", "Lucky"]
WEAPON = {"dauntless": "Dauntless", "ravenous": "Ravenous", "valiance": "Valiance", "praxis": "Praxis",
          "arcana concerto": "Arcanas_Concerto", "arcanas concerto": "Arcanas_Concerto", "lacerate": "Lacerate",
          "smite": "Smite", "elysium": "Elysium", "acheron": "Acheron", "aweblast": "Awe_Blast", "awe blast": "Awe_Blast",
          "health vamp": "Health_Vamp", "mana vamp": "Mana_Vamp", "spiral carve": "Spiral_Carve",
          "powerword die": "Powerword_Die", "forge": "Forge"}
HELM = {"anima": "Anima", "examen": "Examen", "pneuma": "Pneuma", "vim": "Vim", "hearty": "Hearty",
        "base forge": "Forge", "forge": "Forge"}
CAPE = {"vainglory": "Vainglory", "lament": "Lament", "absolution": "Absolution", "penitence": "Penitence",
        "avarice": "Avarice", "base forge": "Forge", "forge": "Forge"}
KEYWORDS = ("Lucky", "Wizard", "Healer", "Fighter", "Thief", "Hybrid", "Forge", "Dauntless", "Valiance",
            "Vainglory", "Lament", "Anima", "Examen", "Vim", "Pneuma", "Penitence", "Avarice", "Arcana",
            "Ravenous", "Elysium", "Acheron", "Praxis", "Absolution")


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "MoVibeSkua data builder; personal use"})
    return urllib.request.urlopen(req, timeout=60).read().decode("utf-8", "ignore")


def tabs():
    html = fetch(f"https://docs.google.com/spreadsheets/d/{SHEET}/htmlview")
    return sorted(set(re.findall(r'name: "([^"]+)", pageUrl: "[^"]*gid=(\d+)', html)))


def slot(text, table):
    """(enhancement type or None, special or None) for one slot's words."""
    t = text.strip().lower()
    typ = next((x for x in TYPES if t.startswith(x.lower())), None)
    rest = t[len(typ):].strip() if typ else t
    special = None
    for k in sorted(table, key=len, reverse=True):
        if rest == k or rest.startswith(k):
            special = table[k]
            break
    return typ, special


def parse_set(cell):
    lines = [x.strip() for x in cell.replace("\r", "").split("\n") if x.strip()]
    if len(lines) != 2 or lines[0].count("|") != 1 or lines[1].count("|") != 1:
        return None
    weapon, cls = [x.strip() for x in lines[0].split("|")]
    helm, cape = [x.strip() for x in lines[1].split("|")]
    wtype, wspec = slot(weapon, WEAPON)
    ctype, _ = slot(cls, {})
    htype, hspec = slot(helm, HELM)
    _, cspec = slot(cape, CAPE)
    if ctype is None:
        return None
    return {"type": ctype, "weapon": wspec or "None", "helm": hspec or "None", "cape": cspec or "None",
            "text": f"{weapon} | {cls} / {helm} | {cape}"}


def main():
    sets = defaultdict(list)
    fights = defaultdict(list)
    for name, gid in tabs():
        rows = list(csv.reader(io.StringIO(fetch(f"https://docs.google.com/spreadsheets/d/{SHEET}/export?format=csv&gid={gid}"))))
        for r, row in enumerate(rows):
            for c, cell in enumerate(row):
                if "|" not in cell or len(cell) > 120 or not any(k in cell for k in KEYWORDS):
                    continue
                cls = None
                for dr in (-2, -1, 0, -3, 1):
                    rr = r + dr
                    if 0 <= rr < len(rows) and c + 1 < len(rows[rr]):
                        v = rows[rr][c + 1].strip()
                        if v and "★" not in v and "|" not in v:
                            cls = v
                            break
                s = parse_set(cell)
                if cls and s:
                    sets[cls].append(json.dumps({k: s[k] for k in ("type", "weapon", "helm", "cape")}, sort_keys=True))
                    fights[cls].append(name)
    out = {}
    for cls, lst in sets.items():
        best, n = Counter(lst).most_common(1)[0]
        entry = json.loads(best)
        entry["fights"] = n
        entry["of"] = len(lst)
        out[cls.lower()] = entry
    data = {"source": f"https://docs.google.com/spreadsheets/d/{SHEET} (Simplified Bosses Guide by r/AQW)",
            "built": date.today().isoformat(), "classes": dict(sorted(out.items()))}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(data, indent=1), encoding="utf-8")
    print(f"{len(out)} classes -> {OUT}")
    for k, v in sorted(out.items()):
        print(f"  {k}: {v['weapon']} | {v['type']} / {v['helm']} | {v['cape']}  ({v['fights']}/{v['of']} fights)")


if __name__ == "__main__":
    sys.exit(main())
