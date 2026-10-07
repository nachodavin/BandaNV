using System.Diagnostics;
using System.Text.Json;
using BandaNV.App.Services;
using BandaNV.Core.Infrastructure;
using BandaNV.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class SettingsPage : Page
{
    private SettingsSection _currentSection = SettingsSection.General;
    private SettingsConfirmMode _confirmMode = SettingsConfirmMode.None;
    private const string UnselectedFolderText = "No seleccionada";
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isPageReady;

    public SettingsPage()
    {
        InitializeComponent();

        _isPageReady = false;
        LoadPersistentSettingsIntoUi(global::BandaNV.App.App.Settings.Current);
        UpdateHistoryDependentVisibility();
        _isPageReady = true;

        SetSettingsSection(SettingsSection.General);
        UpdateAppearancePreview();
    }

    public void SyncUpdateStartupNoticeToggle(
        bool enabled)
    {
        var wasReady =
            _isPageReady;

        _isPageReady =
            false;

        try
        {
            AutoUpdateToggle.IsOn =
                enabled;
        }
        finally
        {
            _isPageReady =
                wasReady;
        }
    }

    private void SettingsTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<SettingsSection>(tag, out var section))
        {
            return;
        }

        SetSettingsSection(section);
    }

    private void SetSettingsSection(SettingsSection section)
    {
        _currentSection = section;

        GeneralSettingsPanel.Visibility =
            section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsPanel.Visibility =
            section == SettingsSection.Appearance ? Visibility.Visible : Visibility.Collapsed;
        OrganizationSettingsPanel.Visibility =
            section == SettingsSection.Organization ? Visibility.Visible : Visibility.Collapsed;
        HistorySecuritySettingsPanel.Visibility =
            section == SettingsSection.HistorySecurity ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSettingsPanel.Visibility =
            section == SettingsSection.Advanced ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility =
            section == SettingsSection.About ? Visibility.Visible : Visibility.Collapsed;

        UpdateTabVisual(GeneralTabButton, section == SettingsSection.General);
        UpdateTabVisual(AppearanceTabButton, section == SettingsSection.Appearance);
        UpdateTabVisual(OrganizationTabButton, section == SettingsSection.Organization);
        UpdateTabVisual(HistorySecurityTabButton, section == SettingsSection.HistorySecurity);
        UpdateTabVisual(AdvancedTabButton, section == SettingsSection.Advanced);
        UpdateTabVisual(AboutTabButton, section == SettingsSection.About);
    }

    private static void UpdateTabVisual(Button button, bool isSelected)
    {
        button.Background = isSelected
            ? (Brush)Application.Current.Resources["BandaAccentSoftBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        button.BorderBrush = isSelected
            ? (Brush)Application.Current.Resources["BandaBorderStrongBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        button.Foreground = isSelected
            ? (Brush)Application.Current.Resources["BandaAccentBrush"]
            : (Brush)Application.Current.Resources["BandaMutedStrongBrush"];
    }

    private async void ChangeSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        var previousSource =
            GetConfiguredFolderPath(
                SourceFolderText);
        var previousDestination =
            GetConfiguredFolderPath(
                DestinationFolderText);

        var wasUsingLinkedDestination =
            !string.IsNullOrWhiteSpace(previousSource) &&
            previousDestination.Equals(
                System.IO.Path.Combine(
                    previousSource,
                    "ORGANIZADO"),
                StringComparison.CurrentCultureIgnoreCase);

        SetConfiguredFolderText(
            SourceFolderText,
            folder.Path);

        if (wasUsingLinkedDestination)
        {
            SetConfiguredFolderText(
                DestinationFolderText,
                System.IO.Path.Combine(
                    folder.Path,
                    "ORGANIZADO"));
        }

        QueuePersistSettings();
        ShowSettingsFeedback("Carpeta de origen actualizada.");
    }

    private async void ChangeDestinationFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        SetConfiguredFolderText(
            DestinationFolderText,
            folder.Path);
        QueuePersistSettings();
        ShowSettingsFeedback("Carpeta de destino actualizada.");
    }

    private static string GetConfiguredFolderPath(
        TextBlock textBlock) =>
        textBlock.Text.Equals(
            UnselectedFolderText,
            StringComparison.CurrentCultureIgnoreCase)
            ? string.Empty
            : textBlock.Text.Trim();

    private static void SetConfiguredFolderText(
        TextBlock textBlock,
        string? path)
    {
        textBlock.Text =
            string.IsNullOrWhiteSpace(path)
                ? UnselectedFolderText
                : path.Trim();
    }

    private static async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var window = global::BandaNV.App.App.MainWindowInstance;
        if (window is null)
        {
            return null;
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private void StartupPageOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        StartupPageValueText.Text = value;
        StartupPageFlyout.Hide();
        QueuePersistSettings();
        ShowSettingsFeedback($"Página inicial: {value}.");
    }

    private void CloseBehaviorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        CloseBehaviorValueText.Text = value;
        CloseBehaviorFlyout.Hide();
        QueuePersistSettings();
        ShowSettingsFeedback($"Comportamiento al cerrar: {value}.");
    }

    private void ThemeOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        ThemeValueText.Text = value;
        ThemeFlyout.Hide();

        ApplyCurrentAppearance();
        UpdateAppearancePreview();
        QueuePersistSettings();

        ShowSettingsFeedback(
            $"Tema seleccionado: {value}.");
    }

    private void PrimaryColorPicker_ColorChanged(
        ColorPicker sender,
        ColorChangedEventArgs args)
    {
        if (!_isPageReady)
        {
            return;
        }

        ApplyPrimaryColor(args.NewColor);
        PrimaryColorHexText.Text = ToHex(args.NewColor);
        UpdateAppearancePreview();
        QueuePersistSettings();
    }

    private void SecondaryColorPicker_ColorChanged(
        ColorPicker sender,
        ColorChangedEventArgs args)
    {
        if (!_isPageReady)
        {
            return;
        }

        ApplySecondaryColor(args.NewColor);
        SecondaryColorHexText.Text = ToHex(args.NewColor);
        UpdateAppearancePreview();
        QueuePersistSettings();
    }

    private void ResetPrimaryColorButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.CreateDefault();

        if (!TryParseHexColor(defaults.PrimaryColor, out var primary))
        {
            return;
        }

        PrimaryColorPicker.Color = primary;
        PrimaryColorHexText.Text = ToHex(primary);
        ApplyPrimaryColor(primary);
        PrimaryColorFlyout.Hide();
        UpdateAppearancePreview();
        QueuePersistSettings();

        ShowSettingsFeedback("Color primario restablecido.");
    }

    private void ResetSecondaryColorButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.CreateDefault();

        if (!TryParseHexColor(defaults.SecondaryColor, out var secondary))
        {
            return;
        }

        SecondaryColorPicker.Color = secondary;
        SecondaryColorHexText.Text = ToHex(secondary);
        ApplySecondaryColor(secondary);
        SecondaryColorFlyout.Hide();
        UpdateAppearancePreview();
        QueuePersistSettings();

        ShowSettingsFeedback("Color secundario restablecido al teal original.");
    }

    private static void ApplyPrimaryColor(
        Windows.UI.Color color)
    {
        AppearanceService.ApplyPrimaryColor(
            color);
    }

    private void ApplySecondaryColor(
        Windows.UI.Color color)
    {
        AppearanceService.ApplySecondaryColor(
            color);

        AppearanceService.UpdateActionButtonResources(
            CheckUpdatesButton,
            color);
    }

    private static string ToHex(Windows.UI.Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseHexColor(
        string? value,
        out Windows.UI.Color color)
    {
        color = Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xC6);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Windows.UI.Color.FromArgb(255, r, g, b);
        return true;
    }

    private void ApplyCurrentAppearance()
    {
        AppearanceService.ApplyTheme(
            ThemeValueText.Text,
            PrimaryColorPicker.Color,
            SecondaryColorPicker.Color);

        AppearanceService.UpdateActionButtonResources(
            CheckUpdatesButton,
            SecondaryColorPicker.Color);

        global::BandaNV.App.App.MainWindowInstance?
            .ApplyThemeSetting(
                ThemeValueText.Text);
    }

    private void UpdateAppearancePreview()
    {
        if (!_isPageReady)
        {
            return;
        }

        AppearancePreviewDescriptionText.Text =
            $"{ThemeValueText.Text.ToLowerInvariant()} · " +
            $"primario {PrimaryColorHexText.Text} · " +
            $"secundario {SecondaryColorHexText.Text}";
    }

    private void ConflictBehaviorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        ConflictBehaviorValueText.Text = value;
        ConflictBehaviorFlyout.Hide();
        QueuePersistSettings();
        ShowSettingsFeedback($"Conflictos de nombre: {value}.");
    }

    private void UnknownExtensionBehaviorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        UnknownExtensionBehaviorValueText.Text = value;
        UnknownExtensionBehaviorFlyout.Hide();
        QueuePersistSettings();
        ShowSettingsFeedback($"Extensiones sin categoría: {value}.");
    }

    private async void HistoryRetentionOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        HistoryRetentionValueText.Text = value;
        HistoryRetentionFlyout.Hide();

        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = null;

        try
        {
            var snapshot =
                CapturePersistentSettings();

            await global::BandaNV.App.App.Settings.SaveAsync(
                snapshot);

            var result =
                await global::BandaNV.App.App.History.ApplyRetentionAsync(
                    global::BandaNV.App.App.Settings.Current);

            var removed =
                result.DeletedExecutions +
                result.DeletedLogs +
                result.DeletedBackupDirectories;

            ShowSettingsFeedback(
                removed == 0
                    ? $"Conservación del historial: {value}. No había archivos vencidos para limpiar."
                    : $"Conservación del historial: {value}. Se limpiaron {result.DeletedExecutions} ejecuciones, {result.DeletedLogs} logs y {result.DeletedBackupDirectories} carpetas de backup.");
        }
        catch (Exception ex)
        {
            ShowSettingsFeedback(
                $"La política se actualizó en pantalla, pero no se pudo completar el mantenimiento: {ex.Message}");
        }
    }

    private async void GenericSettingToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isPageReady)
        {
            return;
        }

        if (ReferenceEquals(
                sender,
                StartWithWindowsToggle))
        {
            try
            {
                WindowsStartupService.Apply(
                    StartWithWindowsToggle.IsOn);
            }
            catch (Exception ex)
            {
                var wasReady =
                    _isPageReady;

                _isPageReady =
                    false;

                StartWithWindowsToggle.IsOn =
                    global::BandaNV.App.App.Settings.Current
                        .StartWithWindows;

                _isPageReady =
                    wasReady;

                ShowSettingsFeedback(
                    $"No se pudo cambiar el inicio con Windows: {ex.Message}");

                return;
            }
        }

        if (ReferenceEquals(
                sender,
                AutoUpdateToggle))
        {
            global::BandaNV.App.App.MainWindowInstance?
                .SyncUpdateStartupNoticeToggle(
                    AutoUpdateToggle.IsOn);
        }

        if (ReferenceEquals(
                sender,
                SaveHistoryToggle))
        {
            UpdateHistoryDependentVisibility();
        }

        if (ReferenceEquals(
                sender,
                DeleteUnusedCategoryFoldersToggle) &&
            DeleteUnusedCategoryFoldersToggle.IsOn)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = null;

            try
            {
                await global::BandaNV.App.App.Settings.SaveAsync(
                    CapturePersistentSettings());

                var deleted =
                    await global::BandaNV.App.App.CategoryFolders
                        .CleanupUnusedCategoryFoldersAsync(
                            global::BandaNV.App.App.Settings.Current);

                await global::BandaNV.App.App.Settings.SaveAsync(
                    global::BandaNV.App.App.Settings.Current);

                ShowSettingsFeedback(
                    deleted == 0
                        ? "Preferencia actualizada. No había carpetas sin uso para eliminar."
                        : deleted == 1
                            ? "Preferencia actualizada. Se eliminó 1 carpeta de categoría sin uso."
                            : $"Preferencia actualizada. Se eliminaron {deleted} carpetas de categorías sin uso.");
            }
            catch (Exception ex)
            {
                ShowSettingsFeedback(
                    $"La preferencia se actualizó, pero no se pudo completar la limpieza: {ex.Message}");
            }

            return;
        }

        QueuePersistSettings();
        ShowSettingsFeedback("Preferencia actualizada.");
    }

    private void UpdateHistoryDependentVisibility()
    {
        var visibility =
            SaveHistoryToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;

        SaveOrganizeHistoryRow.Visibility =
            visibility;
        SaveSearchHistoryRow.Visibility =
            visibility;
        HistoryRetentionRow.Visibility =
            visibility;
    }

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        PortablePaths.EnsureDirectories();
        OpenFolder(PortablePaths.LogsDirectory, "Carpeta portable de logs abierta.");
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        PortablePaths.EnsureDirectories();
        OpenFolder(PortablePaths.RootDirectory, "Carpeta portable de BandaNV abierta.");
    }

    private void OpenFolder(string path, string successMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            ShowSettingsFeedback(successMessage);
        }
        catch
        {
            ShowSettingsFeedback("No se pudo abrir la carpeta.");
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsConfirmation(
            SettingsConfirmMode.ClearHistory,
            "Limpiar historial",
            "Historial, logs y backups",
            "¿Querés eliminar el historial guardado, sus logs y los backups protegidos asociados? Las ejecuciones que estuvieran activas se conservarán por seguridad. Esta acción no se puede deshacer.",
            "Limpiar historial");
    }

    private async void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = global::BandaNV.App.App.MainWindowInstance;
        if (window is null)
        {
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"BandaNV_config_{DateTime.Now:yyyy-MM-dd}"
        };

        picker.FileTypeChoices.Add(
            "Configuración BandaNV",
            new List<string> { ".bandanv" });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var backup = CaptureSettingsBackup();
            var json = JsonSerializer.Serialize(
                backup,
                new JsonSerializerOptions { WriteIndented = true });

            await Windows.Storage.FileIO.WriteTextAsync(file, json);

            BackupStatusText.Text = "Backup exportado";
            BackupDetailText.Text = $"{file.Name} · {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
            ShowSettingsFeedback($"Backup guardado como {file.Name}.");
        }
        catch
        {
            ShowSettingsFeedback("No se pudo exportar el backup de configuración.");
        }
    }

    private async void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = global::BandaNV.App.App.MainWindowInstance;
        if (window is null)
        {
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".bandanv");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            var backup = JsonSerializer.Deserialize<SettingsBackupModel>(json);

            if (backup is null ||
                !string.Equals(
                    backup.Format,
                    SettingsBackupModel.CurrentFormat,
                    StringComparison.Ordinal))
            {
                ShowSettingsFeedback("El archivo seleccionado no es un backup compatible de BandaNV.");
                return;
            }

            ApplySettingsBackup(backup);
            QueuePersistSettings();

            BackupStatusText.Text = "Backup importado";
            BackupDetailText.Text = $"{file.Name} · configuración aplicada";
            ShowSettingsFeedback($"Configuración importada desde {file.Name}.");
        }
        catch
        {
            ShowSettingsFeedback("No se pudo importar el backup seleccionado.");
        }
    }

    private SettingsBackupModel CaptureSettingsBackup()
    {
        return new SettingsBackupModel
        {
            CreatedAt = DateTime.Now,
            SourceFolder = GetConfiguredFolderPath(SourceFolderText),
            DestinationFolder = GetConfiguredFolderPath(DestinationFolderText),
            StartupPage = StartupPageValueText.Text,
            CloseBehavior = CloseBehaviorValueText.Text,
            StartWithWindows = StartWithWindowsToggle.IsOn,
            AutoUpdate = AutoUpdateToggle.IsOn,
            Theme = ThemeValueText.Text,
            PrimaryColor = PrimaryColorHexText.Text,
            SecondaryColor = SecondaryColorHexText.Text,
            PreviewBeforeOrganize = PreviewBeforeOrganizeToggle.IsOn,
            OrganizeFoldersAsUnits = OrganizeFoldersToggle.IsOn,
            CreateFolders = CreateFoldersToggle.IsOn,
            DeleteUnusedCategoryFolders = DeleteUnusedCategoryFoldersToggle.IsOn,
            ConflictBehavior = ConflictBehaviorValueText.Text,
            UnknownExtensionBehavior = UnknownExtensionBehaviorValueText.Text,
            RecycleBin = RecycleBinToggle.IsOn,
            ConfirmDestructive = ConfirmDestructiveToggle.IsOn,
            SaveHistory = SaveHistoryToggle.IsOn,
            SaveOrganizeHistory = SaveOrganizeHistoryToggle.IsOn,
            SaveSearchHistory = SaveSearchHistoryToggle.IsOn,
            HistoryRetention = HistoryRetentionValueText.Text,
            Categories = global::BandaNV.App.App.Settings.Current.Categories
                .Select(category => new CategorySettings(
                    category.Id,
                    category.Name,
                    category.Extensions,
                    category.Order,
                    category.ColorHex))
                .ToList()
        };
    }

    private void ApplySettingsBackup(SettingsBackupModel backup)
    {
        _isPageReady = false;

        SetConfiguredFolderText(
            SourceFolderText,
            backup.SourceFolder);
        SetConfiguredFolderText(
            DestinationFolderText,
            backup.DestinationFolder);
        StartupPageValueText.Text = backup.StartupPage;
        CloseBehaviorValueText.Text = backup.CloseBehavior;
        ThemeValueText.Text = backup.Theme;

        StartWithWindowsToggle.IsOn = backup.StartWithWindows;
        AutoUpdateToggle.IsOn = backup.AutoUpdate;
        PreviewBeforeOrganizeToggle.IsOn = backup.PreviewBeforeOrganize;
        OrganizeFoldersToggle.IsOn = backup.OrganizeFoldersAsUnits;
        CreateFoldersToggle.IsOn = backup.CreateFolders;
        DeleteUnusedCategoryFoldersToggle.IsOn =
            backup.DeleteUnusedCategoryFolders;
        ConflictBehaviorValueText.Text = backup.ConflictBehavior;
        UnknownExtensionBehaviorValueText.Text = backup.UnknownExtensionBehavior;
        RecycleBinToggle.IsOn = backup.RecycleBin;
        ConfirmDestructiveToggle.IsOn = backup.ConfirmDestructive;
        SaveHistoryToggle.IsOn = backup.SaveHistory;
        SaveOrganizeHistoryToggle.IsOn = backup.SaveOrganizeHistory;
        SaveSearchHistoryToggle.IsOn = backup.SaveSearchHistory;
        HistoryRetentionValueText.Text = backup.HistoryRetention;

        if (backup.Categories.Count > 0)
        {
            global::BandaNV.App.App.Settings.Current.Categories =
                backup.Categories
                    .Select(category => new CategorySettings(
                        category.Id,
                        category.Name,
                        category.Extensions,
                        category.Order,
                        category.ColorHex))
                    .ToList();
        }

        var defaultPrimary =
            Windows.UI.Color.FromArgb(255, 0x12, 0x3A, 0x34);
        var defaultSecondary =
            Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xC6);

        if (!TryParseHexColor(backup.PrimaryColor, out var primaryColor))
        {
            primaryColor = defaultPrimary;
        }

        var secondarySource = string.IsNullOrWhiteSpace(backup.SecondaryColor)
            ? backup.AccentColor
            : backup.SecondaryColor;

        if (!TryParseHexColor(secondarySource, out var secondaryColor))
        {
            secondaryColor = defaultSecondary;
        }

        PrimaryColorPicker.Color = primaryColor;
        PrimaryColorHexText.Text = ToHex(primaryColor);
        SecondaryColorPicker.Color = secondaryColor;
        SecondaryColorHexText.Text = ToHex(secondaryColor);

        UpdateHistoryDependentVisibility();
        _isPageReady = true;

        ApplyCurrentAppearance();

        try
        {
            WindowsStartupService.Apply(
                StartWithWindowsToggle.IsOn);
        }
        catch
        {
            var wasReady =
                _isPageReady;

            _isPageReady =
                false;

            StartWithWindowsToggle.IsOn =
                global::BandaNV.App.App.Settings.Current
                    .StartWithWindows;

            _isPageReady =
                wasReady;
        }

        global::BandaNV.App.App.MainWindowInstance?
            .SyncUpdateStartupNoticeToggle(
                AutoUpdateToggle.IsOn);

        UpdateAppearancePreview();
        SetSettingsSection(_currentSection);
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsConfirmation(
            SettingsConfirmMode.ResetSettings,
            "Restablecer configuración",
            "Volver a las preferencias predeterminadas",
            "Se restablecerán las preferencias de BandaNV a sus valores predeterminados. No se eliminarán categorías, historial, logs ni archivos organizados.",
            "Restablecer");
    }

    private void ClearAppDataButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsConfirmation(
            SettingsConfirmMode.ClearAppData,
            "Borrar datos de BandaNV",
            "Eliminar los datos guardados por la aplicación",
            "Se restaurarán las categorías y sus extensiones a los valores iniciales y se eliminarán el historial, los logs y los backups de seguridad que puedan borrarse de forma segura. Tus preferencias y los archivos que BandaNV ya organizó no se modificarán. Los registros técnicos necesarios para Deshacer pueden conservarse por seguridad. Esta acción no se puede deshacer.",
            "Borrar datos");
    }

    private async void CheckUpdatesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CheckForUpdatesAndOfferAsync(
            showNonAvailableResult: true);
    }

    private void ReleaseNotesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        AppVersionInfo.GitHubReleasesUrl,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            ShowSettingsFeedback(
                $"No se pudieron abrir las notas de versión: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesAndOfferAsync(
        bool showNonAvailableResult)
    {
        CheckUpdatesButton.IsEnabled = false;
        var previousContent =
            CheckUpdatesButton.Content;

        CheckUpdatesButton.Content =
            "Buscando...";

        try
        {
            var result =
                await global::BandaNV.App.App.Updates.CheckAsync();

            switch (result.Status)
            {
                case UpdateCheckStatus.Available:
                    if (global::BandaNV.App.App.MainWindowInstance is { } mainWindow)
                    {
                        mainWindow.SetAvailableUpdate(
                            result,
                            showModal: true);
                    }
                    else
                    {
                        ShowSettingsFeedback(
                            $"Nueva versión disponible: {result.AvailableVersion}.");
                    }
                    break;

                case UpdateCheckStatus.Current:
                    global::BandaNV.App.App.MainWindowInstance?
                        .ClearAvailableUpdate();

                    if (showNonAvailableResult)
                    {
                        ShowSettingsFeedback(
                            $"Estás usando la última versión disponible ({result.InstalledVersion}).");
                    }
                    break;

                case UpdateCheckStatus.LocalNewer:
                    global::BandaNV.App.App.MainWindowInstance?
                        .ClearAvailableUpdate();

                    if (showNonAvailableResult)
                    {
                        ShowSettingsFeedback(
                            $"Esta build ({result.InstalledVersion}) es más nueva que la última Release publicada ({result.AvailableVersion}).");
                    }
                    break;

                case UpdateCheckStatus.FailedSuppressed:
                    global::BandaNV.App.App.MainWindowInstance?
                        .ClearAvailableUpdate();

                    if (showNonAvailableResult)
                    {
                        ShowSettingsFeedback(
                            $"La versión {result.AvailableVersion} fue omitida porque una instalación anterior no pudo completarse. BandaNV volverá a ofrecer una Release posterior.");
                    }
                    break;

                default:
                    if (showNonAvailableResult ||
                        result.Status ==
                            UpdateCheckStatus.Error)
                    {
                        ShowSettingsFeedback(
                            string.IsNullOrWhiteSpace(
                                result.Message)
                                ? "No se pudo comprobar si hay actualizaciones."
                                : result.Message);
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (showNonAvailableResult)
            {
                ShowSettingsFeedback(
                    "Búsqueda de actualizaciones cancelada.");
            }
        }
        catch (Exception ex)
        {
            ShowSettingsFeedback(
                $"No se pudo comprobar si hay actualizaciones: {ex.Message}");
        }
        finally
        {
            CheckUpdatesButton.Content =
                previousContent ??
                "Buscar actualizaciones";
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenSettingsConfirmation(
        SettingsConfirmMode mode,
        string title,
        string subtitle,
        string body,
        string dangerButtonText)
    {
        _confirmMode = mode;

        SettingsConfirmTitleText.Text = title;
        SettingsConfirmSubtitleText.Text = subtitle;
        SettingsConfirmBodyText.Text = body;
        SettingsConfirmDangerButton.Content = dangerButtonText;
        SettingsConfirmOverlay.Visibility = Visibility.Visible;
    }

    private async void SettingsConfirmDangerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        switch (_confirmMode)
        {
            case SettingsConfirmMode.ClearHistory:
                SettingsConfirmDangerButton.IsEnabled = false;
                SettingsConfirmSecondaryButton.IsEnabled = false;

                try
                {
                    var result =
                        await global::BandaNV.App.App.History.ClearAsync();

                    CloseSettingsConfirmation();

                    var message =
                        $"Historial limpiado: {result.DeletedExecutions} ejecuciones, " +
                        $"{result.DeletedLogs} logs y " +
                        $"{result.DeletedBackupDirectories} carpetas de backup eliminadas.";

                    if (result.ProtectedExecutions > 0)
                    {
                        message +=
                            $" {result.ProtectedExecutions} ejecución{(result.ProtectedExecutions == 1 ? string.Empty : "es")} activa{(result.ProtectedExecutions == 1 ? string.Empty : "s")} se conservó{(result.ProtectedExecutions == 1 ? string.Empty : "aron")} por seguridad.";
                    }

                    ShowSettingsFeedback(message);
                }
                catch (Exception ex)
                {
                    CloseSettingsConfirmation();
                    ShowSettingsFeedback(
                        $"No se pudo completar la limpieza del historial: {ex.Message}");
                }
                finally
                {
                    SettingsConfirmDangerButton.IsEnabled = true;
                    SettingsConfirmSecondaryButton.IsEnabled = true;
                }
                break;

            case SettingsConfirmMode.ResetSettings:
                ResetVisibleSettingsToDefaults();
                CloseSettingsConfirmation();
                ShowSettingsFeedback("Configuración restablecida a sus valores predeterminados.");
                break;

            case SettingsConfirmMode.ClearAppData:
                SettingsConfirmDangerButton.IsEnabled = false;
                SettingsConfirmSecondaryButton.IsEnabled = false;

                try
                {
                    var historyResult =
                        await global::BandaNV.App.App.History.ClearAsync();

                    var defaultCategories =
                        AppSettings.CreateDefault().Categories;

                    global::BandaNV.App.App.Settings.Current
                        .OrphanedCategoryFolders
                        .Clear();

                    await global::BandaNV.App.App.Settings.UpdateCategoriesAsync(
                        defaultCategories);

                    CloseSettingsConfirmation();

                    var message =
                        $"Datos de BandaNV borrados: categorías y extensiones restauradas, " +
                        $"{historyResult.DeletedExecutions} ejecuciones, " +
                        $"{historyResult.DeletedLogs} logs y " +
                        $"{historyResult.DeletedBackupDirectories} carpetas de backup eliminadas.";

                    if (historyResult.ProtectedExecutions > 0)
                    {
                        message +=
                            $" {historyResult.ProtectedExecutions} registro{(historyResult.ProtectedExecutions == 1 ? string.Empty : "s")} técnico{(historyResult.ProtectedExecutions == 1 ? string.Empty : "s")} se conservó{(historyResult.ProtectedExecutions == 1 ? string.Empty : "aron")} por seguridad.";
                    }

                    ShowSettingsFeedback(message);
                }
                catch (Exception ex)
                {
                    CloseSettingsConfirmation();
                    ShowSettingsFeedback(
                        $"No se pudieron borrar todos los datos de BandaNV: {ex.Message}");
                }
                finally
                {
                    SettingsConfirmDangerButton.IsEnabled = true;
                    SettingsConfirmSecondaryButton.IsEnabled = true;
                }
                break;

            default:
                CloseSettingsConfirmation();
                break;
        }
    }

    private void ResetVisibleSettingsToDefaults()
    {
        var defaults =
            AppSettings.CreateDefault();

        defaults.Categories =
            global::BandaNV.App.App.Settings.Current.Categories
                .Select(category => new CategorySettings(
                    category.Id,
                    category.Name,
                    category.Extensions,
                    category.Order,
                    category.ColorHex))
                .ToList();

        defaults.OrphanedCategoryFolders =
            global::BandaNV.App.App.Settings.Current
                .OrphanedCategoryFolders
                .ToList();

        _isPageReady = false;

        LoadPersistentSettingsIntoUi(
            defaults);
        UpdateHistoryDependentVisibility();

        _isPageReady = true;

        ApplyCurrentAppearance();

        try
        {
            WindowsStartupService.Apply(
                defaults.StartWithWindows);
        }
        catch
        {
        }

        global::BandaNV.App.App.MainWindowInstance?
            .SyncUpdateStartupNoticeToggle(
                defaults.AutoUpdate);

        UpdateAppearancePreview();
        QueuePersistSettings();
    }

    private void LoadPersistentSettingsIntoUi(AppSettings settings)
    {
        SetConfiguredFolderText(
            SourceFolderText,
            settings.SourceFolder);
        SetConfiguredFolderText(
            DestinationFolderText,
            settings.DestinationFolder);
        StartupPageValueText.Text = settings.StartupPage;
        CloseBehaviorValueText.Text = settings.CloseBehavior;

        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
        AutoUpdateToggle.IsOn = settings.AutoUpdate;

        ThemeValueText.Text = settings.Theme;
        PreviewBeforeOrganizeToggle.IsOn = settings.PreviewBeforeOrganize;
        OrganizeFoldersToggle.IsOn = settings.OrganizeFoldersAsUnits;
        CreateFoldersToggle.IsOn = settings.CreateFolders;
        DeleteUnusedCategoryFoldersToggle.IsOn =
            settings.DeleteUnusedCategoryFolders;

        ConflictBehaviorValueText.Text = settings.ConflictBehavior;
        UnknownExtensionBehaviorValueText.Text = settings.UnknownExtensionBehavior;

        RecycleBinToggle.IsOn = settings.UseRecycleBin;
        ConfirmDestructiveToggle.IsOn = settings.ConfirmDestructiveActions;

        SaveHistoryToggle.IsOn = settings.SaveHistory;
        SaveOrganizeHistoryToggle.IsOn = settings.SaveOrganizeHistory;
        SaveSearchHistoryToggle.IsOn = settings.SaveSearchHistory;
        HistoryRetentionValueText.Text = settings.HistoryRetention;

        var defaultPrimary = Windows.UI.Color.FromArgb(255, 0x12, 0x3A, 0x34);
        var defaultSecondary = Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xC6);

        if (!TryParseHexColor(settings.PrimaryColor, out var primary))
        {
            primary = defaultPrimary;
        }

        if (!TryParseHexColor(settings.SecondaryColor, out var secondary))
        {
            secondary = defaultSecondary;
        }

        PrimaryColorPicker.Color = primary;
        PrimaryColorHexText.Text = ToHex(primary);
        SecondaryColorPicker.Color = secondary;
        SecondaryColorHexText.Text = ToHex(secondary);

        ApplyPrimaryColor(primary);
        ApplySecondaryColor(secondary);
    }

    private AppSettings CapturePersistentSettings()
    {
        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            SourceFolder = GetConfiguredFolderPath(SourceFolderText),
            DestinationFolder = GetConfiguredFolderPath(DestinationFolderText),
            StartupPage = StartupPageValueText.Text,
            CloseBehavior = CloseBehaviorValueText.Text,
            StartWithWindows = StartWithWindowsToggle.IsOn,
            AutoUpdate = AutoUpdateToggle.IsOn,
            Theme = ThemeValueText.Text,
            PrimaryColor = PrimaryColorHexText.Text,
            SecondaryColor = SecondaryColorHexText.Text,
            PreviewBeforeOrganize = PreviewBeforeOrganizeToggle.IsOn,
            OrganizeFoldersAsUnits = OrganizeFoldersToggle.IsOn,
            CreateFolders = CreateFoldersToggle.IsOn,
            DeleteUnusedCategoryFolders = DeleteUnusedCategoryFoldersToggle.IsOn,
            ConflictBehavior = ConflictBehaviorValueText.Text,
            UnknownExtensionBehavior = UnknownExtensionBehaviorValueText.Text,
            UseRecycleBin = RecycleBinToggle.IsOn,
            ConfirmDestructiveActions = ConfirmDestructiveToggle.IsOn,
            SaveHistory = SaveHistoryToggle.IsOn,
            SaveOrganizeHistory = SaveOrganizeHistoryToggle.IsOn,
            SaveSearchHistory = SaveSearchHistoryToggle.IsOn,
            HistoryRetention = HistoryRetentionValueText.Text,
            Categories = global::BandaNV.App.App.Settings.Current.Categories
                .Select(category => new CategorySettings(
                    category.Id,
                    category.Name,
                    category.Extensions,
                    category.Order,
                    category.ColorHex))
                .ToList(),
            OrphanedCategoryFolders =
                global::BandaNV.App.App.Settings.Current
                    .OrphanedCategoryFolders
                    .ToList()
        };
    }

    private void QueuePersistSettings()
    {
        if (!_isPageReady)
        {
            return;
        }

        var snapshot = CapturePersistentSettings();

        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = new CancellationTokenSource();

        _ = PersistSettingsAfterDelayAsync(
            snapshot,
            _saveDebounceCts.Token);
    }

    private static async Task PersistSettingsAfterDelayAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            await global::BandaNV.App.App.Settings.SaveAsync(
                settings,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SettingsConfirmBackdrop_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseSettingsConfirmation();
    }

    private void CloseSettingsConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSettingsConfirmation();
    }

    private void CloseSettingsConfirmation()
    {
        SettingsConfirmOverlay.Visibility = Visibility.Collapsed;
        _confirmMode = SettingsConfirmMode.None;
    }

    private void CloseSettingsFeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsFeedbackBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowSettingsFeedback(string message)
    {
        if (!_isPageReady)
        {
            return;
        }

        SettingsFeedbackText.Text = message;
        SettingsFeedbackBorder.Visibility = Visibility.Visible;
    }
}

