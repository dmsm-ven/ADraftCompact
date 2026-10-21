using System.Windows;
using ADraftCompact.Services;

namespace ADraftCompact.Views;

/// <summary>Minimal progress window: new version, progress bar, downloaded size.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateService _service;
    private readonly UpdateInfo _info;

    /// <summary>True when the new exe is ready and the app must exit now so the swap script can replace it.</summary>
    public bool ReadyToRestart { get; private set; }

    public UpdateWindow(UpdateService service, UpdateInfo info)
    {
        InitializeComponent();
        _service = service;
        _info = info;
        VersionText.Text = $"Downloading v{AppVersion.Format(info.Version)} (current v{AppVersion.Text})";
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                Bar.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                SizeText.Text = $"{p.Done / 1048576.0:F1} / {p.Total / 1048576.0:F1} MB";
            });
            await _service.DownloadAndPrepareAsync(_info, progress);
            ReadyToRestart = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The update failed and was skipped:\n{ex.Message}", "Ability Draft Overlay",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }
}
