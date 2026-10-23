# AVT – Vorlagen-Tool · Handbuch

© S. Eichler · Eichler-GmbH · für AutoCAD MEP 2026 · Stand: AVT 0.2.0 (23.10.2026)

AVT pflegt den Eichler-Hausstandard (EHS), die gemeinsame Zeichnungsvorlage für ATT, ADM, ANA und ASG. AVT erzeugt die
Vorlage aus den Stammdaten, prüft Zeichnungen gegen ihre Fassung, bereitet freigegebene Fassungen für die Verteilung
vor, stellt alte Zeichnungen auf den Hausstandard um und bereinigt Zeichnungen von Altlasten wie fremden
Eigenschaftssätzen oder Einträgen von Zusatzprogrammen. Heute (0.2.0) laufen Bereinigen, Prüfen und der
Inhaltsauszug ohne Oberfläche über die Kommandozeile; Palette und Werkzeugkasten-Knopf folgen mit 0.3.0.

---

## Inhalt

1. [Installation und Start](#1-installation-und-start)
2. [Überblick und Arbeitsablauf](#2-überblick-und-arbeitsablauf)
3. [Bereinigen](#3-bereinigen)
4. [Prüfen und Inhaltsauszug](#4-prüfen-und-inhaltsauszug)
5. [API-Sonde](#5-api-sonde)
6. [Befehle](#6-befehle)
7. [Häufige Fragen](#7-häufige-fragen)
8. [Grenzen](#8-grenzen)
9. [Versionsstand](#9-versionsstand)

---

## 1. Installation und Start

AVT besteht heute aus einem Werkzeug für AutoCAD MEP 2026 (`AVT.Werkzeug.dll`) und Ablaufskripten im Ordner `tools`
des AVT-Projekts. Die Skripte bauen das Werkzeug selbst, kopieren die Zeichnung nach `C:\Temp\…` und führen die
Befehle in der AutoCAD-Konsole (`accoreconsole`, ohne Oberfläche) aus.

**Voraussetzungen**

| Was | Wozu |
|---|---|
| AutoCAD MEP 2026 | liefert die AutoCAD-Konsole und die MEP-Bibliotheken |
| .NET SDK 10 | baut das Werkzeug vor jedem Lauf |
| Python 3 | schreibt Berichte und Vergleiche |
| Windows PowerShell | führt die Ablaufskripte aus |

**Regeln für jeden Lauf**

- AVT arbeitet nur an Kopien unter `C:\Temp`. Die Quelle wird nur gelesen; eine Zeichnung des Nutzers wird nie
  geändert oder gespeichert.
- Die Konsole läuft mit eigenem, abgeschottetem Profil (`/isolate`). Die AutoCAD-Einstellungen des Nutzers
  (z. B. `FILEDIA`, `SECURELOAD`) bleiben unberührt.
- Während eines MEP-Tests anderer Module (MEP-Slot, vergeben von AMK) startet AVT keine Konsole.

**Aufruf (Beispiel)**

```
powershell -File tools\AVT_bereinigen.ps1 -Quelle <Zeichnung.dwg> -Arbeit C:\Temp\AVT\Lauf1 -Ergebnis bereinigt.dwg
```

---

## 2. Überblick und Arbeitsablauf

Der Hausstandard entsteht nicht mehr von Hand, sondern aus einem **Bauplan** (`Schema\Bauplan_EHS.md`):

1. **Stammdaten** – Tabellen (CSV, in Excel lesbar) im EHS-Ordner: Systeme, Kürzel, Feldkatalog, Anzeigethemen,
   Layer, Textstile, Umbenennungen. Dazu die Symbolbibliothek, die ASG verwaltet.
2. **Vorlage erzeugen** – AVT baut die Vorlage aus den Stammdaten auf einer unveränderten Autodesk-Leervorlage.
   Gleiche Stammdaten ergeben die gleiche Vorlage; belegt wird das über den Inhaltsauszug (Kapitel 4).
3. **Prüfen** – AVT vergleicht die gebaute Vorlage mit den Stammdaten und mit der Vorgängerfassung.
4. **Freigabe** – nur durch den Nutzer. Danach trägt AVT die Zeichnungseigenschaften (DWGPROPS) ein und legt die
   Fassung schreibgeschützt im EHS-Ordner ab.

**Werkstatt und Live-System**

| Ort | Rolle |
|---|---|
| AVT-Projekt und `C:\Temp\AVT` | Werkstatt: Werkzeug, Bauläufe, Entwürfe `1.0.0-e<n>`, Tests |
| EHS-Ordner (`Vorlagen\CAD\CADStandardEichlerGmbH\Schema`) | Live-System: nur freigegebene Fassungen |

**Versionen der Vorlage:** `x.y.z` – x: etwas wird umbenannt oder fällt weg, y: etwas kommt dazu, z: Korrektur.
Jede Fassung nennt ihre Vorgängerin; keine Nummer wird übersprungen, keine Fassung überschrieben oder gelöscht.
Die erste Live-Fassung ist EHS 1.0.0. EHS 0.0 bis 0.2 sind Entwürfe und bleiben als Belege.

---

## 3. Bereinigen

Bereinigen entfernt Altlasten aus einer Kopie und speichert das Ergebnis als neue Datei:

- alle Objekte im Modellbereich (Layouts bleiben),
- Eigenschaftssatzdefinitionen und Eigenschaftssätze – außer denen, auf die ein bleibendes Objekt zeigt
  (z. B. ein Anzeigethema),
- Bauteiltabellenstile, TAG-Mehransichtsblöcke, Legendenblöcke,
- Blöcke, die nur von Entferntem benutzt wurden,
- Spuren der Module in Wörterbüchern, RegApps und Layern,
- RegApps ohne Benutzer (AutoCAD-eigene `AC*`/`AEC*` bleiben),
- auf Auftrag: Einträge fremder Zusatzprogramme (Vorsilben) und einzelne Anzeigethemen.

Kein pauschales Bereinigen (PURGE): unbenutzte Symbolstile gehören zur Vorlage und bleiben.
Bevor etwas entfernt wird, prüft AVT alle Verweise der Zeichnung. Zeigt ein bleibendes Objekt auf etwas, das weg soll,
bleibt das Ziel stehen und steht im Bericht. Ist das Verweisnetz unvollständig, speichert AVT nichts.

**Aufruf**

```
powershell -File tools\AVT_bereinigen.ps1 -Quelle <dwg> -Arbeit C:\Temp\AVT\<Ordner> -Ergebnis <name>.dwg
    [-FremdeWoerterbuecher <Vorsilbe1>,<Vorsilbe2>] [-Themen '<Name1>;<Name2>'] [-Trocken] [-OhneZweitenDurchgang]
```

| Schalter | Wirkung |
|---|---|
| `-FremdeWoerterbuecher` | Vorsilben von Wörterbucheinträgen fremder Zusatzprogramme, die mit entfernt werden. Groß- und Kleinschreibung zählt. `AC…`/`AEC…` werden abgewiesen. |
| `-Themen` | genaue Namen von Anzeigethemen, die entfernt werden; ein unbekannter Name erscheint als Warnung |
| `-Trocken` | nur Bestandsaufnahme und Entfernplan, nichts wird gespeichert |
| `-OhneZweitenDurchgang` | Befund der Nachprüfung nur melden |

**Ablauf und Bericht**

1. Bestandsaufnahme (`vorher.txt`) und Entfernplan mit Gründen (`plan.txt`), dann Bereinigen und Speichern als neue Datei.
2. Gegenprobe: das Ergebnis wird neu geöffnet, AUDIT (nur melden), Bestandsaufnahme (`nachher.txt`).
3. **Nachprüfung** (`nachpruefung.txt`): Haben Zusatzprogramme beim Speichern Wörterbücher oder RegApps neu angelegt?
   Sind Spuren übrig?
4. Bei Befund folgt automatisch ein **zweiter Durchgang** am Ergebnis (Ordner `zweiter`), danach wird erneut geprüft.
5. Bericht: `python -I -X utf8 tools\avt_bericht.py <Arbeitsordner>` → `Bericht_AVT.md`.

**Warum ein zweiter Durchgang?** Manche Zusatzprogramme (Object-Enabler) laden, sobald ihre Objekte in der Zeichnung
stehen, und legen beim Speichern ihre Wörterbücher wieder an. Im zweiten Durchgang sind diese Objekte schon weg, die
Enabler laden nicht mehr, das Ergebnis bleibt sauber.

---

## 4. Prüfen und Inhaltsauszug

**Inhaltsauszug (kanonische Inventur):** AVT schreibt jedes Objekt der Zeichnung als Textzeile: einen festen Pfad aus
Namen (z. B. `NOD/AEC_DISPLAYTHEME_STYLES/DN Rohrleitung`), die Klasse und eine Prüfsumme über den ganzen Inhalt.
Verweise auf andere Objekte zählen mit ihrem Pfad, nie mit ihrem Handle. Daraus entsteht eine **Inhaltsprüfsumme**.
Zwei Zeichnungen mit gleichem Inhalt haben dieselbe Prüfsumme, auch wenn sie getrennt gebaut wurden. Ändert sich
etwas, zeigt der Vergleich der beiden Textdateien genau, welches Objekt.

Ausgeglichen werden Werte, die sich bei jedem Bau ändern, ohne Inhalt zu sein: Reihenfolge von Verweislisten,
GUIDs dynamischer Blöcke, Versionsstempel von AEC-Objekten. Räumliche Indizes und die DACH-Sanierungsstile zählen
nur mit ihrem Pfad, weil ihr Inhalt schon beim zweiten Lesen derselben Datei anders ausfällt.

```
powershell -File tools\AVT_kanon.ps1 -Zeichnungen <dwg1>,<dwg2> -Arbeit C:\Temp\AVT\<Ordner> [-Audit]
```

**Prüfen:** Inhaltsauszug einer Zeichnung, dazu AUDIT (nur melden) und der Vergleich mit der Fassung.

```
powershell -File tools\AVT_pruefen.ps1 -Zeichnung <dwg> -Arbeit C:\Temp\AVT\<Ordner> [-Soll <kanon.txt der Fassung>]
```

Der Prüfbericht (`Pruefbericht_AVT.md`) zeigt:

- Fassungsstempel der Zeichnung (Wörterbuch `EHS_Zeichnung`) oder „fehlt“,
- die Zeichnungseigenschaften (DWGPROPS) und ob alle Pflichtfelder `EHS_…` gefüllt sind,
- mit `-Soll`: Objekte, die fehlen, neu sind oder sich geändert haben, nach Klasse gruppiert.

---

## 5. API-Sonde

Die Sonde klärt für den Bau der Vorlage, welche Objektarten sich in MEP 2026 ohne Oberfläche per Programm anlegen
lassen. Sie legt an einer Kopie je Objektart ein Beispiel an (Namen beginnen mit `AVT_Sonde`), speichert, öffnet das
Ergebnis neu, prüft mit AUDIT und liest alles zurück.

```
powershell -File tools\AVT_sonde.ps1 -Quelle <dwg oder dwt> -Arbeit C:\Temp\AVT\<Ordner> [-Auslassen '<Teil1>|<Teil2>']
```

Ergebnis in `sonde.txt` (Anlegen) und `lesen.txt` (Zurücklesen). Die Sonde ist ein Entwicklungswerkzeug; ihre
Zeichnung ist kein Ergebnis für die Praxis.

---

## 6. Befehle

Die Befehle laufen in der AutoCAD-Konsole und werden von den Ablaufskripten aufgerufen. Sie arbeiten nur in
Zeichnungen unter `C:\Temp` und schreiben ihre Ausgabe nur dorthin.

| Befehl | Wirkung |
|---|---|
| `AVTINVENTUR` | Bestandsaufnahme und Entfernplan (nur lesen) |
| `AVTBEREINIGEN` | Entfernplan ausführen und als neue Datei speichern |
| `AVTKANON` | Inhaltsauszug mit Inhaltsprüfsumme, Fassungsstempel und DWGPROPS (nur lesen) |
| `AVTSONDE` | API-Sonde: Beispiele je Objektart anlegen und als neue Datei speichern |
| `AVTSONDELESEN` | Beispiele der Sonde zurücklesen (nur lesen) |

---

## 7. Häufige Fragen

**Ändert AVT meine Zeichnung?** Nein. AVT kopiert sie nach `C:\Temp` und arbeitet nur an der Kopie.

**Warum zwei Durchgänge beim Bereinigen?** Siehe Kapitel 3: Zusatzprogramme können beim Speichern ihre Einträge neu
anlegen. Die Nachprüfung zeigt das; der zweite Durchgang entfernt sie endgültig.

**Warum ist die Prüfsumme gleich, obwohl die Dateien verschieden groß sind?** Die Prüfsumme zählt den Inhalt, nicht
die Datei. Reihenfolge, Handles und Zeitstempel spielen keine Rolle.

**Kann ich einen anderen Namen als „EHS“ verwenden?** Ja. Das Kürzel steht an einer Stelle (heute im Werkzeug,
ab dem Erzeugen in `Stammdaten\Standard.csv`). Umschwenken bedeutet eine neue Hauptnummer mit Umbenennungsliste.

---

## 8. Grenzen

- Heute nur über die Kommandozeile; Palette und Knopf kommen mit 0.3.0.
- Bereinigen entfernt Zusatzprogramm-Einträge nur aus der Zeichnung. Ist das Zusatzprogramm auf dem Rechner
  angemeldet und kommen seine Objekte zurück (z. B. durch Einfügen aus alten Zeichnungen), legt es seine Einträge
  wieder an. Endgültig hilft nur, die Zeichnung ohne das Zusatzprogramm zu bearbeiten.
- Die Inhaltsprüfsumme übergeht den Inhalt räumlicher Indizes und der DACH-Sanierungsstile (siehe Kapitel 4).
- AUDIT meldet bei der Autodesk-Leervorlage „Aecb Model (D A CH Ctb) mm“ schon im Auslieferungszustand 2 Fehler
  (Listen-Definition „Standard“); diese stammen nicht von AVT.

---

## 9. Versionsstand

| Version | Datum | Inhalt |
|---|---|---|
| 0.2.0 | 23.10.2026 | Inhaltsauszug mit Inhaltsprüfsumme (`AVTKANON`), Prüfen gegen eine Fassung, API-Sonde, DWGPROPS lesen, Kürzel des Standards an einer Stelle, Berichtszeilen mit Präfix „AVT“ |
| 0.1.0 | 23.10.2026 | Bereinigen aus ASG übernommen (gleiches Ergebnis wie EHS 0.1/0.2 in 3 Läufen), eigene Konsolen-Anbindung, Nachprüfung mit automatischem zweitem Durchgang |
