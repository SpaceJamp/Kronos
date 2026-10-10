using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;

namespace Kronos.UserControls;

class ImportDLLSummaryControlModel
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>
    /// Files that were imported without their signature being checked.
    /// </summary>
    /// <remarks>
    /// Bound to the warning banner in the summary XAML. Non-zero only on platforms with no
    /// Authenticode implementation, where an imported dll has no expected hash to compare against
    /// and the signature check was the entire gate - so "imported" does not mean "verified" there.
    /// </remarks>
    public int UnverifiedCount { get; set; }

    /// <summary>The full warning text, shown in the banner.</summary>
    public string UnverifiedWarning { get; set; } = string.Empty;

    /// <summary>Whether to show the banner at all.</summary>
    /// <remarks>
    /// Bound to the banner's Visibility. On Windows this is always false, so the banner never
    /// appears and does not become noise.
    /// </remarks>
    public bool ShowUnverifiedWarning { get; set; }

    public ImportDLLSummaryControlModelTranslationProperties TranslationProperties { get; } = new ImportDLLSummaryControlModelTranslationProperties();

    public ImportDLLSummaryControlModel(IReadOnlyList<DLLImportResult> dllImportResults)
    {
        SuccessCount = dllImportResults.Count(x => x.Success == true);
        FailedCount = dllImportResults.Count(x => x.Success == false);

        var unverified = dllImportResults
            .Where(x => x.Success && x.SignatureNotVerified)
            .ToList();

        UnverifiedCount = unverified.Count;
        ShowUnverifiedWarning = unverified.Count > 0;
        UnverifiedWarning = unverified.FirstOrDefault()?.SignatureNotVerifiedMessage ?? string.Empty;

        foreach (var dllImportResult in dllImportResults)
        {
            Logger.Verbose($"DLLImportResult: {dllImportResult.ToString()}");
        }
    }
}