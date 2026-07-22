using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CoreCage.App.ViewModels;
using CoreCage.Core.GameTune;
using CoreCage.Core.Profiles;

namespace CoreCage.App.Views;

/// <summary>
/// Game Presets: one card per detected game with a shipped max-FPS graphics preset. Same VM-first
/// pattern as every other section (ShellViewModel puts a ViewModel into ContentControl.Content,
/// MainWindow's DataTemplate resolves this View, and DataContext is inherited from the
/// ContentPresenter — never set here). The one difference from the other pages: the card VM
/// (<see cref="GamePresetCardViewModel"/>) has no ICommand, so Apply/Restore are wired as Click
/// handlers here instead of Command bindings. The card VM does implement INotifyPropertyChanged, so
/// its post-action state (State/StatusText/BackupPath/CanApply/CanRestore) updates the bound
/// controls in place — no container rebuild, no lost keyboard focus.
/// </summary>
public partial class GamePresetsPage : UserControl
{
    public GamePresetsPage()
    {
        InitializeComponent();
        // Best-effort default focus for keyboard users: land on the cards list itself rather than
        // nothing at all. Focusing a specific DataTemplate-generated Apply button reliably requires
        // container-generation-complete plumbing that isn't worth the complexity here.
        Loaded += (_, _) => CardsList.Focus();
    }

    /// <summary>
    /// Builds the real, production <see cref="GamePresetsViewModel"/>: a <see cref="GameTuneService"/>
    /// backed by a %LOCALAPPDATA%\CoreCage\backups <see cref="ConfigBackup"/> and a live
    /// Process.GetProcessesByName "is the game running" check, plus one <see cref="DetectedGame"/>
    /// per shipped community profile that carries a graphics block (profiles without one — e.g.
    /// guided-only/Unity titles with no graphics context at all — are simply not offered a card).
    /// Called once by <see cref="ShellViewModel"/> so this page never has to know how its data got
    /// built, matching how every other section's real service gets constructed away from the View.
    /// </summary>
    public static GamePresetsViewModel BuildViewModel()
    {
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreCage", "backups");
        var service = new GameTuneService(new ConfigBackup(backupRoot), IsGameRunning);

        var profilesDir = Path.Combine(AppContext.BaseDirectory, "profiles");
        var loaded = CommunityProfileLoader.LoadDirectory(profilesDir);
        var games = loaded.Profiles
            .Where(e => e.Profile.Graphics is not null)
            .Select(e => new DetectedGame(
                GameIdFor(e.Profile.ExeName),
                e.Profile.ExeName,
                e.Profile.DisplayName,
                e.Profile.Graphics))
            .ToList();

        return new GamePresetsViewModel(service, games);
    }

    /// <summary>Stable per-game backup-folder id, derived from the exe name (community profiles have
    /// no separate "id" field per profiles/SCHEMA.md) — lowercased, extension stripped.
    /// <see cref="ConfigBackup"/> sanitizes it further for the filesystem.</summary>
    private static string GameIdFor(string exeName) => Path.GetFileNameWithoutExtension(exeName).ToLowerInvariant();

    /// <summary>Process.GetProcessesByName wants the exe name without its extension.</summary>
    private static bool IsGameRunning(string exeName) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)).Length > 0;

    private void Apply_Click(object sender, RoutedEventArgs e) => Run(sender, card => card.Apply());
    private void Restore_Click(object sender, RoutedEventArgs e) => Run(sender, card => card.Restore());

    /// <summary>
    /// Runs the action on the clicked card. <see cref="GamePresetCardViewModel"/> raises
    /// INotifyPropertyChanged for State/StatusText/BackupPath/CanApply/CanRestore, so the bound
    /// controls on the same container update in place — the clicked button keeps keyboard focus.
    /// </summary>
    private static void Run(object sender, Action<GamePresetCardViewModel> action)
    {
        if (sender is not FrameworkElement { DataContext: GamePresetCardViewModel card }) return;
        action(card);
    }
}
