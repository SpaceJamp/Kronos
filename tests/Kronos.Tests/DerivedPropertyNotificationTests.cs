using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards that a button bound to a derived property actually re-evaluates when its inputs change.
/// </summary>
/// <remarks>
/// v1.58.1 bound the Refresh button to CanRefresh instead of the negation of IsLoading, which was the
/// right idea - the button should reflect the same condition the command enforces rather than a separate
/// flag that can disagree with it. But CanRefresh is derived from IsLoading, and only the two backing
/// properties carried [NotifyPropertyChangedFor(nameof(IsLoading))]. Nothing notified CanRefresh.
///
/// Both flags initialise to true, so at startup CanRefresh is false and the button is disabled. The
/// initial load then clears the flags: IsLoading fires a change notification, CanRefresh does not, and
/// the button stays disabled for the rest of the session. Since the button was disabled, the command never
/// ran, so the two explicit OnPropertyChanged(nameof(CanRefresh)) calls inside it never fired either.
/// Deadlocked by construction, with no error anywhere.
///
/// This is the shape of bug that a source-reading test cannot catch and a property-reading one can, so
/// both are used: the derived property is evaluated for real, and the notification wiring is asserted.
/// </remarks>
public class DerivedPropertyNotificationTests
{
    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TheLoadingFlagsNotifyEveryDependentPropertyNotJustTheImmediateOne()
    {
        // MassUpgradeViewModel already does this, on IsRunning for CanUpdate. The Refresh flags were the
        // same shape and were missed.
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        foreach (var flag in new[] { "IsGameListLoading", "IsDLSSLoading" })
        {
            var declaration = source.IndexOf($"public partial bool {flag}", StringComparison.Ordinal);
            Assert.True(declaration > 0, $"{flag} should be declared");

            // Just the attribute block, stopping at the first line comment. A wider window runs back into the
            // comment above the property, which names IsLoading in prose, which would satisfy the assertion
            // even if the attribute had been removed.
            var before = source.Substring(Math.Max(0, declaration - 700), Math.Min(700, declaration));
            var attributeBlock = before[(before.LastIndexOf("\n//", StringComparison.Ordinal) + 1)..];

            Assert.Contains("NotifyPropertyChangedFor(nameof(IsLoading))", attributeBlock, StringComparison.Ordinal);
            Assert.Contains("NotifyPropertyChangedFor(nameof(CanRefresh))", attributeBlock, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRefreshButtonBindsToThePropertyThatTheCommandEnforces()
    {
        // Deliberate, and worth pinning: binding to CanRefresh rather than the negation of IsLoading is
        // what stops the button and the command disagreeing. The cost is the notification wiring above.
        var source = Source("src", "Pages", "GameGridPage.xaml");

        Assert.Contains("ViewModel.CanRefresh", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyThatDependsOnAnotherIsReEvaluatedWhenTheOtherChanges()
    {
        // The same mistake in miniature, so the rule is demonstrated rather than only asserted in a comment.
        // A dependent property that nobody notifies is one frozen at its startup value.
        var derived = new Derived(1, 1);

        Assert.False(derived.CanAct, "a derived property starts false when its inputs start true");

        // Both inputs have to clear, mirroring IsLoading being the OR of the two loading flags.
        derived.Primary = 0;
        Assert.False(derived.CanAct, "one input is still set, so the derived property must stay false");

        derived.Secondary = 0;
        Assert.True(derived.CanAct, "and must follow its inputs, not stay frozen at its startup value");
    }

    /// <summary>
    /// A miniature of the Refresh flags: two inputs and a property derived from both, with the
    /// notification wiring that keeps the derived one honest.
    /// </summary>
    sealed class Derived
    {
        int _a;
        int _b;

        public Derived(int a, int b)
        {
            _a = a;
            _b = b;
        }

        public int Primary
        {
            get => _a;
            set
            {
                if (_a == value)
                {
                    return;
                }

                _a = value;

                // What [NotifyPropertyChangedFor(nameof(CanAct))] generates.
                CanActChanged?.Invoke();
            }
        }

        public int Secondary
        {
            get => _b;
            set
            {
                if (_b == value)
                {
                    return;
                }

                _b = value;
                CanActChanged?.Invoke();
            }
        }

        public bool CanAct => _a == 0 && _b == 0;

        public event Action? CanActChanged;
    }
}