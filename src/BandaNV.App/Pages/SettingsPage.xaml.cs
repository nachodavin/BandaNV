using System.Diagnostics;
using System.Text.Json;
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
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isPageReady;

    public SettingsPage()
    {
        InitializeComponent();

        _isPageReady = false;
        LoadPersistentSettingsIntoUi(global::BandaNV.App.App.Settings.Current);
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

        DestinationFolderText.Text = folder.Path;
        QueuePersistSettings();
        ShowSettingsFeedback("Carpeta de destino actualizada.");
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
        UpdateAppearancePreview();
        QueuePersistSettings();

        ShowSettingsFeedback(
            $"Tema seleccionado: {value}. El cambio global de tema se conectará en la siguiente etapa de Apariencia.");
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
        var primary = Windows.UI.Color.FromArgb(255, 0x12, 0x3A, 0x34);

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
        var secondary = Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xC6);

        SecondaryColorPicker.Color = secondary;
        SecondaryColorHexText.Text = ToHex(secondary);
        ApplySecondaryColor(secondary);
        SecondaryColorFlyout.Hide();
        UpdateAppearancePreview();
        QueuePersistSettings();

        ShowSettingsFeedback("Color secundario restablecido al teal original.");
    }

    private static void ApplyPrimaryColor(Windows.UI.Color color)
    {
        SetSolidBrushColor("BandaPrimaryColorBrush", color);

        var backgroundBase = Windows.UI.Color.FromArgb(255, 0x06, 0x0A, 0x0D);
        var sidebarBase = Windows.UI.Color.FromArgb(255, 0x07, 0x0C, 0x10);
        var cardBase = Windows.UI.Color.FromArgb(255, 0x0C, 0x12, 0x16);
        var cardAltBase = Windows.UI.Color.FromArgb(255, 0x0D, 0x14, 0x18);
        var popupBase = Windows.UI.Color.FromArgb(255, 0x08, 0x0E, 0x12);
        var borderBase = Windows.UI.Color.FromArgb(255, 0x43, 0x50, 0x59);

        var background = BlendColor(backgroundBase, color, 0.07);
        var sidebar = BlendColor(sidebarBase, color, 0.09);
        var card = BlendColor(cardBase, color, 0.10);
        var cardAlt = BlendColor(cardAltBase, color, 0.14);
        var navIcon = BlendColor(cardAltBase, color, 0.16);
        var popup = BlendColor(popupBase, color, 0.10);
        var border = BlendColor(borderBase, color, 0.10);
        var borderStrong = BlendColor(borderBase, color, 0.16);
        var accentCard = BlendColor(cardBase, color, 0.22);

        SetSolidBrushColor("BandaBackgroundBrush", background);
        SetSolidBrushColor("BandaAppBackgroundGradient", background);
        SetSolidBrushColor("BandaSidebarBrush", sidebar);
        SetSolidBrushColor("BandaSidebarGradientBrush", sidebar);
        SetSolidBrushColor("BandaCardBrush", card);
        SetSolidBrushColor("BandaCardAltBrush", cardAlt);
        SetSolidBrushColor("BandaSurfaceBrush", card);
        SetSolidBrushColor("BandaSurfaceAltBrush", cardAlt);
        SetSolidBrushColor("BandaNavIconBrush", navIcon);
        SetSolidBrushColor("BandaPopupSurfaceBrush", popup);
        SetSolidBrushColor("BandaBorderBrush", WithAlpha(border, 0x4A));
        SetSolidBrushColor("BandaBorderStrongBrush", WithAlpha(borderStrong, 0x72));
        SetSolidBrushColor("BandaAccentCardBrush", accentCard);
    }

    private void ApplySecondaryColor(Windows.UI.Color color)
    {
        SetSolidBrushColor("BandaSecondaryColorBrush", color);
        SetSolidBrushColor("BandaAccentBrush", color);
        SetSolidBrushColor("BandaAccentSoftBrush", WithAlpha(color, 0x24));
        SetSolidBrushColor("BandaAccentFaintBrush", WithAlpha(color, 0x12));
        SetSolidBrushColor("BandaNavActiveBrush", WithAlpha(color, 0x22));
        SetSolidBrushColor("BandaAccentGradientBrush", color);

        var hover = BlendColor(color, Windows.UI.Color.FromArgb(255, 255, 255, 255), 0.14);
        var pressed = ScaleColor(color, 0.80);
        var secondaryHighlight = BlendColor(
            color,
            Windows.UI.Color.FromArgb(255, 255, 255, 255),
            0.18);
        var foreground = GetReadableForeground(color);

        SetSolidBrushColor("BandaSecondaryAccentBrush", secondaryHighlight);
        SetSolidBrushColor("BandaActionHoverBrush", hover);
        SetSolidBrushColor("BandaActionPressedBrush", pressed);
        SetSolidBrushColor("BandaActionForegroundBrush", foreground);

        SetColorResource("BandaActionBaseColor", color);
        SetColorResource("BandaActionHoverColor", hover);
        SetColorResource("BandaActionPressedColor", pressed);
        SetColorResource("BandaActionForegroundColor", foreground);
        SetColorResource("BandaSelectionColor", WithAlpha(color, 0x24));
        SetColorResource("BandaSelectionHoverColor", WithAlpha(color, 0x30));

        UpdateLocalActionButtonResources(
            CheckUpdatesButton,
            color,
            hover,
            pressed,
            foreground);

        SetSolidBrushColor("ToggleSwitchFillOn", color);
        SetSolidBrushColor("ToggleSwitchFillOnPointerOver", hover);
        SetSolidBrushColor("ToggleSwitchFillOnPressed", pressed);
        SetSolidBrushColor("ToggleSwitchStrokeOn", color);
        SetSolidBrushColor("ToggleSwitchStrokeOnPointerOver", hover);
        SetSolidBrushColor("ToggleSwitchStrokeOnPressed", pressed);
        SetSolidBrushColor("ToggleSwitchKnobFillOn", foreground);
        SetSolidBrushColor("ToggleSwitchKnobFillOnPointerOver", foreground);
        SetSolidBrushColor("ToggleSwitchKnobFillOnPressed", foreground);
    }

    private static Windows.UI.Color GetReadableForeground(Windows.UI.Color background)
    {
        var luminance =
            (0.2126 * background.R +
             0.7152 * background.G +
             0.0722 * background.B) / 255.0;

        return luminance >= 0.56
            ? Windows.UI.Color.FromArgb(255, 0x07, 0x11, 0x0F)
            : Windows.UI.Color.FromArgb(255, 0xF5, 0xF8, 0xFA);
    }

    private static void UpdateLocalActionButtonResources(
        Button button,
        Windows.UI.Color normal,
        Windows.UI.Color hover,
        Windows.UI.Color pressed,
        Windows.UI.Color foreground)
    {
        if (button.Resources["ButtonBackground"] is SolidColorBrush background)
        {
            background.Color = normal;
        }

        if (button.Resources["ButtonBackgroundPointerOver"] is SolidColorBrush backgroundHover)
        {
            backgroundHover.Color = hover;
        }

        if (button.Resources["ButtonBackgroundPressed"] is SolidColorBrush backgroundPressed)
        {
            backgroundPressed.Color = pressed;
        }

        if (button.Resources["ButtonBorderBrush"] is SolidColorBrush border)
        {
            border.Color = normal;
        }

        if (button.Resources["ButtonBorderBrushPointerOver"] is SolidColorBrush borderHover)
        {
            borderHover.Color = hover;
        }

        if (button.Resources["ButtonBorderBrushPressed"] is SolidColorBrush borderPressed)
        {
            borderPressed.Color = pressed;
        }

        foreach (var key in new[]
                 {
                     "ButtonForeground",
                     "ButtonForegroundPointerOver",
                     "ButtonForegroundPressed"
                 })
        {
            if (button.Resources[key] is SolidColorBrush brush)
            {
                brush.Color = foreground;
            }
        }
    }

    private static void SetSolidBrushColor(string resourceKey, Windows.UI.Color color)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static void SetColorResource(string resourceKey, Windows.UI.Color color)
    {
        Application.Current.Resources[resourceKey] = color;
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha) =>
        Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Windows.UI.Color ScaleColor(Windows.UI.Color color, double factor) =>
        Windows.UI.Color.FromArgb(
            color.A,
            (byte)Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    private static Windows.UI.Color BlendColor(
        Windows.UI.Color baseColor,
        Windows.UI.Color accent,
        double accentWeight)
    {
        var baseWeight = 1.0 - accentWeight;

        return Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Clamp((int)Math.Round(baseColor.R * baseWeight + accent.R * accentWeight), 0, 255),
            (byte)Math.Clamp((int)Math.Round(baseColor.G * baseWeight + accent.G * accentWeight), 0, 255),
            (byte)Math.Clamp((int)Math.Round(baseColor.B * baseWeight + accent.B * accentWeight), 0, 255));
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

    private void HistoryRetentionOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value })
        {
            return;
        }

        HistoryRetentionValueText.Text = value;
        HistoryRetentionFlyout.Hide();
        QueuePersistSettings();
        ShowSettingsFeedback($"Conservación del historial: {value}.");
    }

    private void GenericSettingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isPageReady)
        {
            return;
        }

        QueuePersistSettings();
        ShowSettingsFeedback("Preferencia actualizada.");
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
            "Historial y logs",
            "¿Querés limpiar el historial guardado? En esta maqueta la confirmación queda preparada; el borrado persistente se conectará cuando Historial deje de usar datos de prueba.",
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
            SourceFolder = SourceFolderText.Text,
            DestinationFolder = DestinationFolderText.Text,
            StartupPage = StartupPageValueText.Text,
            CloseBehavior = CloseBehaviorValueText.Text,
            StartWithWindows = StartWithWindowsToggle.IsOn,
            AutoUpdate = AutoUpdateToggle.IsOn,
            Theme = ThemeValueText.Text,
            PrimaryColor = PrimaryColorHexText.Text,
            SecondaryColor = SecondaryColorHexText.Text,
            Animations = AnimationsToggle.IsOn,
            PreviewBeforeOrganize = PreviewBeforeOrganizeToggle.IsOn,
            IncludeSubfolders = IncludeSubfoldersToggle.IsOn,
            CreateFolders = CreateFoldersToggle.IsOn,
            DeleteEmptyFolders = DeleteEmptyFoldersToggle.IsOn,
            ConflictBehavior = ConflictBehaviorValueText.Text,
            UnknownExtensionBehavior = UnknownExtensionBehaviorValueText.Text,
            Undo = UndoToggle.IsOn,
            RecycleBin = RecycleBinToggle.IsOn,
            ConfirmDestructive = ConfirmDestructiveToggle.IsOn,
            SaveHistory = SaveHistoryToggle.IsOn,
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

        SourceFolderText.Text = backup.SourceFolder;
        DestinationFolderText.Text = backup.DestinationFolder;
        StartupPageValueText.Text = backup.StartupPage;
        CloseBehaviorValueText.Text = backup.CloseBehavior;
        ThemeValueText.Text = backup.Theme;

        StartWithWindowsToggle.IsOn = backup.StartWithWindows;
        AutoUpdateToggle.IsOn = backup.AutoUpdate;
        AnimationsToggle.IsOn = backup.Animations;
        PreviewBeforeOrganizeToggle.IsOn = backup.PreviewBeforeOrganize;
        IncludeSubfoldersToggle.IsOn = backup.IncludeSubfolders;
        CreateFoldersToggle.IsOn = backup.CreateFolders;
        DeleteEmptyFoldersToggle.IsOn = backup.DeleteEmptyFolders;
        ConflictBehaviorValueText.Text = backup.ConflictBehavior;
        UnknownExtensionBehaviorValueText.Text = backup.UnknownExtensionBehavior;
        UndoToggle.IsOn = backup.Undo;
        RecycleBinToggle.IsOn = backup.RecycleBin;
        ConfirmDestructiveToggle.IsOn = backup.ConfirmDestructive;
        SaveHistoryToggle.IsOn = backup.SaveHistory;
        HistoryRetentionValueText.Text = backup.HistoryRetention;

        if (backup.Categories.Count > 0)
        {
            global::BandaNV.App.App.Settings.Current.Categories =
                backup.Categories
                    .Select(category => new CategorySettings(
                        category.Id,
                        category.Name,
                        category.Extensions,
                        category.Order))
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

        _isPageReady = true;

        ApplyPrimaryColor(primaryColor);
        ApplySecondaryColor(secondaryColor);
        UpdateAppearancePreview();
        SetSettingsSection(_currentSection);
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
        var primary = Windows.UI.Color.FromArgb(255, 0x12, 0x3A, 0x34);
        var secondary = Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xC6);

        _isPageReady = false;

        var defaults = AppSettings.CreateDefault();

        SourceFolderText.Text = defaults.SourceFolder;
        DestinationFolderText.Text = defaults.DestinationFolder;

        StartupPageValueText.Text = "Inicio";
        CloseBehaviorValueText.Text = "Cerrar BandaNV";

        StartWithWindowsToggle.IsOn = false;
        AutoUpdateToggle.IsOn = true;

        ThemeValueText.Text = "Oscuro";
        PrimaryColorPicker.Color = primary;
        PrimaryColorHexText.Text = ToHex(primary);
        SecondaryColorPicker.Color = secondary;
        SecondaryColorHexText.Text = ToHex(secondary);
        AnimationsToggle.IsOn = true;

        PreviewBeforeOrganizeToggle.IsOn = true;
        IncludeSubfoldersToggle.IsOn = false;
        CreateFoldersToggle.IsOn = true;
        DeleteEmptyFoldersToggle.IsOn = false;

        ConflictBehaviorValueText.Text = "Preguntar";
        UnknownExtensionBehaviorValueText.Text = "Preguntar en la vista previa";

        UndoToggle.IsOn = true;
        RecycleBinToggle.IsOn = true;
        ConfirmDestructiveToggle.IsOn = true;

        SaveHistoryToggle.IsOn = true;
        HistoryRetentionValueText.Text = "Siempre";

        _isPageReady = true;

        ApplyPrimaryColor(primary);
        ApplySecondaryColor(secondary);
        UpdateAppearancePreview();
        QueuePersistSettings();
    }

    private void LoadPersistentSettingsIntoUi(AppSettings settings)
    {
        SourceFolderText.Text = settings.SourceFolder;
        DestinationFolderText.Text = settings.DestinationFolder;
        StartupPageValueText.Text = settings.StartupPage;
        CloseBehaviorValueText.Text = settings.CloseBehavior;

        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
        AutoUpdateToggle.IsOn = settings.AutoUpdate;

        ThemeValueText.Text = settings.Theme;
        AnimationsToggle.IsOn = settings.Animations;

        PreviewBeforeOrganizeToggle.IsOn = settings.PreviewBeforeOrganize;
        IncludeSubfoldersToggle.IsOn = settings.IncludeSubfolders;
        CreateFoldersToggle.IsOn = settings.CreateFolders;
        DeleteEmptyFoldersToggle.IsOn = settings.DeleteEmptyFolders;

        ConflictBehaviorValueText.Text = settings.ConflictBehavior;
        UnknownExtensionBehaviorValueText.Text = settings.UnknownExtensionBehavior;

        UndoToggle.IsOn = settings.UndoEnabled;
        RecycleBinToggle.IsOn = settings.UseRecycleBin;
        ConfirmDestructiveToggle.IsOn = settings.ConfirmDestructiveActions;

        SaveHistoryToggle.IsOn = settings.SaveHistory;
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
            SourceFolder = SourceFolderText.Text.Trim(),
            DestinationFolder = DestinationFolderText.Text.Trim(),
            StartupPage = StartupPageValueText.Text,
            CloseBehavior = CloseBehaviorValueText.Text,
            StartWithWindows = StartWithWindowsToggle.IsOn,
            AutoUpdate = AutoUpdateToggle.IsOn,
            Theme = ThemeValueText.Text,
            PrimaryColor = PrimaryColorHexText.Text,
            SecondaryColor = SecondaryColorHexText.Text,
            Animations = AnimationsToggle.IsOn,
            PreviewBeforeOrganize = PreviewBeforeOrganizeToggle.IsOn,
            IncludeSubfolders = IncludeSubfoldersToggle.IsOn,
            CreateFolders = CreateFoldersToggle.IsOn,
            DeleteEmptyFolders = DeleteEmptyFoldersToggle.IsOn,
            ConflictBehavior = ConflictBehaviorValueText.Text,
            UnknownExtensionBehavior = UnknownExtensionBehaviorValueText.Text,
            UndoEnabled = UndoToggle.IsOn,
            UseRecycleBin = RecycleBinToggle.IsOn,
            ConfirmDestructiveActions = ConfirmDestructiveToggle.IsOn,
            SaveHistory = SaveHistoryToggle.IsOn,
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
    ResetSettings
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

    public bool Animations { get; set; } = true;

    public bool PreviewBeforeOrganize { get; set; } = true;
    public bool IncludeSubfolders { get; set; }
    public bool CreateFolders { get; set; } = true;
    public bool DeleteEmptyFolders { get; set; }

    public string ConflictBehavior { get; set; } = "Preguntar";
    public string UnknownExtensionBehavior { get; set; } = "Preguntar en la vista previa";

    public bool Undo { get; set; } = true;
    public bool RecycleBin { get; set; } = true;
    public bool ConfirmDestructive { get; set; } = true;

    public bool SaveHistory { get; set; } = true;
    public string HistoryRetention { get; set; } = "Siempre";

    public List<CategorySettings> Categories { get; set; } = [];
}
