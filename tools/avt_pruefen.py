"""Prüfen (AVT 0.2.0): kanonische Inventur einer Zeichnung gegen ihre Fassung (Soll) vergleichen.

Aufruf: python -I -X utf8 avt_pruefen.py <ist-kanon.txt> [--soll <soll-kanon.txt>] [--bericht <datei.md>]
Ohne --soll nur Kopf: Fassungsstempel, DWGPROPS, Pflichtfelder, Lesefehler. Mit --soll zusätzlich: Kopfwerte (Einheiten …),
eigene DWGPROPS-Felder (Präfix aus der Inventur) und je Objekt neu / fehlt / geändert (nach Klasse gruppiert).
Rückgabe 0 = in Ordnung, 1 = Abweichungen oder Warnungen, 2 = Eingabe unbrauchbar.
"""
import argparse
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

BEISPIELE = 8
PFLICHT_KOPF = ("Gesamt", "Objekte")


class Unbrauchbar(Exception):
    """Inventur fehlt, ist unvollständig oder hat ein falsches Format."""


def lies(datei: Path) -> tuple[dict[str, list[str]], dict[str, tuple[str, str]]]:
    """Kopfzeilen (AVTKANON <Art> | Wert) und Objekte (Pfad → (Klasse, Prüfsumme))."""
    if not datei.exists():
        raise Unbrauchbar(f"{datei}: fehlt")
    kopf: dict[str, list[str]] = defaultdict(list)
    objekte: dict[str, tuple[str, str]] = {}
    zeilen = datei.read_text(encoding="utf-8").splitlines()
    if "AVTENDE ok" not in zeilen:
        raise Unbrauchbar(f"{datei}: Inventur nicht mit AVTENDE ok beendet")
    for nr, z in enumerate(zeilen, 1):
        if z.startswith("AVTKANON "):
            art, _, wert = z[len("AVTKANON "):].partition(" | ")
            kopf[art].append(wert)
        elif "\t" in z and not z.startswith("#"):
            teile = z.split("\t")
            if len(teile) != 3:
                raise Unbrauchbar(f"{datei}:{nr}: Objektzeile mit {len(teile)} statt 3 Feldern")
            if teile[0] in objekte:
                raise Unbrauchbar(f"{datei}:{nr}: Pfad doppelt: {teile[0]}")
            objekte[teile[0]] = (teile[1], teile[2])
    fehlend = [k for k in PFLICHT_KOPF if k not in kopf]
    if fehlend:
        raise Unbrauchbar(f"{datei}: Kopfzeilen fehlen: {', '.join(fehlend)}")
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


def eigene_felder(kopf: dict[str, list[str]]) -> dict[str, str]:
    """Benutzerdefinierte DWGPROPS mit dem Präfix des Standards (z. B. EHS_…), aus der Inventur."""
    praefix = kopf.get("Feldpraefix", [""])[0]
    felder = {}
    for f in kopf.get("DWGPROPS", []):
        if f.startswith("*") and praefix and f[1:].startswith(praefix):
            name, _, wert = f[1:].partition(" = ")
            felder[name] = wert
    return felder


def zahl(kopf: dict[str, list[str]], art: str) -> int:
    try:
        return int(kopf.get(art, ["0"])[0])
    except ValueError:
        return 0


