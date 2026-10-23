"""Bericht je Lauf von AVT_bereinigen.ps1 und Vergleich mit einer Referenz (Regression).

Aufruf: python -I -X utf8 avt_bericht.py <Arbeitsordner> [--referenz <nachher.txt>]
Liest vorher.txt, plan.txt, nachher.txt, nachpruefung.txt, lauf2/konsole.txt; nimmt zweiter/ als Endstand, wenn vorhanden.
Schreibt <Arbeitsordner>/Bericht_AVT.md. Mit --referenz: Vergleich der Kennzahlen (AVTZAHL, AVTKLASSE, AVTNAMEN, AVTNOD);
Rückgabecode 1 bei Abweichung. RegApp- und Wörterbuchnamen stehen nur im Bericht unter C:\\Temp, nie im Repo.
"""
import argparse
import sys
from datetime import datetime
from pathlib import Path

VERGLEICH = ("AVTZAHL ", "AVTKLASSE ", "AVTNAMEN ", "AVTNOD ")


def zeilen(datei: Path) -> list[str]:
    """Zeilen einer Ausgabe; ältere Läufe (ASG tools/ehs, AVT 0.1.0) schrieben „EHS…“ statt „AVT…“."""
    return [("AVT" + z[3:]) if z.startswith("EHS") else z for z in datei.read_text(encoding="utf-8").splitlines()]


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
    return werte.get(f"AVTZAHL {name}", "–").split(" | ")[0]


def namen(wert: str) -> set[str]:
    """Namensliste ohne den Zusatz „gelistet N“ (ältere Werkzeugstände schrieben ihn nicht)."""
    return {n.strip() for n in wert.split(" | ")[0].split(";") if n.strip()}


def vergleiche(ist: dict[str, str], soll: dict[str, str]) -> tuple[list[str], list[str]]:
    """Abweichungen (Referenz-Zeilen, die in AVT fehlen oder anders sind) und Zusatzzeilen nur in AVT (Hinweis)."""
    diffs = []
    for k in sorted(soll):
        if k not in ist:
            diffs.append(f"- `{k}`: fehlt in AVT")
        elif k.startswith("AVTNAMEN "):
            a, r = namen(ist[k]), namen(soll[k])
            if a != r:
                diffs.append(f"- `{k}`: nur AVT {sorted(a - r)} · nur Referenz {sorted(r - a)}")
        elif ist[k] != soll[k]:
            diffs.append(f"- `{k}`: AVT `{ist[k]}` · Referenz `{soll[k]}`")
    return diffs, [f"- `{k}` (nur AVT, in der Referenz nicht ausgegeben)" for k in sorted(set(ist) - set(soll))]


def bericht(ordner: Path, referenz: Path | None) -> int:
    ende = ordner / "zweiter" if (ordner / "zweiter" / "nachher.txt").exists() else ordner
    for d in (ordner / "vorher.txt", ordner / "plan.txt", ende / "nachher.txt"):
        if "AVTENDE ok" not in zeilen(d):
            sys.exit(f"{d}: Lauf nicht mit AVTENDE ok beendet – kein Bericht")
    vor, nach = kennzahlen(ordner / "vorher.txt"), kennzahlen(ende / "nachher.txt")
    md = [f"# Bericht AVT Bereinigen", "",
          f"Stand {datetime.now():%d.%m.%Y %H:%M} · `tools/AVT_bereinigen.ps1` (headless)", "",
          f"- Arbeitsordner: `{ordner}`", f"- Endstand: `{ende}`" + (" (zweiter Durchgang)" if ende != ordner else ""),
          f"- AUDIT am Ergebnis: {audit(ende)}", "", "## Kennzahlen", "", "| Kategorie | vorher | nachher |", "|---|---:|---:|"]
    for name in ("Objekte gesamt", "Eigenschaftssatzdefinitionen", "Anzeigethemen", "Symbolstile", "Symbolstile mit Beschreibung",
                 "Systemdefinitionen", "Blöcke", "Layer", "Linientypen", "Textstile", "Bemassungsstile", "RegApps", "Layouts"):
        md.append(f"| {name} | {zahl(vor, name)} | {zahl(nach, name)} |")
    md += ["", "## Plan (Durchgang 1)", ""]
    md += [f"- {z[len('AVTPLANZAHL '):]}" for z in zeilen(ordner / "plan.txt") if z.startswith("AVTPLANZAHL ")]
    md += [f"- {z}" for z in zeilen(ordner / "plan.txt") if z.startswith(("AVTERGEBNIS", "AVTRESTREGAPP", "AVTWARNUNG", "AVTFEHLER"))]
    schutz = [z for z in zeilen(ordner / "plan.txt") if z.startswith("AVTSCHUTZ ")]
    md += ["", f"## Geschützt (bleibt wegen Verweis): {len(schutz)}", ""] + [f"- {z[len('AVTSCHUTZ '):]}" for z in schutz]
    md += ["", "## Nachprüfung (Object-Enabler, Restspuren)", ""]
    for o in [ordner] + ([ende] if ende != ordner else []):
        datei = o / "nachpruefung.txt"
        if datei.exists():
            md.append(f"**{o.name}**")
            md += [f"- {z[len('AVTNACH '):]}" for z in zeilen(datei)]
    rc = 0
    if referenz:
        diffs, zusatz = vergleiche(nach, kennzahlen(referenz))
        md += ["", f"## Vergleich mit Referenz `{referenz}`", ""]
        md += diffs if diffs else ["Keine Abweichung in AVTZAHL, AVTKLASSE, AVTNAMEN, AVTNOD."]
        md += zusatz
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
