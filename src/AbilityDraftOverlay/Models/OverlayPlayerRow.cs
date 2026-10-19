using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbilityDraftOverlay.Models;

/// <summary>Icons currently displayed for one of the ten Ability Draft players.</summary>
public partial class OverlayPlayerRow : ObservableObject
{
    public int Player { get; init; }

    [ObservableProperty] private ImageSource? hero;
    [ObservableProperty] private ImageSource? ability1;
    [ObservableProperty] private ImageSource? ability2;
    [ObservableProperty] private ImageSource? ability3;
    [ObservableProperty] private ImageSource? ultimate;
}
