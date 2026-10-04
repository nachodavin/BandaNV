using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class SettingsPage : Page
{
    private SettingsSection _currentSection = SettingsSection.General;
    private SettingsConfirmMode _confirmMode = SettingsConfirmMode.None;
    private bool _isPageReady;

    public SettingsPage()
    {
        InitializeComponent();

        _isPageReady = true;
        SetSettingsSection(SettingsSection.General);
        UpdateAppearancePreview();
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

        var previousSource = SourceFolderText.Text;
        var previousDefaultDestination =
            System.IO.Path.Combine(previousSource, "ORGANIZADO");

        SourceFolderText.Text = folder.Path;

        if (DestinationFolderText.Text.Equals(
                previousDefaultDestination,
                StringComparison.CurrentCultureIgnoreCase))
        {
            DestinationFolderText.Text =
                System.IO.Path.Combine(folder.Path, "ORGANIZADO");
        }

        ShowSettingsFeedback("Carpeta de origen actualizada para esta configuración.");
    }

    private async void ChangeDestinationFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        DestinationFolderText.Text = folder.Path;
        ShowSettingsFeedback("Carpeta de destino actualizada para esta configuración.");
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
        UpdateAppearancePreview();

        ShowSettingsFeedback(
            $"Tema seleccionado: {value}. La aplicación global del tema se conectará al guardar preferencias reales.");
    }

    private void DensityOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        DensityValueText.Text = value;
        DensityFlyout.Hide();
        UpdateAppearancePreview();
        ShowSettingsFeedback($"Densidad de interfaz: {value}.");
    }

    private void UpdateAppearancePreview()
    {
        if (!_isPageReady)
        {
            return;
        }

        AppearancePreviewDescriptionText.Text =
            $"{ThemeValueText.Text.ToLowerInvariant()} · teal · {DensityValueText.Text.ToLowerInvariant()}";
    }

    private void ConflictBehaviorOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        ConflictBehaviorValueText.Text = value;
        ConflictBehaviorFlyout.Hide();
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
        ShowSettingsFeedback($"Extensiones sin categoría: {value}.");
    }

    private void HistoryRetentionOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        HistoryRetentionValueText.Text = value;
        HistoryRetentionFlyout.Hide();
        ShowSettingsFeedback($"Conservación del historial: {value}.");
    }

    private void GenericSettingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isPageReady)
        {
            return;
        }

        ShowSettingsFeedback("Preferencia actualizada en esta maqueta de Configuración.");
    }

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = EnsureBandaNvFolder("Logs");
        OpenFolder(path, "Carpeta de logs abierta.");
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = EnsureBandaNvFolder("Data");
        OpenFolder(path, "Carpeta de datos de BandaNV abierta.");
    }

    private static string EnsureBandaNvFolder(string childFolder)
    {
        var root = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BandaNV");

        var path = System.IO.Path.Combine(root, childFolder);
        System.IO.Directory.CreateDirectory(path);
        return path;
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
            "Historial y logs",
            "¿Querés limpiar el historial guardado? En esta maqueta la confirmación queda preparada; el borrado persistente se conectará cuando Historial deje de usar datos de prueba.",
            "Limpiar historial");
    }

    private void RebuildIndexButton_Click(object sender, RoutedEventArgs e)
    {
        SearchIndexStatusText.Text = "Reconstrucción preparada";
        SearchIndexDetailText.Text =
            "Maqueta · el motor de indexación persistente todavía no está conectado";

        ShowSettingsFeedback(
            "La acción de reconstrucción quedó preparada para el índice real de Buscar.");
    }

    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFeedback(
            "Exportar configuración quedó preparado para cuando conectemos la persistencia real.");
    }

    private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFeedback(
            "Importar configuración quedó preparado para cuando conectemos la persistencia real.");
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsConfirmation(
            SettingsConfirmMode.ResetSettings,
            "Restablecer configuración",
            "Volver a los valores iniciales",
            "Se restablecerán las preferencias visibles de esta maqueta. Las categorías y los archivos organizados no se eliminan.",
            "Restablecer");
    }

    private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFeedback(
            "Búsqueda de actualizaciones preparada. El servicio de actualización de v2.0 todavía no está conectado.");
    }

    private void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFeedback(
            "Las notas de versión se conectarán cuando definamos el flujo final de actualizaciones.");
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

    private void SettingsConfirmDangerButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_confirmMode)
        {
            case SettingsConfirmMode.ClearHistory:
                CloseSettingsConfirmation();
                ShowSettingsFeedback(
                    "Confirmación aplicada en la maqueta. El historial persistente se limpiará desde este mismo flujo.");
                break;

            case SettingsConfirmMode.ResetSettings:
                ResetVisibleSettingsToDefaults();
                CloseSettingsConfirmation();
                ShowSettingsFeedback("Configuración visible restablecida a sus valores iniciales.");
                break;

            default:
                CloseSettingsConfirmation();
                break;
        }
    }

    private void ResetVisibleSettingsToDefaults()
    {
        SourceFolderText.Text = @"C:\Users\Usuario\Downloads";
        DestinationFolderText.Text = @"C:\Users\Usuario\Downloads\ORGANIZADO";

        StartupPageValueText.Text = "Inicio";
        CloseBehaviorValueText.Text = "Cerrar BandaNV";

        ThemeValueText.Text = "Oscuro";
        DensityValueText.Text = "Cómoda";

        ConflictBehaviorValueText.Text = "Preguntar";
        UnknownExtensionBehaviorValueText.Text = "Preguntar en la vista previa";
        HistoryRetentionValueText.Text = "Siempre";

        _isPageReady = false;

        StartWithWindowsToggle.IsOn = false;
        AutoUpdateToggle.IsOn = true;
        AnimationsToggle.IsOn = true;
        PreviewBeforeOrganizeToggle.IsOn = true;
        IncludeSubfoldersToggle.IsOn = false;
        CreateFoldersToggle.IsOn = true;
        DeleteEmptyFoldersToggle.IsOn = false;
        UndoToggle.IsOn = true;
        RecycleBinToggle.IsOn = true;
        ConfirmDestructiveToggle.IsOn = true;
        SaveHistoryToggle.IsOn = true;

        _isPageReady = true;

        SearchIndexStatusText.Text = "Listo para conectar";
        SearchIndexDetailText.Text = "Maqueta · todavía sin índice persistente";

        UpdateAppearancePreview();
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
    ResetSettings
}
