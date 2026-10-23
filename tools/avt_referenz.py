"""Referenz für Bereinigen/Prüfen aus einem Inhaltsauszug (AVTKANON) erzeugen.

Aufruf: python -I -X utf8 avt_referenz.py <kanon.txt> <Fassung> [<ziel.referenz.txt>]
Übernimmt die benannten Einträge der Fassung: Tabelleneinträge (Blöcke, Layer, Linientypen, Textstile, Bemaßungsstile,
RegApps …) und Einträge im Wörterbuch der Zeichnung bis zur zweiten Ebene (z. B. Satzdefinitionen, Symbolstile).
Ziel ohne Angabe: src/AVT.Kern/Referenz/<Fassung>.referenz.txt (wird in AVT.Kern eingebettet).
"""
import sys
from pathlib import Path

if len(sys.argv) not in (3, 4):
    sys.exit(__doc__)
quelle, fassung = Path(sys.argv[1]), sys.argv[2]
ziel = Path(sys.argv[3]) if len(sys.argv) == 4 else Path(__file__).resolve().parent.parent / "src" / "AVT.Kern" / "Referenz" / f"{fassung}.referenz.txt"
zeilen = quelle.read_text(encoding="utf-8").splitlines()
if "AVTENDE ok" not in zeilen:
    sys.exit(f"{quelle}: Inhaltsauszug nicht mit AVTENDE ok beendet")
gesamt = next(z.split(" | ")[1] for z in zeilen if z.startswith("AVTKANON Gesamt"))
pfade = set()
for z in zeilen:
    if "\t" not in z or z.startswith("#"):
        continue
    teile = z.split("\t")[0].split("/")
    if teile[0].startswith("Ac") and teile[0].endswith("Table#0") and len(teile) == 2:
        pfade.add("/".join(teile))
    elif teile[0] == "NOD" and len(teile) in (2, 3):
        pfade.add("/".join(teile))
with ziel.open("w", encoding="utf-8", newline="\n") as f:
    f.write(f"# Referenz für Bereinigen/Prüfen: benannte Einträge der Fassung {fassung} (Inhaltsauszug {gesamt})\n")
    f.write("# Erzeugt aus dem Inhaltsauszug (AVTKANON) – Tabelleneinträge und Wörterbuch-Einträge bis zur 2. Ebene.\n")
    for p in sorted(pfade):
        f.write(p + "\n")
print(f"{len(pfade)} Pfade → {ziel}")
