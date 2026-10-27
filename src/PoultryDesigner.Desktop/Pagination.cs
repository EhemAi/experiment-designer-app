namespace PoultryDesigner.Desktop;

internal sealed class Pagination : FlowLayoutPanel
{
    private readonly Button previous, next;
    private readonly StyledButton[] pageButtons;
    private readonly Label count = Ui.Label(""), rowsLabel = Ui.Label("Rows / page");
    private readonly Label[] gaps = [Ui.Label("…"), Ui.Label("…")];
    private readonly ComboBox selector = Ui.Combo(["100", "250", "500"]);
    private Action<int>? choosePage, resizePage;
    private int currentPage;
    private bool updating;
    internal Pagination()
    {
        Dock = DockStyle.Top; AutoSize = true; WrapContents = true; Padding = new(4);
        previous = Ui.Button("Previous", () => choosePage?.Invoke(currentPage - 1));
        next = Ui.Button("Next", () => choosePage?.Invoke(currentPage + 1));
        pageButtons = Enumerable.Range(0, 7).Select(_ =>
        {
            var button = (StyledButton)Ui.Button("", () => { }); button.MinimumSize = new(36, 32);
            button.Click += (_, _) => { if (!updating && button.Tag is int index) choosePage?.Invoke(index); };
            return button;
        }).ToArray();
        selector.Width = 75; selector.AccessibleName = "Rows per page";
        selector.SelectedIndexChanged += (_, _) => { if (!updating && int.TryParse(selector.Text, out int size)) resizePage?.Invoke(size); };
        Controls.Add(previous); Controls.AddRange(pageButtons); Controls.AddRange(gaps); Controls.Add(next);
        Controls.Add(count); Controls.Add(rowsLabel); Controls.Add(selector);
        foreach (Control control in Controls) control.Visible = false;
    }
    internal void UpdatePages(int total, int page, int size, Action<int> choose, Action<int> resize)
    {
        if (total < 0 || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        int pages = Math.Max(1, (total + size - 1) / size);
        currentPage = Math.Clamp(page, 0, pages - 1); choosePage = choose; resizePage = resize;
        updating = true; SuspendLayout();
        try
        {
            // Keep the native controls, including the button whose Click is being handled.
            // Disposing that button or its selector during navigation can reenter focus
            // and validation while the animal table is rebinding.
            foreach (Control control in Controls) control.Visible = false;
            foreach (var button in pageButtons) { button.Text = ""; button.Tag = null; }
            var order = new List<Control>();
            if (pages > 1)
            {
                previous.Enabled = currentPage > 0; order.Add(previous);
                int first = Math.Max(0, currentPage - 2), last = Math.Min(pages - 1, currentPage + 2);
                var indices = Enumerable.Range(first, last - first + 1).Prepend(0).Append(pages - 1).Distinct().Order().ToArray();
                int gap = 0;
                for (int i = 0; i < indices.Length; i++)
                {
                    if (i > 0 && indices[i] - indices[i - 1] > 1) order.Add(gaps[gap++]);
                    var button = pageButtons[i]; button.Tag = indices[i]; button.Text = (indices[i] + 1).ToString();
                    button.AccessibleName = "Page " + button.Text; button.Primary = indices[i] == currentPage; button.Invalidate(); order.Add(button);
                }
                next.Enabled = currentPage < pages - 1; order.Add(next);
            }
            count.Text = total == 0 ? "No matching records" : $"{currentPage * size + 1:N0}–{Math.Min(total, (currentPage + 1) * size):N0} of {total:N0}";
            order.Add(count);
            if (total > 100) { selector.SelectedItem = size.ToString(); order.Add(rowsLabel); order.Add(selector); }
            for (int index = 0; index < order.Count; index++) { Controls.SetChildIndex(order[index], index); order[index].Visible = true; }
        }
        finally { ResumeLayout(); updating = false; }
    }
}
