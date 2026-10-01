using System;

namespace Kronos.Helpers;

/// <summary>
/// Decides which value a resource candidate should contribute to a translation row.
/// </summary>
/// <remarks>
/// Extracted from TranslationToolboxWindowModel so the rule could be tested, and because the code it
/// replaces had two problems. It called <c>ResourceCandidate.Qualifiers.First()</c>, which throws
/// <see cref="InvalidOperationException"/> when the sequence is empty, and the sequence is only
/// documented to normally hold a single language qualifier, not guaranteed to hold one. The author
/// of the original had left a comment admitting as much: "This should always just be 1 item, not
/// more than 1, maybe?"
///
/// Neither the model nor WinUI can be constructed in a test, so the decision is a plain static
/// method over the qualifier's string value instead.
/// </remarks>
public static class TranslationResourceHelper
{
    /// <summary>
    /// Whether the candidate's value should be used for a translation targeting
    /// <paramref name="selectedLanguage"/>.
    /// </summary>
    /// <param name="selectedLanguage">
    /// The BCP-47 language the user is translating into, for example "de-DE". The en-US case is
    /// special because that language is the source text rather than a translation.
    /// </param>
    /// <param name="qualifierValue">
    /// The qualifier attached to the candidate, or null when there is none.
    /// </param>
    /// <remarks>
    /// A candidate carrying an en-US qualifier holds the English source text, so it is not a
    /// translation and must not be offered as one.
    ///
    /// A candidate with no qualifier holds the neutral resource, which is the correct value to show,
    /// so a missing qualifier is treated as usable rather than as an error. That is the case the
    /// previous First() call would have crashed on.
    /// </remarks>
    public static bool ShouldUseCandidateValue(string? selectedLanguage, string? qualifierValue)
    {
        // en-US is the source language, so its candidates are the English text rather than a
        // translation of it.
        if (string.Equals(selectedLanguage, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // No qualifier at all means the neutral resource, which is what we want to display. Note
        // that this is deliberately not "return false": treating an absent qualifier as "nothing to
        // show" would blank the row instead of filling it with the neutral value.
        if (qualifierValue is null)
        {
            return true;
        }

        return string.Equals(qualifierValue, "EN-US", StringComparison.InvariantCultureIgnoreCase) == false;
    }
}
