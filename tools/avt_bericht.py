"""Bericht je Lauf von AVT_bereinigen.ps1 und Vergleich mit einer Referenz (Regression).

Aufruf: python -I -X utf8 avt_bericht.py <Arbeitsordner> [--referenz <nachher.txt>]
Liest vorher.txt, plan.txt, nachher.txt, nachpruefung.txt, lauf2/konsole.txt; nimmt zweiter/ als Endstand, wenn vorhanden.
Schreibt <Arbeitsordner>/Bericht_AVT.md. Mit --referenz: Vergleich der Kennzahlen (EHSZAHL, EHSKLASSE, EHSNAMEN, EHSNOD);
Rückgabecode 1 bei Abweichung. RegApp- und Wörterbuchnamen stehen nur im Bericht unter C:\\Temp, nie im Repo.
"""
import argparse
import sys
from datetime import datetime
from pathlib import Path

VERGLEICH = ("EHSZAHL ", "EHSKLASSE ", "EHSNAMEN ", "EHSNOD ")


def zeilen(datei: Path) -> list[str]:
    return datei.read_text(encoding="utf-8").splitlines()


def felder(zeile: str, vorsilbe: str) -> list[str]:
    return [t.strip() for t in zeile[len(vorsilbe):].split("|")]


def kennzahlen(datei: Path) -> dict[str, str]:
    """Schlüssel „Art Name“ → Wert, für alle Vergleichszeilen."""
    aus = {}
    for z in zeilen(datei):
        for vs in VERGLEICH:
            if z.startswith(vs):
                f = felder(z, vs)
                aus[f"{vs.strip()} {f[0]}"] = " | ".join(f[1:])
    return aus


def audit(ordner: Path) -> str:
    konsole = ordner / "lauf2" / "konsole.txt"
    if not konsole.exists():
        return "AUDIT-Protokoll fehlt"
    return next((z.strip() for z in zeilen(konsole) if "Fehler gefunden" in z), "AUDIT-Zeile fehlt")


def zahl(werte: dict[str, str], name: str) -> str:
    return werte.get(f"EHSZAHL {name}", "–").split(" | ")[0]


def vergleiche(ist: dict[str, str], soll: dict[str, str]) -> list[str]:
    diffs = []
    for k in sorted(set(ist) | set(soll)):
        if ist.get(k) != soll.get(k):
            diffs.append(f"- `{k}`: AVT `{ist.get(k, 'fehlt')}` · Referenz `{soll.get(k, 'fehlt')}`")
    return diffs


def bericht(ordner: Path, referenz: Path | None) -> int:
    ende = ordner / "zweiter" if (ordner / "zweiter" / "nachher.txt").exists() else ordner
    for d in (ordner / "vorher.txt", ordner / "plan.txt", ende / "nachher.txt"):
        if "EHSENDE ok" not in zeilen(d):
            sys.exit(f"{d}: Lauf nicht mit EHSENDE ok beendet – kein Bericht")
    vor, nach = kennzahlen(ordner / "vorher.txt"), kennzahlen(ende / "nachher.txt")
    md = [f"# Bericht AVT Bereinigen", "",
          f"Stand {datetime.now():%d.%m.%Y %H:%M} · AVT 0.1.0 · `tools/AVT_bereinigen.ps1` (headless)", "",
          f"- Arbeitsordner: `{ordner}`", f"- Endstand: `{ende}`" + (" (zweiter Durchgang)" if ende != ordner else ""),
          f"- AUDIT am Ergebnis: {audit(ende)}", "", "## Kennzahlen", "", "| Kategorie | vorher | nachher |", "|---|---:|---:|"]
    for name in ("Objekte gesamt", "Eigenschaftssatzdefinitionen", "Anzeigethemen", "Symbolstile", "Symbolstile mit Beschreibung",
                 "Systemdefinitionen", "Blöcke", "Layer", "Linientypen", "Textstile", "Bemassungsstile", "RegApps", "Layouts"):
        md.append(f"| {name} | {zahl(vor, name)} | {zahl(nach, name)} |")
    md += ["", "## Plan (Durchgang 1)", ""]
    md += [f"- {z[len('EHSPLANZAHL '):]}" for z in zeilen(ordner / "plan.txt") if z.startswith("EHSPLANZAHL ")]
    md += [f"- {z}" for z in zeilen(ordner / "plan.txt") if z.startswith(("EHSERGEBNIS", "EHSRESTREGAPP", "EHSWARNUNG", "EHSFEHLER"))]
    schutz = [z for z in zeilen(ordner / "plan.txt") if z.startswith("EHSSCHUTZ ")]
    md += ["", f"## Geschützt (bleibt wegen Verweis): {len(schutz)}", ""] + [f"- {z[len('EHSSCHUTZ '):]}" for z in schutz]
    md += ["", "## Nachprüfung (Object-Enabler, Restspuren)", ""]
    for o in [ordner] + ([ende] if ende != ordner else []):
        datei = o / "nachpruefung.txt"
        if datei.exists():
            md.append(f"**{o.name}**")
            md += [f"- {z[len('AVTNACH '):]}" for z in zeilen(datei)]
    rc = 0
    if referenz:
        diffs = vergleiche(nach, kennzahlen(referenz))
        md += ["", f"## Vergleich mit Referenz `{referenz}`", ""]
        md += diffs if diffs else ["Keine Abweichung in EHSZAHL, EHSKLASSE, EHSNAMEN, EHSNOD."]
        rc = 1 if diffs else 0
    (ordner / "Bericht_AVT.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print(f"Bericht: {ordner / 'Bericht_AVT.md'} · Abweichungen zur Referenz: {'ja' if rc else 'nein'}")
    return rc


if __name__ == "__main__":
    a = argparse.ArgumentParser()
    a.add_argument("ordner", type=Path)
    a.add_argument("--referenz", type=Path)
    args = a.parse_args()
    sys.exit(bericht(args.ordner, args.referenz))
