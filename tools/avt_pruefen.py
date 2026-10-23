"""Prüfen (AVT 0.2.0): kanonische Inventur einer Zeichnung gegen ihre Fassung (Soll) vergleichen.

Aufruf: python -I -X utf8 avt_pruefen.py <ist-kanon.txt> [--soll <soll-kanon.txt>] [--bericht <datei.md>]
Ohne --soll nur Kopf: Fassungsstempel, DWGPROPS, Pflichtfelder. Mit --soll zusätzlich je Objekt: neu, fehlt, geändert
(nach Klasse gruppiert). Rückgabe 0 = gleich bzw. Kopf vollständig, 1 = Abweichungen, 2 = Eingabe unbrauchbar.
"""
import argparse
import sys
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

BEISPIELE = 8


def lies(datei: Path) -> tuple[dict[str, list[str]], dict[str, tuple[str, str]]]:
    """Kopfzeilen (AVTKANON <Art> | Wert) und Objekte (Pfad → (Klasse, Prüfsumme))."""
    kopf: dict[str, list[str]] = defaultdict(list)
    objekte: dict[str, tuple[str, str]] = {}
    zeilen = datei.read_text(encoding="utf-8").splitlines()
    if "AVTENDE ok" not in zeilen:
        sys.exit(f"{datei}: Inventur nicht mit AVTENDE ok beendet")
    for z in zeilen:
        if z.startswith("AVTKANON "):
            art, _, wert = z[len("AVTKANON "):].partition(" | ")
            kopf[art].append(wert)
        elif "\t" in z and not z.startswith("#"):
            pfad, klasse, summe = z.split("\t")
            objekte[pfad] = (klasse, summe)
    return kopf, objekte


def abschnitt(titel: str, eintraege: list[tuple[str, str]]) -> list[str]:
    if not eintraege:
        return []
    md = ["", f"### {titel}: {len(eintraege)}", "", "| Klasse | Anzahl | Beispiele |", "|---|---:|---|"]
    je = defaultdict(list)
    for pfad, klasse in eintraege:
        je[klasse].append(pfad)
    for klasse, pfade in sorted(je.items(), key=lambda kv: -len(kv[1])):
        bsp = "; ".join(f"`{p}`" for p in sorted(pfade)[:BEISPIELE]) + (" …" if len(pfade) > BEISPIELE else "")
        md.append(f"| {klasse} | {len(pfade)} | {bsp} |")
    return md


def pruefe(ist_datei: Path, soll_datei: Path | None) -> tuple[list[str], int, str]:
    kopf, ist = lies(ist_datei)
    rc = 0
    kurz = []
    md = ["# Prüfbericht AVT", "", f"Stand {datetime.now():%d.%m.%Y %H:%M} · `tools/avt_pruefen.py`", "",
          f"- Zeichnung (Inventur): `{ist_datei}`", f"- Inhaltsprüfsumme: `{kopf['Gesamt'][0]}` · {kopf['Objekte'][0]} Objekte",
          f"- Kopf: {kopf.get('Kopf', ['–'])[0]}", "", "## Fassung", ""]
    md += [f"- Stempel: {f}" for f in kopf.get("Fassung", ["fehlt"])]
    pflicht = kopf.get("Pflichtfelder", ["unbekannt"])[0]
    md.append(f"- DWGPROPS-Pflichtfelder: {pflicht}")
    if pflicht != "alle da":
        rc = 1
    kurz.append(f"Pflichtfelder {pflicht}")
    md += ["", "### DWGPROPS", ""] + [f"- {f}" for f in kopf.get("DWGPROPS", [])]
    if soll_datei:
        skopf, soll = lies(soll_datei)
        md += ["", f"## Vergleich mit der Fassung `{soll_datei}`", "",
               f"- Soll-Prüfsumme `{skopf['Gesamt'][0]}` · Ist `{kopf['Gesamt'][0]}`"]
        if skopf["Gesamt"][0] == kopf["Gesamt"][0]:
            md.append("- **Inhalt gleich der Fassung.**")
            kurz.append("Inhalt gleich der Fassung")
        else:
            rc = 1
            neu = [(p, ist[p][0]) for p in ist if p not in soll]
            fehlt = [(p, soll[p][0]) for p in soll if p not in ist]
            anders = [(p, ist[p][0]) for p in ist if p in soll and ist[p] != soll[p]]
            zahl = Counter({"neu": len(neu), "fehlt": len(fehlt), "geändert": len(anders)})
            md.append(f"- Abweichungen: {zahl['neu']} neu, {zahl['fehlt']} fehlen, {zahl['geändert']} geändert")
            kurz.append(f"Inhalt: {zahl['neu']} neu, {zahl['fehlt']} fehlen, {zahl['geändert']} geändert")
            md += abschnitt("Fehlt gegenüber der Fassung", fehlt)
            md += abschnitt("Neu gegenüber der Fassung", neu)
            md += abschnitt("Geändert", anders)
    return md, rc, " · ".join(kurz)


if __name__ == "__main__":
    a = argparse.ArgumentParser()
    a.add_argument("ist", type=Path)
    a.add_argument("--soll", type=Path)
    a.add_argument("--bericht", type=Path)
    args = a.parse_args()
    if not args.ist.exists() or (args.soll and not args.soll.exists()):
        print("Inventur-Datei fehlt", file=sys.stderr)
        sys.exit(2)
    text, rc, kurz = pruefe(args.ist, args.soll)
    ziel = args.bericht or args.ist.with_name("Pruefbericht_AVT.md")
    ziel.write_text("\n".join(text) + "\n", encoding="utf-8")
    print(f"Prüfbericht: {ziel} · {'Abweichungen' if rc else 'in Ordnung'} · {kurz}")
    sys.exit(rc)
