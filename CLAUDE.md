# AVT – AutoCAD-Vorlagen-Tool (Pflege des Eichler-Hausstandards)

AVT pflegt den **Eichler-Hausstandard (EHS)** – die gemeinsame, saubere Zeichnungsvorlage für ATT, ADM, ANA und ASG.
Gestartet am 23.10.2026 auf Wunsch des Nutzers („dann bauen wir das als Modul auf“). © S. Eichler · Eichler-GmbH.
GitHub: `eichlers/AVT-AutoCADVorlagenTool` (privat), Branch `main`. Sprache mit dem Nutzer: **Deutsch**. Koordination über **AMK** (Aufträge kommen von der AMK-Sitzung).

@../AMK-AutoCADModulKoordination/AMK_Stand.md

## Aufgabe

1. **Bereinigen** – ein Knopf/Befehl, der Altlasten aus einer Zeichnung entfernt: alte Eigenschaftssätze (`TAGBauteil` & Co.,
   fremde Sätze), Fremd-Add-on-Einträge (z. B. liNear `LIN_*`), Dubletten wie „ANA_Hydraulik (2)“, alte Stempel-/Legendenblöcke.
   Brauchbare Werte werden vorher nach `EHS_Bauteil` übernommen (Feldkatalog). Nutzer: „so was schleppt sich sonst ein wie ein Virus“.
2. **Prüfen** – Zeichnung gegen den Hausstandard prüfen (Stammdaten, Regeln), Bericht je Abweichung.
3. **Vorlage erzeugen** – EHS-Fassungen aus den Stammdaten bzw. aus der Vorfassung reproduzierbar per Skript bauen.
4. **Umstellen** – alte Zeichnungen auf den Hausstandard umstellen (Stile, Sätze), mit Vorschau.
5. **Release** – freigegebene Fassung für die Verteilung vorbereiten (GitHub-Release des Schema-Repos).

Nicht AVT: Symbolstile erzeugen/ändern (ASG), Stempel (ATT), Tabellen/Listen/Datenbank-Abgleich (ADM), Rechnen (ANA).

## Ablage

- Hausstandard (Daten, Fassungen, Regeln, Handbuch): `C:\Users\eichlers15\Meine Ablage\Vorlagen\CAD\CADStandardEichlerGmbH\Schema`
  (GitHub `eichlers/CADStandardEichlerGmbH-Schema`) – **gepflegt von AMK**; AVT liefert Ergebnisse an AMK, schreibt dort nicht selbst.
- Startpunkt Code: das EHS-Bereinigungsskript von ASG (`ASG-AutoCADSymbolGenerator\tools\ehs\`, Commit 095faa6 + Folgecommit) –
  Übernahme nach AVT mit ASG abstimmen.

## Stand

- **0.1.0 Bereinigen** (23.10.2026, aus ASG tools/ehs 095faa6 + e9f502d): `src/AVT.Bereinigen` (AVTINVENTUR, AVTBEREINIGEN),
  `tools/AVT_bereinigen.ps1` (Ablauf + Nachprüfung Object-Enabler, bei Befund automatisch zweiter Durchgang),
  `tools/headless/run.ps1` (/isolate, ohne ASG-Plugin), `tools/avt_bericht.py` (Bericht + Vergleich), `tools/AVT_regression.ps1`.
  Regression 3 × gleich zu ASG EHS 0.1 und 0.2 (`C:\Temp\AVT\Regression\20261023_1309_*`). Fremd-Vorsilben nur beim Aufruf.
- **0.2.0 Prüfen + Inhaltsauszug + API-Sonde** (23.10.2026): Projekt heißt `src/AVT.Werkzeug` (AVTKANON, AVTSONDE,
  AVTSONDELESEN), `tools/AVT_kanon.ps1`, `AVT_pruefen.ps1` + `avt_pruefen.py`, `AVT_sonde.ps1`, `avt_gemeinsam.ps1`.
  Kürzel „EHS“ nur in `Standard.cs`; Berichtszeilen „AVT…“. Inventur immer VOR AUDIT (AUDIT ändert Schraffuren).
  Nachweis: 7 × EHS 0.2 → Prüfsumme f13fa95c. Handbuch `docs/AVT_Handbuch.md` + PDF (Modulfarbe Anthrazit #4A5662).
- **0.3.0 Plugin** (23.10.2026): `src/AVT.Kern` (gemeinsame Logik, Fremd/ a-b-c), `src/AVT.Mep` (Palette, Knopf #4A5662,
  Hinweis, Zahnrad), `src/AVT.Werkzeug` (headless), `tests/AVT.Kern.Tests` (42), `bundle/`, `tools/AVT_bundle.ps1`.
  Bereinigen der offenen Zeichnung: Vorschau → Bestätigung → Sicherungskopie → Runden mit Purge → Nachprüfung; speichert nie.
  Einspielen nur nach AMK-„frei“-Runde. Umstellen alter Zeichnungen entfällt (Nutzer 23.10.: neues System).
- **Vorgemerkt:** Prüfen gegen Unterklassen.csv/Bauarten.csv (DIN EN IEC 81346-2:2020-10); Kunden-Bezug (Roche) im Kern = Fehler,
  Begriffe aus den Stammdaten. Textstile nur Arial (TrueType) – Nutzer 23.10.: „muss immer Arial sein, damit man im PDF
  suchen kann“; Stammdaten\Textstile.csv (Rollen EHS_Text_18/25/35/50), Prüfen: andere Schrift (SHX, Kundenschrift) = Fehler,
  auch Legendenschrift der Anzeigethemen; Textstile der Leervorlage melden.
- **Versionsplan** (AMK ok): 0.3.0 Plugin/Palette/Knopf + Bereinigen der offenen Zeichnung (Vorschau, Bestätigung,
  Sicherungskopie, Einteilung a/b/c), 0.4.0 Erzeugen, 0.6.0 Release (DWGPROPS schreiben).
- **Bauplan „grüne Wiese“** (Nutzer 23.10., `Schema\Bauplan_EHS.md`): AVT erzeugt die Vorlage aus Stammdaten – erst nach Auftrag AMK bauen.

## Feste Regeln

- **Zeichnungen des Nutzers** (Nutzer 23.10.): AVT ändert eine Zeichnung des Nutzers nur nach Vorschau und ausdrücklicher
  Bestätigung (Bereinigen in der Palette), legt vorher eine Sicherungskopie an und speichert nie selbst. Alles andere
  (Bauläufe, Tests, Sonden) nur an Kopien in `C:\Temp\…`. EHS-Fassungen sind schreibgeschützt.
- **Bereinigen-Vorgabe** (Nutzer 23.10.): vorab angehakt nur Einträge mit Fremdspur; Unbenutztes ohne Fremdspur nur anzeigen.
- Kein pauschales PURGE in Vorlagen (unbenutzte Symbolstile gehören dazu).
- Anzeigethemen zeigen per Zeiger auf Satzdefinitionen – vor dem Entfernen einer Definition den Verweisgraph prüfen.
- Headless (accoreconsole) nur außerhalb eines MEP-Slots; MEP-Tests nur mit Slot von AMK; MEP 2025 nie anfassen.
- Nach jedem Schritt committen und pushen (Footer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- Werkzeugkasten-Knopf, Palette, Tooltip, Zahnrad nach den gemeinsamen Regeln in `AMK_Stand.md`, sobald es ein Plugin gibt.
