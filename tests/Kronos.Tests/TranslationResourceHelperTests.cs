using System;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the rule that decides whether a resource candidate's value is usable as a translation.
/// </summary>
/// <remarks>
/// The code this replaced branched on <c>ResourceCandidate.Qualifiers.Count == 0</c> and then left the
/// branch body empty apart from the comment "this should never happen". So the case it anticipated
/// was detected and then silently produced a blank row, even though the neutral value was available
/// to display.
///
/// Neither ResourceMap nor ResourceCandidate can be constructed in a test, so the decision was pulled
/// out into TranslationResourceHelper as a method over plain strings.
/// </remarks>
public class TranslationResourceHelperTests
{
    [Fact]
    public void ACandidateWithNoQualifierFallsBackToTheNeutralValue()
    {
        // The regression. A candidate with an empty qualifier sequence used to leave the row blank.
        // The neutral resource is the correct value to show, so it has to be usable.
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue("de-DE", null));
    }

    [Fact]
    public void ACandidateQualifiedForAnotherLanguageIsUsable()
    {
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue("de-DE", "fr-FR"));
    }

    [Fact]
    public void ACandidateQualifiedForEnglishIsNotUsableAsATranslation()
    {
        // en-US holds the source text, not a translation of it, so offering it as the translation
        // would show the English string back to whoever is translating.
        Assert.False(TranslationResourceHelper.ShouldUseCandidateValue("de-DE", "EN-US"));
    }

    [Theory]
    [InlineData("en-us")]
    [InlineData("EN-US")]
    [InlineData("En-Us")]
    public void TheEnglishQualifierIsMatchedRegardlessOfCase(string qualifier)
    {
        Assert.False(TranslationResourceHelper.ShouldUseCandidateValue("de-DE", qualifier));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-us")]
    public void TranslatingEnglishItselfAlwaysUsesTheCandidate(string selected)
    {
        // Special case that existed before: en-US is the source language, so its candidates are the
        // English text rather than a translation, and the row is populated from them.
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue(selected, "en-US"));
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue(selected, "de-DE"));
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue(selected, null));
    }

    [Fact]
    public void AnEmptyQualifierStringIsTreatedAsAbsentRatherThanAsEnglish()
    {
        // An empty string is not the same as an absent one. Defaulting it to null here keeps
        // "no information" meaning "use the neutral value" instead of accidentally matching.
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue("de-DE", string.Empty));
    }

    [Fact]
    public void ANullSelectedLanguageDoesNotThrow()
    {
        // The selected language comes from a dictionary key lookup, and this is reachable from an
        // async void event handler where an exception would take the window down.
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue(null, "fr-FR"));
        Assert.True(TranslationResourceHelper.ShouldUseCandidateValue(null, null));
    }
}
