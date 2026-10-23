// Start von AVT mit AutoCAD MEP 2026 (Bundle: LoadOnAutoCADStartup): sobald eine Zeichnung aktiv und AutoCAD ruhig ist,
// den Werkzeugkasten anlegen bzw. aktualisieren und den leisen Hinweis einrichten. Die Palette bleibt beim Start zu
// (gemeinsame Regel) – sie öffnet nur über den Knopf AVT oder AVTPALETTE.

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Avt.Mep.Palette;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Avt.Mep.AvtAnwendung))]

namespace Avt.Mep;

public sealed class AvtAnwendung : IExtensionApplication
{
    public void Initialize() => AcApp.Idle += BeimStart;

    public void Terminate() { }

    static void BeimStart(object? sender, EventArgs e)
    {
        if (AcApp.DocumentManager.MdiActiveDocument is not Document || !AcApp.IsQuiescent) return;
        AcApp.Idle -= BeimStart;
        try
        {
            Protokoll.Merke("Sitzung", $"Start {Programminfo.Zeile}");
            Werkzeugkasten.WerkzeugkastenBefehl.BeimStart();
            Hinweis.Einrichten();
        }
        catch (System.Exception ex) { Protokoll.Merke("Fehler", $"Start: {ex.GetBaseException().Message}"); }
    }
}
