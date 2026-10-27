using System.Data;
using System.Globalization;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private void RenderDraftValidation()
    {
        var errors = new List<string>();
        void Field(Control control, string error)
        {
            fieldErrors.SetError(control, error); control.AccessibleDescription = error;
            if (error.Length > 0) errors.Add(error);
        }
        Field(replicates, replicates.Value < 2 ? "At least two replicates per treatment are required." : "");
        Field(perPen, perPen.Value < 1 ? "At least one animal per experimental unit is required." : "");
        var rows = treatmentTable.Rows.Cast<DataRow>().Where(x => x.RowState != DataRowState.Deleted).ToArray();
        string[] ids = rows.Select(r => Convert.ToString(r["Treatment ID"])!.Trim()).ToArray();
        string[] labels = rows.Select(r => Convert.ToString(r["Label"])!.Trim()).ToArray();
        string treatmentError = rows.Length < 2 ? "Define at least two treatments." : ids.Any(string.IsNullOrWhiteSpace) || labels.Any(string.IsNullOrWhiteSpace)
            ? "Every treatment needs an ID and a label." : ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || labels.Distinct(StringComparer.Ordinal).Count() != labels.Length
            ? "Treatment IDs and labels must each be unique." : "";
        Field(treatments, treatmentError);
        long required = (long)rows.Length * (int)replicates.Value * (int)perPen.Value;
        if (required > PoultryDesigner.Engine.AllocationEngine.MaximumAnimals) errors.Add("The required animal count exceeds the supported maximum.");
        if (stage == 0 && errors.Count > 0) { pageAlert.Text = string.Join(" ", errors); pageAlert.ForeColor = Theme.Warning; }
    }
}
