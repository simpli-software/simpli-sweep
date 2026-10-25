using System.Globalization;
using Cleaner.Core;

namespace Cleaner.App.ViewModels;

public sealed class RuleItemViewModel : ObservableObject
{
    private bool _isSelected;

    /// <param name="preferred">The user's saved choice for this category, or null for the rule's default.</param>
    public RuleItemViewModel(RuleScanResult result, bool? preferred = null)
    {
        Result = result;
        _isSelected = CanSelect && (preferred ?? result.Rule.EnabledByDefault);
    }

    public RuleScanResult Result { get; }
    public CleanRule Rule => Result.Rule;
    public string Name => Rule.Name;
    public string Description => Rule.Description;
    public string Category => Rule.Category;
    public long Bytes => Result.Status == RuleStatus.Ok ? Result.Bytes : 0;

    /// <summary>Only categories with something to clean can be ticked.</summary>
    public bool CanSelect => Result.Status == RuleStatus.Ok;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value && CanSelect))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    public string SizeText => Result.Status == RuleStatus.Ok ? ByteSize.Format(Result.Bytes) : "";

    public string DetailText => Result.Status switch
    {
        RuleStatus.Ok when Rule.Kind == RuleKind.RecycleBin => "Emptied permanently",
        RuleStatus.Ok => string.Format(CultureInfo.CurrentCulture, "{0:N0} files", Result.FileCount),
        RuleStatus.NeedsAdmin => "Needs administrator",
        RuleStatus.NothingFound => "Nothing to clean",
        _ => "Rule error",
    };
}
