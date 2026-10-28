// Lists every Level in the open project with its elevation in feet (Revit's internal unit).
// Read-only: it changes nothing, so it needs no Transaction.
var sb = new StringBuilder();
foreach (var level in new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
    sb.AppendLine(level.Name + "  " + level.Elevation.ToString("F3") + " ft");
sb.ToString()
