using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BandaNV.App.Pages;

public sealed partial class HistoryPage : Page
{
    public ObservableCollection<HistoryExecutionPreview> PreviewExecutions { get; } = new();
    public ObservableCollection<HistoryFilePreview> SelectedFiles { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();

        LoadPreviewData();
        HistoryList.SelectedIndex = 0;
    }

    private void LoadPreviewData()
    {
        PreviewExecutions.Clear();

        PreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "04/10/2026 · 00:47",
            Type = "ORGANIZAR",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads",
            Destination = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            FileCount = 34,
            FileCountText = "34",
            SizeText = "1.8 GB",
            CanUndo = true,
            UndoBadgeText = "Reversible",
            Files = BuildPrimaryPreviewFiles()
        });

        PreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "03/10/2026 · 18:12",
            Type = "ORGANIZAR",
            OriginShort = "Escritorio",
            Origin = @"C:\Users\Usuario\Desktop",
            Destination = @"C:\Users\Usuario\Desktop\ORGANIZADO",
            FileCount = 12,
            FileCountText = "12",
            SizeText = "420 MB",
            CanUndo = false,
            UndoBadgeText = "Ya deshecha",
            Files =
            [
                new HistoryFilePreview("brief.pdf", "DOCUMENTS", "3.8 MB"),
                new HistoryFilePreview("referencia.png", "IMAGES", "7.6 MB"),
                new HistoryFilePreview("entrega.zip", "RAR", "386 MB"),
                new HistoryFilePreview("audio.wav", "AUDIO", "22.6 MB")
            ]
        });

        PreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "02/10/2026 · 23:08",
            Type = "ORGANIZAR",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads",
            Destination = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            FileCount = 57,
            FileCountText = "57",
            SizeText = "3.1 GB",
            CanUndo = false,
            UndoBadgeText = "No reversible",
            Files =
            [
                new HistoryFilePreview("captura_01.png", "IMAGES", "5.1 MB"),
                new HistoryFilePreview("materiales.7z", "RAR", "1.7 GB"),
                new HistoryFilePreview("clase.mp4", "VIDEOS", "884 MB"),
                new HistoryFilePreview("fuentes.zip", "RAR", "118 MB"),
                new HistoryFilePreview("documentacion.pdf", "DOCUMENTS", "11.4 MB")
            ]
        });

        PreviewExecutions.Add(new HistoryExecutionPreview
        {
            DateTimeText = "01/10/2026 · 14:36",
            Type = "DESHACER",
            OriginShort = "Descargas",
            Origin = @"C:\Users\Usuario\Downloads\ORGANIZADO",
            Destination = @"C:\Users\Usuario\Downloads",
            FileCount = 12,
            FileCountText = "12",
            SizeText = "420 MB",
            CanUndo = false,
            UndoBadgeText = "Registro Undo",
            Files =
            [
                new HistoryFilePreview("brief.pdf", "DOCUMENTS", "3.8 MB"),
                new HistoryFilePreview("referencia.png", "IMAGES", "7.6 MB"),
                new HistoryFilePreview("entrega.zip", "RAR", "386 MB"),
                new HistoryFilePreview("audio.wav", "AUDIO", "22.6 MB")
            ]
        });
    }

    private static IReadOnlyList<HistoryFilePreview> BuildPrimaryPreviewFiles()
    {
        var files = new List<HistoryFilePreview>
        {
            new("foto_rolling_01.jpg", "IMAGES", "14.2 MB"),
            new("TP_final.pdf", "DOCUMENTS", "8.1 MB"),
            new("pack_autos.rar", "RAR", "1.2 GB"),
            new("video_final.mp4", "VIDEOS", "542 MB"),
            new("logo_nako.ai", "DESIGN", "35 MB"),
            new("referencia_01.png", "IMAGES", "6.4 MB"),
            new("referencia_02.webp", "IMAGES", "3.8 MB"),
            new("presupuesto.xlsx", "DOCUMENTS", "182 KB"),
            new("brief_cliente.docx", "DOCUMENTS", "1.1 MB"),
            new("tipografia.otf", "FONTS", "624 KB"),
            new("musica_demo.mp3", "AUDIO", "9.7 MB"),
            new("captura.gif", "GIF", "4.3 MB"),
            new("setup_herramienta.exe", "INSTALLERS", "118 MB"),
            new("recursos.7z", "RAR", "286 MB")
        };

        for (var index = files.Count + 1; index <= 34; index++)
        {
            files.Add(new HistoryFilePreview(
                $"archivo_{index:00}.png",
                "IMAGES",
                $"{2 + (index % 8)}.{index % 10} MB"));
        }

        return files;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryExecutionPreview execution)
        {
            ShowExecutionDetails(execution);
        }
    }

    private void ShowExecutionDetails(HistoryExecutionPreview execution)
    {
        DetailDateText.Text = $"{execution.DateTimeText} · {execution.Type}";
        DetailFileCountText.Text = execution.FileCountText;
        DetailSizeText.Text = execution.SizeText;
        DetailOriginText.Text = execution.Origin;
        DetailDestinationText.Text = execution.Destination;
        UndoStatusText.Text = execution.UndoBadgeText;
        UndoPreviewButton.IsEnabled = execution.CanUndo;

        var activeBackground = (Brush)Application.Current.Resources["BandaAccentSoftBrush"];
        var inactiveBackground = (Brush)Application.Current.Resources["BandaNavIconBrush"];
        var activeForeground = (Brush)Application.Current.Resources["BandaAccentBrush"];
        var inactiveForeground = (Brush)Application.Current.Resources["BandaMutedStrongBrush"];

        UndoStatusBorder.Background = execution.CanUndo ? activeBackground : inactiveBackground;
        UndoStatusText.Foreground = execution.CanUndo ? activeForeground : inactiveForeground;

        SelectedFiles.Clear();
        foreach (var file in execution.Files)
        {
            file.CanDelete = execution.CanUndo;
            file.DeleteVisibility = execution.CanUndo
                ? Visibility.Visible
                : Visibility.Collapsed;

            SelectedFiles.Add(file);
        }
    }

    private async void DeleteFilePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fileName } ||
            HistoryList.SelectedItem is not HistoryExecutionPreview { CanUndo: true })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Vista previa de eliminación",
            Content = $"La acción individual para \"{fileName}\" queda disponible únicamente en ejecuciones reversibles. La eliminación real se conectará junto con el motor de Historial/Undo y tendrá confirmación antes de modificar archivos.",
            CloseButtonText = "Entendido"
        };

        await dialog.ShowAsync();
    }

    private async void UndoPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryExecutionPreview { CanUndo: true })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Vista previa de Undo",
            Content = "El botón ya muestra cuándo una ejecución es reversible. La operación real se conectará cuando migremos el motor de logs y Undo.",
            CloseButtonText = "Entendido"
        };

        await dialog.ShowAsync();
    }
}

public sealed class HistoryExecutionPreview
{
    public string DateTimeText { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OriginShort { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public string FileCountText { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public bool CanUndo { get; set; }
    public string UndoBadgeText { get; set; } = string.Empty;
    public IReadOnlyList<HistoryFilePreview> Files { get; set; } = [];
}

public sealed class HistoryFilePreview
{
    public HistoryFilePreview()
    {
    }

    public HistoryFilePreview(string name, string category, string sizeText)
    {
        Name = name;
        Category = category;
        SizeText = sizeText;
    }

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public bool CanDelete { get; set; }
    public Visibility DeleteVisibility { get; set; } = Visibility.Collapsed;
}
