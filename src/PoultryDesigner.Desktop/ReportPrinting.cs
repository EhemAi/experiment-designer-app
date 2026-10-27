using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Desktop;

internal sealed record PrintedRow(string[][] Cells, int FirstLine, int LineCount, bool Header, bool Continuation);
internal sealed record PrintedPage(ReportTable Table, List<PrintedRow> Rows);

// Shared pagination for native print preview, printers and offscreen verification.
internal sealed class ReportPrinting : IDisposable
{
    private readonly AllocationReport report;
    private readonly Font body = Theme.Body(9), bold = Theme.Body(9, FontStyle.Bold), title = Theme.Heading(17), heading = Theme.Heading(12);
    internal readonly List<PrintedPage> Pages = [];
    private const float Left = 36, Width = 720, Top = 172, Bottom = 562, Line = 13;
    internal ReportPrinting(AllocationReport source)
    {
        report = AllocationReports.Printable(source);
        using var bitmap = new Bitmap(1584, 1224); bitmap.SetResolution(144, 144); using var g = Graphics.FromImage(bitmap); g.PageUnit = GraphicsUnit.Point;
        foreach (var table in report.Tables)
        {
            float[] widths = Widths(table); string[][] headers = table.Columns.Select((c, i) => Wrap(g, c.Name, bold, widths[i] - 12)).ToArray();
            int headerLines = headers.Max(x => x.Length); float y = Top;
            PrintedPage page = NewPage();
            PrintedPage NewPage() { var next = new PrintedPage(table, []); Pages.Add(next); next.Rows.Add(new(headers, 0, headerLines, true, false)); y = Top + headerLines * Line + 12; return next; }
            if (table.Rows.IsEmpty) page.Rows.Add(new([new[] { "No records for this revision." }], 0, 1, false, false));
            foreach (var row in table.Rows)
            {
                var cells = row.Select((v, i) => Wrap(g, AllocationReports.PrintedText(v), body, widths[i] - 12)).ToArray();
                int count = cells.Max(x => x.Length), first = 0;
                while (first < count)
                {
                    int capacity = (int)((Bottom - y - 12) / Line); if (capacity <= 0) { page = NewPage(); continue; }
                    // Keep ordinary rows together; exceptionally tall rows continue with repeated column headers.
                    if (first == 0 && count > capacity && count * Line + 12 <= Bottom - Top - headerLines * Line - 12) { page = NewPage(); continue; }
                    int lines = Math.Min(count - first, capacity); page.Rows.Add(new(cells, first, lines, false, first > 0)); first += lines; y += lines * Line + 12;
                    if (first < count) page = NewPage();
                }
            }
        }
    }
    private static float[] Widths(ReportTable t)
    {
        if (t.Name == "Summary") return [190, 530];
        double[] weights = t.Columns.Select(c => c.Name.Contains("location", StringComparison.OrdinalIgnoreCase) ? 2.0
            : c.Name.Contains("reason", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("SHA") ? 2.4 : c.Name.Contains("ID") || c.Name == "Label" || c.Name.Contains("label") ? 1.3 : 1).ToArray();
        return weights.Select(x => (float)(Width * x / weights.Sum())).ToArray();
    }
    private static string[] Wrap(Graphics g, string text, Font font, float width)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(""); continue; }
            int start = 0;
            while (start < paragraph.Length)
            {
                int lo = 1, hi = paragraph.Length - start;
                while (lo < hi) { int n = (lo + hi + 1) / 2; if (g.MeasureString(paragraph.Substring(start, n), font, int.MaxValue, StringFormat.GenericTypographic).Width <= width) lo = n; else hi = n - 1; }
                int length = lo;
                if (start + length < paragraph.Length) { int space = paragraph.LastIndexOf(' ', start + length - 1, length); if (space > start) length = space - start; }
                // Avoid splitting a UTF-16 surrogate pair across rows/pages.
                if (length > 1 && char.IsHighSurrogate(paragraph[start + length - 1])) length--;
                lines.Add(paragraph.Substring(start, length)); start += length; if (start < paragraph.Length && paragraph[start] == ' ') start++;
            }
        }
        return lines.ToArray();
    }
    internal Bitmap Render(int index, int dpi = 144)
    {
        var bitmap = new Bitmap(11 * dpi, (int)(8.5 * dpi)); bitmap.SetResolution(dpi, dpi); using var g = Graphics.FromImage(bitmap); Draw(g, index); return bitmap;
    }
    private void Draw(Graphics g, int index)
    {
        g.PageUnit = GraphicsUnit.Point; g.Clear(Color.White); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.SmoothingMode = SmoothingMode.AntiAlias;
        var page = Pages[index]; float[] widths = Widths(page.Table);
        using var ink = new SolidBrush(Color.FromArgb(23, 25, 27)); using var muted = new SolidBrush(Color.FromArgb(75, 83, 90));
        using var band = new SolidBrush(Color.FromArgb(32, 35, 38)); using var teal = new SolidBrush(Color.FromArgb(66, 158, 132)); using var white = new SolidBrush(Color.White);
        using var pale = new SolidBrush(Color.FromArgb(244, 246, 246)); using var rule = new Pen(Color.FromArgb(218, 223, 225), 0.5f);
        g.FillRectangle(teal, Left, 24, 3, 25);
        g.DrawString("Allocation report", title, ink, new RectangleF(Left + 12, 25, Width - 12, 30));
        float contextY = 59; foreach (string line in Wrap(g, report.Context, body, Width)) { g.DrawString(line, body, muted, Left, contextY, StringFormat.GenericTypographic); contextY += Line; }
        g.DrawString("Exported UTC " + report.GeneratedUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"), body, muted, Left, contextY, StringFormat.GenericTypographic);
        g.DrawString(page.Table.Name, heading, ink, Left, 98, StringFormat.GenericTypographic);
        float descriptionY = 123; foreach (string line in Wrap(g, page.Table.Description, body, Width)) { g.DrawString(line, body, muted, Left, descriptionY, StringFormat.GenericTypographic); descriptionY += Line; }
        float y = Top; int stripe = 0;
        foreach (var row in page.Rows)
        {
            float height = row.LineCount * Line + 12;
            if (row.Header) g.FillRectangle(band, Left, y, Width, height); else if (++stripe % 2 == 0) g.FillRectangle(pale, Left, y, Width, height);
            float x = Left;
            for (int c = 0; c < row.Cells.Length; c++)
            {
                for (int l = 0; l < row.LineCount; l++)
                {
                    int lineIndex = row.FirstLine + l; if (lineIndex < row.Cells[c].Length)
                        g.DrawString(row.Cells[c][lineIndex], row.Header ? bold : body, row.Header ? white : ink, x + 6, y + 6 + l * Line, StringFormat.GenericTypographic);
                }
                x += widths[c];
            }
            y += height; g.DrawLine(rule, Left, y, Left + Width, y);
        }
        g.DrawLine(rule, Left, 578, Left + Width, 578); g.DrawString("EXPERIMENT / ED", body, muted, Left, 585);
        g.DrawString($"Page {index + 1} of {Pages.Count}", body, muted, new RectangleF(Left, 585, Width, 15), new StringFormat { Alignment = StringAlignment.Far });
    }
    internal PrintDocument Document()
    {
        var document = new PrintDocument { DocumentName = report.Title };
        document.DefaultPageSettings.Landscape = true; document.DefaultPageSettings.PaperSize = new PaperSize("Letter", 850, 1100);
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0); int current = 0; bool preview = false;
        document.BeginPrint += (_, e) => { current = 0; preview = e.PrintAction == PrintAction.PrintToPreview; };
        document.PrintPage += (_, e) =>
        {
            DrawPrintPage(e.Graphics!, current, e.PageBounds, new(e.PageSettings.HardMarginX, e.PageSettings.HardMarginY), preview ? 144 : 300);
            current++; e.HasMorePages = current < Pages.Count;
        };
        return document;
    }
    // PageBounds follows the requested orientation; some printer drivers report
    // PrintableArea in portrait coordinates even for landscape. Use one uniform
    // scale and compensate the printer origin, preserving the entire page.
    internal static RectangleF PagePlacement(Rectangle pageBounds, PointF hardMargin)
    {
        float width = pageBounds.Width * .72f, height = pageBounds.Height * .72f;
        float scale = Math.Min(width / 792, height / 612);
        return new((width - 792 * scale) / 2 - hardMargin.X * .72f,
            (height - 612 * scale) / 2 - hardMargin.Y * .72f, 792 * scale, 612 * scale);
    }
    internal void DrawPrintPage(Graphics graphics, int index, Rectangle pageBounds, PointF hardMargin, int dpi)
    {
        var state = graphics.Save();
        try
        {
            graphics.PageUnit = GraphicsUnit.Point;
            var place = PagePlacement(pageBounds, hardMargin);
            graphics.TranslateTransform(place.X, place.Y);
            float scale = place.Width / 792;
            graphics.ScaleTransform(scale, scale);
            // Recording private-font DrawString calls directly into Windows
            // preview metafiles can shift glyphs/rows on replay. Embed the same
            // verified page instead; preview uses 144 dpi, physical/PDF output
            // uses 300 dpi. Only one temporary bitmap is held at a time.
            using var page = Render(index, dpi);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(page, new RectangleF(0, 0, 792, 612), new RectangleF(0, 0, page.Width, page.Height), GraphicsUnit.Pixel);
        }
        finally { graphics.Restore(state); }
    }
    public void Dispose() { body.Dispose(); bold.Dispose(); title.Dispose(); heading.Dispose(); }
}