internal enum SettingsSection
{
    General,
    Appearance,
    Organization,
    HistorySecurity,
    Advanced,
    About
}

internal enum SettingsConfirmMode
{
    None,
    ClearHistory,
    ResetSettings,
    ClearAppData
}

internal sealed class SettingsBackupModel
{
    public const string CurrentFormat = "BandaNV.SettingsBackup.v1";

    public string Format { get; set; } = CurrentFormat;
    public DateTime CreatedAt { get; set; }

    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string StartupPage { get; set; } = "Inicio";
    public string CloseBehavior { get; set; } = "Cerrar BandaNV";

    public bool StartWithWindows { get; set; }
    public bool AutoUpdate { get; set; } = true;

    public string Theme { get; set; } = "Oscuro";
    public string PrimaryColor { get; set; } = "#123A34";
    public string SecondaryColor { get; set; } = "#4FE0C6";

    // Compatibilidad con backups creados durante la primera maqueta
    // que solo tenían un color de acento.
    public string AccentColor { get; set; } = string.Empty;

    public bool PreviewBeforeOrganize { get; set; } = true;
    public bool OrganizeFoldersAsUnits { get; set; } = true;
    public bool CreateFolders { get; set; } = true;
    public bool DeleteUnusedCategoryFolders { get; set; } = true;

    public string ConflictBehavior { get; set; } = "Preguntar";
    public string UnknownExtensionBehavior { get; set; } = "Preguntar en la vista previa";

    public bool RecycleBin { get; set; } = true;
    public bool ConfirmDestructive { get; set; } = true;

    public bool SaveHistory { get; set; } = true;
    public bool SaveOrganizeHistory { get; set; } = true;
    public bool SaveSearchHistory { get; set; } = true;
    public string HistoryRetention { get; set; } = "Siempre";

    public List<CategorySettings> Categories { get; set; } = [];
}