def pruefe(ist_datei: Path, soll_datei: Path | None) -> tuple[list[str], int, str]:
    kopf, ist = lies(ist_datei)
    rc = 0
    kurz = []
    md = ["# Prüfbericht AVT", "", f"Stand {datetime.now():%d.%m.%Y %H:%M} · `tools/avt_pruefen.py`", "",
          f"- Zeichnung (Inventur): `{ist_datei}`", f"- Inhaltsprüfsumme: `{kopf['Gesamt'][0]}` · {kopf['Objekte'][0]} Objekte",
          f"- Kopf: {kopf.get('Kopf', ['–'])[0]}"]
    # Lesefehler: deren Inhalt zählt nicht in der Prüfsumme – nie stillschweigend „gleich“ melden (Review 23.10.2026).
    unlesbar, fehler, instabil = zahl(kopf, "Unlesbar"), zahl(kopf, "Fehler"), zahl(kopf, "Instabil")
    md.append(f"- Nicht lesbar: {unlesbar} · Lesefehler: {fehler} · nur mit Pfad gezählt (instabile Klassen): {instabil}")
    if unlesbar or fehler:
        rc = 1
        kurz.append(f"WARNUNG {unlesbar} unlesbar, {fehler} Lesefehler")
        md += [f"  - Lesefehler: `{f}`" for f in kopf.get("FehlerObjekt", [])]
    md += ["", "## Fassung", ""] + [f"- Stempel: {f}" for f in kopf.get("Fassung", ["fehlt"])]
    pflicht = kopf.get("Pflichtfelder", ["unbekannt"])[0]
    md.append(f"- DWGPROPS-Pflichtfelder: {pflicht}")
    if pflicht != "alle da":
        rc = 1
    kurz.append(f"Pflichtfelder {pflicht}")
    md += ["", "### DWGPROPS", ""] + [f"- {f}" for f in kopf.get("DWGPROPS", [])]
    if soll_datei:
        skopf, soll = lies(soll_datei)
        md += ["", f"## Vergleich mit der Fassung `{soll_datei}`", ""]
        if kopf.get("Kopf") != skopf.get("Kopf"):
            rc = 1
            kurz.append("Kopfwerte verschieden")
            md.append(f"- **Kopfwerte verschieden:** Ist {kopf.get('Kopf', ['–'])[0]} · Soll {skopf.get('Kopf', ['–'])[0]}")
        ef, sf = eigene_felder(kopf), eigene_felder(skopf)
        anders = sorted(k for k in set(ef) | set(sf) if ef.get(k) != sf.get(k))
        if anders:
            rc = 1
            kurz.append(f"{len(anders)} eigene DWGPROPS-Felder verschieden")
            md += ["- **Eigene DWGPROPS-Felder verschieden:**"] + [f"  - {k}: Ist „{ef.get(k, 'fehlt')}“ · Soll „{sf.get(k, 'fehlt')}“" for k in anders]
        md.append(f"- Soll-Prüfsumme `{skopf['Gesamt'][0]}` · Ist `{kopf['Gesamt'][0]}`")
        if skopf["Gesamt"][0] == kopf["Gesamt"][0]:
            md.append("- **Inhalt gleich der Fassung.**")
            kurz.append("Inhalt gleich der Fassung")
        else:
            rc = 1
            neu = [(p, ist[p][0]) for p in ist if p not in soll]
            fehlt = [(p, soll[p][0]) for p in soll if p not in ist]
            geaendert = [(p, ist[p][0]) for p in ist if p in soll and ist[p] != soll[p]]
            md.append(f"- Abweichungen: {len(neu)} neu, {len(fehlt)} fehlen, {len(geaendert)} geändert")
            kurz.append(f"Inhalt: {len(neu)} neu, {len(fehlt)} fehlen, {len(geaendert)} geändert")
            md += abschnitt("Fehlt gegenüber der Fassung", fehlt)
            md += abschnitt("Neu gegenüber der Fassung", neu)
            md += abschnitt("Geändert", geaendert)
    return md, rc, " · ".join(kurz)


if __name__ == "__main__":
    a = argparse.ArgumentParser()
    a.add_argument("ist", type=Path)
    a.add_argument("--soll", type=Path)
    a.add_argument("--bericht", type=Path)
    args = a.parse_args()
    try:
        text, rc, kurz = pruefe(args.ist, args.soll)
    except Unbrauchbar as fehler:
        print(f"Prüfen nicht möglich: {fehler}", file=sys.stderr)
        sys.exit(2)
    ziel = args.bericht or args.ist.with_name("Pruefbericht_AVT.md")
    ziel.write_text("\n".join(text) + "\n", encoding="utf-8")
    print(f"Prüfbericht: {ziel} · {'Abweichungen' if rc else 'in Ordnung'} · {kurz}")
    sys.exit(rc)
