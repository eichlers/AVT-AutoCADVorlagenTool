# AVT – AutoCAD-Vorlagen-Tool (Pflege des Eichler-Hausstandards)

AVT pflegt den **Eichler-Hausstandard (EHS)** – die gemeinsame, saubere Zeichnungsvorlage für ATT, ADM, ANA und ASG.
Gestartet am 23.10.2026 auf Wunsch des Nutzers („dann bauen wir das als Modul auf“). © S. Eichler · Eichler-GmbH.
Sprache mit dem Nutzer: **Deutsch**. Koordination über **AMK** (Aufträge kommen von der AMK-Sitzung).

@../AMK AutoCADModulKoordination/AMK_Stand.md

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
- Startpunkt Code: das EHS-Bereinigungsskript von ASG (`ASG AutoCADSymbolGenerator\tools\ehs\`, Commit 095faa6 + Folgecommit) –
  Übernahme nach AVT mit ASG abstimmen.

## Feste Regeln

- Nie eine Zeichnung des Nutzers ändern oder speichern; nur Kopien in `C:\Temp\…`. EHS-Fassungen sind schreibgeschützt.
- Kein pauschales PURGE in Vorlagen (unbenutzte Symbolstile gehören dazu).
- Anzeigethemen zeigen per Zeiger auf Satzdefinitionen – vor dem Entfernen einer Definition den Verweisgraph prüfen.
- Headless (accoreconsole) nur außerhalb eines MEP-Slots; MEP-Tests nur mit Slot von AMK; MEP 2025 nie anfassen.
- Nach jedem Schritt committen und pushen (Footer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- Werkzeugkasten-Knopf, Palette, Tooltip, Zahnrad nach den gemeinsamen Regeln in `AMK_Stand.md`, sobald es ein Plugin gibt.
