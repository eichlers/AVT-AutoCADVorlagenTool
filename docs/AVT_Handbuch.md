# AVT – Vorlagen-Tool · Handbuch

© S. Eichler · Eichler-GmbH · für AutoCAD MEP 2026 · Stand: AVT 0.3.0 (23.10.2026)

AVT pflegt den Eichler-Hausstandard (EHS), die gemeinsame Zeichnungsvorlage für ATT, ADM, ANA und ASG. AVT erzeugt die
Vorlage aus den Stammdaten, prüft Zeichnungen gegen ihre Fassung, bereitet freigegebene Fassungen für die Verteilung
vor, stellt alte Zeichnungen auf den Hausstandard um und bereinigt Zeichnungen von Altlasten wie fremden
Eigenschaftssätzen, alten Stempelblöcken oder Einträgen von Zusatzprogrammen. Seit 0.3.0 gibt es dafür eine Palette in
AutoCAD MEP; das Bereinigen der Vorlage und der Inhaltsauszug laufen weiter auch ohne Oberfläche.

---

## Inhalt

1. [Installation und Start](#1-installation-und-start)
2. [Die Palette im Überblick](#2-die-palette-im-überblick)
3. [Überblick und Grundlagen](#3-überblick-und-grundlagen)
4. [Bereinigen der offenen Zeichnung](#4-bereinigen-der-offenen-zeichnung)
5. [Bereinigen der Vorlage (ohne Oberfläche)](#5-bereinigen-der-vorlage-ohne-oberfläche)
6. [Prüfen und Inhaltsauszug](#6-prüfen-und-inhaltsauszug)
7. [API-Sonde](#7-api-sonde)
8. [Befehle](#8-befehle)
9. [Häufige Fragen](#9-häufige-fragen)
10. [Grenzen](#10-grenzen)
11. [Versionsstand](#11-versionsstand)

---

## 1. Installation und Start

AVT liegt als AutoCAD-Bundle in `%APPDATA%\Autodesk\ApplicationPlugins\AVT.bundle` (Inhalt: `AVT.Mep.dll`,
`AVT.Kern.dll`) und lädt sich beim Start von AutoCAD MEP 2026 selbst – kein NETLOAD nötig. Das Bundle lädt nur in
AutoCAD MEP 2026. Installiert wird bei geschlossenem MEP (`tools\AVT_bundle.ps1 -Installieren`), abgestimmt über AMK.

- **Werkzeugkasten:** Beim ersten Start legt AVT den Werkzeugkasten „AVT“ an (Teil-Menü `%APPDATA%\AVT\AVT.cuix`,
  oben angedockt) – ein Knopf, anthrazitfarbenes Quadrat mit „AVT“. Tooltip: „AVT – Vorlagen-Tool (Palette öffnen)“.
  Wird der Werkzeugkasten entladen, bleibt er weg; `AVTWERKZEUGKASTEN` holt ihn zurück.
- **Palette:** öffnet mit dem Knopf oder mit `AVTPALETTE` (kurz `AVT`) – immer sichtbar und ausgeklappt, auch wenn sie
  angedockt auf „Automatisch ausblenden“ steht. Beim Start von AutoCAD bleibt sie zu, auch wenn sie beim Beenden offen
  war. Unten (bei der Befehlszeile) dockt sie nicht an. Die Palette trägt dasselbe Symbol wie der Knopf.
- **Hinweis beim Öffnen:** AVT prüft jede geöffnete Zeichnung leise im Leerlauf (gemessen: gut 1 s bei 40 000 Objekten)
  und meldet einmal in der Befehlszeile, wenn Inhalte nicht zur Fassung gehören, z. B.
  „AVT: 206 Fremdinhalte, 44 benutzt (nicht in der Fassung) – Palette AVT → „Bereinigen …“ zeigt sie.“ Kein Dialog,
  nichts wird geändert. Abschaltbar im Zahnrad.
- **Protokoll und Einstellungen:** `%APPDATA%\AVT\AVT_sitzung.log`, `%APPDATA%\AVT\AVT_einstellungen.json`.

**Werkzeuge ohne Oberfläche** (Bau und Prüfung der Vorlage): Ablaufskripte im Ordner `tools` des AVT-Projekts. Sie
bauen `AVT.Werkzeug.dll`, kopieren die Zeichnung nach `C:\Temp\…` und führen die Befehle in der AutoCAD-Konsole
(`accoreconsole`, eigenes abgeschottetes Profil `/isolate`) aus. Voraussetzungen: AutoCAD MEP 2026, .NET SDK 10,
Python 3, Windows PowerShell. Während eines MEP-Tests anderer Module (MEP-Slot, vergeben von AMK) startet AVT keine
Konsole.

---

## 2. Die Palette im Überblick

| Bereich | Inhalt |
|---|---|
| **AVT ⓘ** | Kurzbeschreibung, Version und Stand als Tooltip |
| **⚙ Zahnrad** | Version, Stand (Builddatum), © S. Eichler · Eichler-GmbH, Eichler-Logo, Versionshistorie, Einstellung „Hinweis beim Öffnen“ |
| **Diese Zeichnung** | Name, Fassung (aus dem Fassungsstempel bzw. den Zeichnungseigenschaften) – oder „Keine EHS-Vorlage“, dann vergleicht AVT mit der Live-Fassung; darunter der Hinweis auf Fremdinhalte |
| **Prüfen** | nur lesen: Fassungsstempel, DWGPROPS-Pflichtfelder, Fremdinhalte (a/b/c), Inhaltsprüfsumme |
| **Bereinigen …** | Vorschau der Fremdinhalte, dann auf Wunsch bereinigen (Kapitel 4) |
| **Bericht** | Ergebnis von Prüfen und Bereinigen, auch im Sitzungsprotokoll |

AVT läuft auch ohne EHS-Vorlage: Ohne Fassungsstempel gibt es einen Hinweis, keinen Abbruch.

---

## 3. Überblick und Grundlagen

Der Hausstandard entsteht nicht mehr von Hand, sondern aus einem **Bauplan** (`Schema\Bauplan_EHS.md`):

1. **Stammdaten** – Tabellen (CSV, in Excel lesbar) im EHS-Ordner: Systeme, Kürzel, Feldkatalog, Anzeigethemen,
   Layer, Textstile, Umbenennungen. Dazu die Symbolbibliothek, die ASG verwaltet.
2. **Vorlage erzeugen** – AVT baut die Vorlage aus den Stammdaten auf einer unveränderten Autodesk-Leervorlage.
   Gleiche Stammdaten ergeben die gleiche Vorlage; belegt wird das über den Inhaltsauszug (Kapitel 6).
3. **Prüfen** – AVT vergleicht die gebaute Vorlage mit den Stammdaten und mit der Vorgängerfassung.
4. **Freigabe** – nur durch den Nutzer. Danach trägt AVT die Zeichnungseigenschaften (DWGPROPS) ein und legt die
   Fassung schreibgeschützt im EHS-Ordner ab.

**Normgrundlage:** Die Stile und das Tagging sind auf die DIN EN IEC 81346-2:2020-10 aufgebaut bzw. angelehnt
(Klassifizierung von Objekten und Kennbuchstaben). Die Unterklassen und Bauarten stehen in den Stammdaten des
EHS-Ordners; AVT soll Zeichnungen später auch gegen diese Tabellen prüfen.

**Werkstatt und Live-System**

| Ort | Rolle |
|---|---|
| AVT-Projekt und `C:\Temp\AVT` | Werkstatt: Werkzeug, Bauläufe, Entwürfe `1.0.0-e<n>`, Tests |
| EHS-Ordner (`Vorlagen\CAD\CADStandardEichlerGmbH\Schema`) | Live-System: nur freigegebene Fassungen |

**Versionen der Vorlage:** `x.y.z` – x: etwas wird umbenannt oder fällt weg, y: etwas kommt dazu, z: Korrektur.
Jede Fassung nennt ihre Vorgängerin; keine Nummer wird übersprungen, keine Fassung überschrieben oder gelöscht.
Die erste Live-Fassung ist EHS 1.0.0. EHS 0.0 bis 0.2 sind Entwürfe und bleiben als Belege.

---

## 4. Bereinigen der offenen Zeichnung

Fremd ist alles, was nicht zur Fassung der Zeichnung gehört – nicht nur Einträge von Zusatzprogrammen, auch alte
Stempel- und Legendenblöcke, fremde Eigenschaftssätze, Dubletten wie „ANA_Hydraulik (2)“ oder unbenutzte Stile.
Nie als fremd gelten: Einträge von AutoCAD/MEP selbst, Einträge der Module (ATT, ADM, ANA, ASG, AVT, EHS) und Namen
wie „0“ oder „Standard“. Zusatzprogramme erkennt AVT an ihren Objektklassen, nicht an ihrem Namen.

**Ablauf**

1. **Vorschau** („Bereinigen …“): drei Gruppen, nichts wird geändert.

   | Gruppe | Bedeutung | Vorgabe |
   |---|---|---|
   | a) fremd und unbenutzt | Blöcke, Stile, Satzdefinitionen, Layer, Text-/Linientypen, RegApps, Wörterbücher, Dubletten | angehakt – wird entfernt |
   | b) fremd und benutzt | z. B. eingefügte alte Stempelblöcke, `TAGBauteil`-Sätze an Objekten | nicht angehakt – angehakt = als Ausnahme merken |
   | c) Projektinhalt | eigene Blöcke, Plankopf, Legenden, Tabellenstile ohne Fremdspuren | nur Information |

   Was nur von anderem Unbenutztem benutzt wird (z. B. ein Block in einem unbenutzten fremden Block), zählt als unbenutzt.
2. **Ausgewählte bereinigen …**: AVT verlangt eine gespeicherte Zeichnung und fragt einmal nach.
3. **Sicherungskopie** neben der Zeichnung: `<Name>_vor-Bereinigen_<JJJJMMTT_hhmm>.dwg`, Prüfsumme mit dem Original
   verglichen. Gelingt sie nicht, wird nichts geändert.
4. **Bereinigen**: AVT liest die Zeichnung neu und entfernt nur, was jetzt noch fremd und unbenutzt ist.
   Wörterbucheinträge werden ausgetragen, RegApps nur, soweit AutoCAD sie freigibt. Ein Fehler → nichts wird entfernt.
5. **Nachprüfung** und **Bericht** in der Palette. **Speichern macht der Nutzer** – AVT speichert nie.

Ausnahmen (b behalten) stehen in der Zeichnung im Wörterbuch `AVT_Ausnahmen`; der Hinweis meldet sie dort nicht mehr.

**Nachweis ohne Oberfläche** (gleicher Kern wie die Palette):

```
powershell -File tools\AVT_fremd.ps1 -Quelle <dwg> -Arbeit C:\Temp\AVT\<Ordner> [-Bereinigen [-AusnahmenMerken]]
```

---

## 5. Bereinigen der Vorlage (ohne Oberfläche)

Dieses Bereinigen ist für den Bau der Vorlage gedacht: Es entfernt Altlasten aus einer Kopie unter `C:\Temp` und
speichert das Ergebnis als neue Datei. Für Projektzeichnungen gibt es das Bereinigen in der Palette (Kapitel 4).

Entfernt wird:

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

## 6. Prüfen und Inhaltsauszug

**Inhaltsauszug (kanonische Inventur):** AVT schreibt jedes Objekt der Zeichnung als Textzeile: einen festen Pfad aus
Namen (z. B. `NOD/AEC_DISPLAYTHEME_STYLES/DN Rohrleitung`), die Klasse und eine Prüfsumme über den ganzen Inhalt.
Verweise auf andere Objekte zählen mit ihrem Pfad, nie mit ihrem Handle. Daraus entsteht eine **Inhaltsprüfsumme**.
Zwei Zeichnungen mit gleichem Inhalt haben dieselbe Prüfsumme, auch wenn sie getrennt gebaut wurden. Ändert sich
etwas, zeigt der Vergleich der beiden Textdateien genau, welches Objekt.

Ausgeglichen werden Werte, die sich bei jedem Bau ändern, ohne Inhalt zu sein: Reihenfolge von Verweislisten,
GUIDs dynamischer Blöcke, Versionsstempel von AEC-Objekten, Nummern anonymer Blöcke und anonymer AEC-Einträge
(`*U12`, `*A113` – sie heißen im Auszug `*U#0`, `*A#0` … nach ihrer Reihenfolge). Räumliche Indizes und die DACH-Sanierungsstile zählen
nur mit ihrem Pfad, weil ihr Inhalt schon beim zweiten Lesen derselben Datei anders ausfällt.

```
powershell -File tools\AVT_kanon.ps1 -Zeichnungen '<dwg1>;<dwg2>' -Arbeit C:\Temp\AVT\<Ordner> [-Audit]
```

**Prüfen:** Inhaltsauszug einer Zeichnung, danach AUDIT (nur melden) und der Vergleich mit der Fassung. Der Auszug
kommt immer vor AUDIT: AUDIT berechnet Schraffuren neu, auch wenn es nichts beheben soll, und würde den Auszug
verfälschen.

```
powershell -File tools\AVT_pruefen.ps1 -Zeichnung <dwg> -Arbeit C:\Temp\AVT\<Ordner> [-Soll <kanon.txt der Fassung>]
```

Der Prüfbericht (`Pruefbericht_AVT.md`) zeigt:

- Fassungsstempel der Zeichnung (Wörterbuch `EHS_Zeichnung`) oder „fehlt“,
- die Zeichnungseigenschaften (DWGPROPS) und ob alle Pflichtfelder `EHS_…` gefüllt sind,
- Objekte, die AutoCAD nicht lesen konnte (Warnung – ihr Inhalt zählt nicht in der Prüfsumme),
- mit `-Soll`: abweichende Kopfwerte (Einheiten, Plotstil-Art, Linientypfaktor), abweichende eigene DWGPROPS-Felder
  (`EHS_…`) und Objekte, die fehlen, neu sind oder sich geändert haben, nach Klasse gruppiert.

Rückgabe des Skripts: 0 = in Ordnung, 1 = Abweichungen oder Warnungen, 2 = Prüfen nicht möglich (Inventur fehlt oder
ist unvollständig). Ausgaben früherer Läufe löscht AVT vor jedem Lauf; ein abgebrochener Lauf meldet sich als Fehler,
nie als altes Ergebnis.

---

## 7. API-Sonde

Die Sonde klärt für den Bau der Vorlage, welche Objektarten sich in MEP 2026 ohne Oberfläche per Programm anlegen
lassen. Sie legt an einer Kopie je Objektart ein Beispiel an (Namen beginnen mit `AVT_Sonde`), speichert, öffnet das
Ergebnis neu, prüft mit AUDIT und liest alles zurück.

```
powershell -File tools\AVT_sonde.ps1 -Quelle <dwg oder dwt> -Arbeit C:\Temp\AVT\<Ordner> [-Auslassen '<Teil1>|<Teil2>']
```

Ergebnis in `sonde.txt` (Anlegen) und `lesen.txt` (Zurücklesen). Die Sonde ist ein Entwicklungswerkzeug; ihre
Zeichnung ist kein Ergebnis für die Praxis.

**Stand 0.2.0 (MEP 2026, ohne Oberfläche, nach Speichern und Neuöffnen zurückgelesen, AUDIT 0 an EHS 0.2):**

| Objektart | per Programm | Bemerkung |
|---|---|---|
| Layer, Linientypen (Strichmuster oder aus `acadiso.lin`), Textstile, Bemaßungsstile, Layouts | ja | |
| AutoCAD-Tabellenstil | ja | als Kopie von „Standard“; neu angelegt ergab er AUDIT-Fehler |
| Eigenschaftssatzdefinition mit Text-, Zahl-, Ja/Nein-Feldern | ja | |
| Formelfeld | ja | |
| Automatisches Feld | ja | Quellen mit deutschem Namen (z. B. „Farbe“), Liste je Objektklasse von AutoCAD |
| Layer-Schlüsselstil | ja | neu angelegt kommt er mit den 113 Standardschlüsseln |
| Schemasystem (Systemdefinition) | ja | Kürzel, Gruppe, Layer-Schlüssel |
| Mehransichtsblock mit Ansichtsblock | ja | Darstellung wählbar aus den Darstellungen der Zeichnung |
| Bauteiltabellenstil mit Spalten | ja | Formate (Höhe, Textstil, Ausrichtung) einstellbar |
| Anzeigethema mit Regel (Satz, Feld, Bedingung) | ja | |
| Farbe einer Themen-Komponente | teilweise | an einer kopierten Komponente ja; eine neu angelegte Komponente hat keine Darstellungseigenschaften |
| Anzeigesatz | teilweise | leer anlegbar; Inhalt kommt aus der Leervorlage |
| Anzeigekonfiguration | aus der Leervorlage | |
| Zeichnungseigenschaften (DWGPROPS) mit eigenen Feldern | ja | |

---

## 8. Befehle

**In AutoCAD MEP (Plugin)**

| Befehl | Wirkung |
|---|---|
| `AVTPALETTE`, `AVT` | Palette öffnen (sichtbar und ausgeklappt) |
| `AVTWERKZEUGKASTEN` | Werkzeugkasten „AVT“ neu anlegen und oben andocken |
| `AVTPALETTESTART` | tut nichts (AutoCAD ruft ihn beim Start für eine beim Beenden offene Palette auf) |

**In der AutoCAD-Konsole (Werkzeuge ohne Oberfläche, nur Zeichnungen unter `C:\Temp`)**

| Befehl | Wirkung |
|---|---|
| `AVTINVENTUR` | Bestandsaufnahme und Entfernplan der Vorlage (nur lesen) |
| `AVTBEREINIGEN` | Entfernplan ausführen und als neue Datei speichern |
| `AVTKANON` | Inhaltsauszug mit Inhaltsprüfsumme, Fassungsstempel und DWGPROPS (nur lesen) |
| `AVTFREMD` | Fremdinhalte a/b/c (nur lesen) |
| `AVTFREMDBEREINIGEN` | alle a entfernen, b auf Wunsch als Ausnahme merken, als neue Datei speichern |
| `AVTSONDE` | API-Sonde: Beispiele je Objektart anlegen und als neue Datei speichern |
| `AVTSONDELESEN` | Beispiele der Sonde zurücklesen (nur lesen) |

---

## 9. Häufige Fragen

**Ändert AVT meine Zeichnung?** Prüfen und der Hinweis beim Öffnen lesen nur. „Bereinigen …“ in der Palette ändert die
offene Zeichnung erst nach Vorschau, Bestätigung und Sicherungskopie – und speichert nie. Die Werkzeuge ohne
Oberfläche arbeiten nur an Kopien unter `C:\Temp`.

**Warum zwei Durchgänge beim Bereinigen?** Siehe Kapitel 5: Zusatzprogramme können beim Speichern ihre Einträge neu
anlegen. Die Nachprüfung zeigt das; der zweite Durchgang entfernt sie endgültig.

**Warum ist die Prüfsumme gleich, obwohl die Dateien verschieden groß sind?** Die Prüfsumme zählt den Inhalt, nicht
die Datei. Reihenfolge, Handles und Zeitstempel spielen keine Rolle.

**Kann ich einen anderen Namen als „EHS“ verwenden?** Ja. Das Kürzel steht an einer Stelle (heute im Werkzeug,
ab dem Erzeugen in `Stammdaten\Standard.csv`). Umschwenken bedeutet eine neue Hauptnummer mit Umbenennungsliste.

**Der Hinweis kommt bei jeder Zeichnung – kann ich ihn abschalten?** Ja, im Zahnrad: „Beim Öffnen einer Zeichnung leise
auf Fremdinhalte hinweisen“. Benutzte Fremdinhalte, die bleiben sollen, merkt „Bereinigen …“ als Ausnahme (Haken bei b);
dann meldet AVT sie in dieser Zeichnung nicht mehr.

**Warum muss ich vor dem Bereinigen speichern?** Die Sicherungskopie entsteht aus der gespeicherten Datei. Ungespeicherte
Änderungen wären nicht darin – darum fragt AVT vorher danach.

---

## 10. Grenzen

- Bereinigen entfernt Zusatzprogramm-Einträge nur aus der Zeichnung. Ist das Zusatzprogramm auf dem Rechner
  angemeldet und kommen seine Objekte zurück (z. B. durch Einfügen aus alten Zeichnungen), legt es seine Einträge
  wieder an. Endgültig hilft nur, die Zeichnung ohne das Zusatzprogramm zu bearbeiten.
- Ist eine Zeichnung in einer Sitzung offen, in der ein Zusatzprogramm geladen ist, kann es beim Speichern seine Einträge
  wieder anlegen (Befund an RI 9.1: ein Wörterbuch und eine RegApp). Der Hinweis zeigt sie beim nächsten Öffnen; dann die
  Zeichnung im MEP-Profil ohne Zusatzprogramm neu öffnen und erneut bereinigen.
- Fremd heißt: nicht in der Fassung. Ohne Fassungsstempel vergleicht AVT mit der Live-Fassung (heute EHS 0.2). Was dort
  fehlt, aber zu Autodesk gehört (z. B. Satzdefinitionen der MEP-Leervorlage), erscheint ebenfalls als fremd.
- Funde der Gruppe b lassen sich in 0.3.0 nur behalten (als Ausnahme merken). „Ersetzen durch EHS-Symbol“ und „an ASG
  zur Aufnahme geben“ kommen, sobald das Symbolregister von ASG steht.
- Änderungen durch „Bereinigen …“ lassen sich nicht mit Zurück (Z) rückgängig machen – dafür gibt es die Sicherungskopie
  und das Schließen ohne Speichern.
- Die Inhaltsprüfsumme übergeht den Inhalt räumlicher Indizes und der DACH-Sanierungsstile (siehe Kapitel 6).
- AUDIT meldet bei der Autodesk-Leervorlage „Aecb Model (D A CH Ctb) mm“ schon im Auslieferungszustand 2 Fehler
  (Listen-Definition „Standard“); diese stammen nicht von AVT.
- Namenlose und anonyme Einträge zählen im Inhaltsauszug nach ihrer Reihenfolge in der Zeichnung. Gleich gebaute
  Zeichnungen vergleicht AVT sicher; wurde gleicher Inhalt in anderer Reihenfolge gebaut oder fällt ein Eintrag weg,
  erscheinen die folgenden als „geändert“. Ab dem Erzeugen (0.4.0) ordnet AVT sie nach Inhalt.

---

## 11. Versionsstand

| Version | Datum | Inhalt |
|---|---|---|
| 0.3.0 | 23.10.2026 | Plugin mit Palette und Werkzeugkasten-Knopf (Anthrazit); Prüfen in der Palette; Bereinigen der offenen Zeichnung mit Vorschau a/b/c, Sicherungskopie, Nachprüfung; leiser Hinweis beim Öffnen; gemeinsame Logik in AVT.Kern; Unit-Tests |
| 0.2.0 | 23.10.2026 | Inhaltsauszug mit Inhaltsprüfsumme (`AVTKANON`), Prüfen gegen eine Fassung, API-Sonde, DWGPROPS lesen, Kürzel des Standards an einer Stelle, Berichtszeilen mit Präfix „AVT“ |
| 0.1.0 | 23.10.2026 | Bereinigen aus ASG übernommen (gleiches Ergebnis wie EHS 0.1/0.2 in 3 Läufen), eigene Konsolen-Anbindung, Nachprüfung mit automatischem zweitem Durchgang |
