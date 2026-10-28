// Counts the model elements in the open project, grouped by category, biggest first.
// Read-only: it changes nothing, so it needs no Transaction.
var sb = new StringBuilder();
var counts = new FilteredElementCollector(Doc)
    .WhereElementIsNotElementType()
    .Where(e => e.Category != null)
    .GroupBy(e => e.Category.Name)
    .OrderByDescending(g => g.Count());
foreach (var g in counts.Take(25))
    sb.AppendLine(g.Count().ToString().PadLeft(6) + "  " + g.Key);
sb.ToString()
