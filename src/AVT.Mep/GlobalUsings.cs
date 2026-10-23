// WPF-Projekte bekommen System.IO nicht als impliziten Namensraum (Konflikt mit System.Windows.Shapes.Path) – hier ergänzt.
global using System.IO;
global using Path = System.IO.Path;
