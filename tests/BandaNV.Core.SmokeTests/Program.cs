using BandaNV.Core.Models;
using BandaNV.Core.Services;

namespace BandaNV.Core.SmokeTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    public static async Task<int> Main()
    {
        Console.OutputEncoding =
            System.Text.Encoding.UTF8;

        Console.WriteLine();
        Console.WriteLine(
            "BandaNV v2.0 - Smoke tests del motor");
        Console.WriteLine();

        await RunAsync(
            "Carpeta profunda de una categoría se mantiene como una unidad",
            DeepSingleCategoryFolderAsync);

        await RunAsync(
            "Carpeta con categorías mezcladas queda sin asignar",
            MixedCategoryFolderAsync);

        await RunAsync(
            "Archivo desconocido impide autoclasificación de la carpeta",
            UnknownExtensionFolderAsync);

        await RunAsync(
            "Carpeta madre vacía sigue apareciendo en el análisis",
            EmptyTopLevelFolderAsync);

        await RunAsync(
            "Subcarpetas vacías forman parte del fingerprint seguro",
            EmptyNestedDirectoryChangesFingerprintAsync);

        await RunAsync(
            "Destino dentro del origen queda excluido del análisis",
            DestinationInsideSourceIsExcludedAsync);

        await RunAsync(
            "Mover una carpeta conserva toda su estructura interna",
            MoveFolderPreservesTreeAsync);

        Console.WriteLine();
        Console.WriteLine(
            $"Resultado: {_passed} OK · {_failed} error(es)");

        return _failed == 0
            ? 0
            : 1;
    }

    private static async Task RunAsync(
        string name,
        Func<Task> test)
    {
        try
        {
            await test();

            _passed++;
            Console.WriteLine(
                $"[OK] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine(
                $"[ERROR] {name}");
            Console.WriteLine(
                $"        {ex.Message}");
        }
    }

    private static async Task DeepSingleCategoryFolderAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var mother =
            Path.Combine(
                workspace.Source,
                "Madre");

        WriteFile(
            Path.Combine(
                mother,
                "nivel1",
                "nivel2",
                "foto.jpg"),
            "jpg");

        WriteFile(
            Path.Combine(
                mother,
                "nivel1",
                "otra.png"),
            "png");

        var result =
            await AnalyzeAsync(
                workspace);

        var folder =
            SingleFolder(
                result,
                "Madre");

        Equal(
            "IMAGES",
            folder.CategoryName,
            "La carpeta debería inferirse como IMAGES.");

        Equal(
            2,
            folder.ContainedFileCount,
            "La carpeta debería contar dos archivos.");

        Equal(
            2,
            folder.RecognizedFileCount,
            "Ambos archivos deberían reconocerse.");

        Equal(
            1,
            folder.DistinctCategoryCount,
            "Solo debería detectarse una categoría.");

        True(
            folder.FolderContents.Any(item =>
                item.IsDirectory &&
                item.RelativePath.Equals(
                    Path.Combine(
                        "nivel1",
                        "nivel2"),
                    StringComparison.OrdinalIgnoreCase)),
            "La subcarpeta profunda debería aparecer en el contenido.");

        True(
            folder.FolderContents.Any(item =>
                !item.IsDirectory &&
                item.RelativePath.Equals(
                    Path.Combine(
                        "nivel1",
                        "nivel2",
                        "foto.jpg"),
                    StringComparison.OrdinalIgnoreCase)),
            "El archivo profundo debería conservar su ruta relativa.");
    }

    private static async Task MixedCategoryFolderAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var mother =
            Path.Combine(
                workspace.Source,
                "Mixta");

        WriteFile(
            Path.Combine(
                mother,
                "foto.jpg"),
            "jpg");

        WriteFile(
            Path.Combine(
                mother,
                "documento.pdf"),
            "pdf");

        var result =
            await AnalyzeAsync(
                workspace);

        var folder =
            SingleFolder(
                result,
                "Mixta");

        True(
            !folder.IsClassified,
            "Una carpeta con dos categorías no debería autoclasificarse.");

        Equal(
            2,
            folder.DistinctCategoryCount,
            "Deberían detectarse dos categorías.");

        Equal(
            2,
            folder.RecognizedFileCount,
            "Los dos archivos deberían estar reconocidos.");
    }

    private static async Task UnknownExtensionFolderAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var mother =
            Path.Combine(
                workspace.Source,
                "ConDesconocido");

        WriteFile(
            Path.Combine(
                mother,
                "foto.jpg"),
            "jpg");

        WriteFile(
            Path.Combine(
                mother,
                "misterio.zzz"),
            "???");

        var result =
            await AnalyzeAsync(
                workspace);

        var folder =
            SingleFolder(
                result,
                "ConDesconocido");

        True(
            !folder.IsClassified,
            "Una carpeta con archivos desconocidos no debería autoclasificarse.");

        Equal(
            2,
            folder.ContainedFileCount,
            "Deberían contarse ambos archivos.");

        Equal(
            1,
            folder.RecognizedFileCount,
            "Solo el JPG debería estar reconocido.");

        Equal(
            1,
            folder.DistinctCategoryCount,
            "Solo debería detectarse la categoría IMAGES.");
    }

    private static async Task EmptyTopLevelFolderAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var empty =
            Path.Combine(
                workspace.Source,
                "Vacia");

        Directory.CreateDirectory(
            empty);

        var result =
            await AnalyzeAsync(
                workspace);

        var folder =
            SingleFolder(
                result,
                "Vacia");

        Equal(
            0,
            folder.ContainedFileCount,
            "Una carpeta vacía debería informar cero archivos.");

        True(
            !folder.IsClassified,
            "Una carpeta vacía debería quedar sin asignar.");

        True(
            !string.IsNullOrWhiteSpace(
                folder.ContentFingerprint),
            "Una carpeta vacía debería tener fingerprint.");

        var snapshot =
            OrganizationEntrySafety.GetDirectorySnapshot(
                empty);

        Equal(
            folder.ContentFingerprint,
            snapshot.ContentFingerprint,
            "El fingerprint del análisis debería coincidir con el snapshot seguro.");
    }

    private static async Task EmptyNestedDirectoryChangesFingerprintAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var mother =
            Path.Combine(
                workspace.Source,
                "Estructura");

        Directory.CreateDirectory(
            Path.Combine(
                mother,
                "VaciaA"));

        var result =
            await AnalyzeAsync(
                workspace);

        var folder =
            SingleFolder(
                result,
                "Estructura");

        var executionItem =
            new OrganizationExecutionItemRecord
            {
                FileName =
                    folder.FileName,
                OriginalPath =
                    folder.FullPath,
                SizeBytes =
                    folder.SizeBytes,
                ModifiedUtcTicks =
                    folder.ModifiedUtcTicks,
                Kind =
                    OrganizationAnalysisItemKind.Folder,
                ContainedFileCount =
                    folder.ContainedFileCount,
                ContentFingerprint =
                    folder.ContentFingerprint
            };

        True(
            OrganizationEntrySafety.MatchesExpected(
                mother,
                executionItem),
            "La carpeta debería coincidir inmediatamente después del análisis.");

        Directory.Move(
            Path.Combine(
                mother,
                "VaciaA"),
            Path.Combine(
                mother,
                "VaciaB"));

        True(
            !OrganizationEntrySafety.MatchesExpected(
                mother,
                executionItem),
            "Renombrar una subcarpeta vacía debería invalidar el fingerprint.");

        var snapshot =
            OrganizationEntrySafety.GetDirectorySnapshot(
                mother);

        True(
            !string.Equals(
                folder.ContentFingerprint,
                snapshot.ContentFingerprint,
                StringComparison.OrdinalIgnoreCase),
            "El fingerprint debería cambiar aunque no haya archivos.");
    }

    private static async Task DestinationInsideSourceIsExcludedAsync()
    {
        using var workspace =
            TestWorkspace.Create(
                destinationInsideSource:
                    true);

        Directory.CreateDirectory(
            workspace.Destination);

        WriteFile(
            Path.Combine(
                workspace.Destination,
                "no_debe_aparecer.jpg"),
            "destino");

        WriteFile(
            Path.Combine(
                workspace.Source,
                "Valida",
                "foto.jpg"),
            "origen");

        var result =
            await AnalyzeAsync(
                workspace);

        True(
            result.Files.All(item =>
                !item.FullPath.Equals(
                    workspace.Destination,
                    StringComparison.OrdinalIgnoreCase) &&
                !item.FullPath.StartsWith(
                    workspace.Destination +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)),
            "La carpeta destino y su contenido no deberían analizarse como origen.");

        SingleFolder(
            result,
            "Valida");
    }

    private static Task MoveFolderPreservesTreeAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var source =
            Path.Combine(
                workspace.Source,
                "Mover");

        var target =
            Path.Combine(
                workspace.Destination,
                "Mover");

        WriteFile(
            Path.Combine(
                source,
                "nivel1",
                "nivel2",
                "archivo.txt"),
            "contenido");

        Directory.CreateDirectory(
            Path.Combine(
                source,
                "nivel1",
                "vacia"));

        OrganizationEntrySafety.MoveEntrySafely(
            source,
            target);

        True(
            !Directory.Exists(
                source),
            "La carpeta original debería dejar de existir.");

        True(
            File.Exists(
                Path.Combine(
                    target,
                    "nivel1",
                    "nivel2",
                    "archivo.txt")),
            "El archivo profundo debería conservarse.");

        True(
            Directory.Exists(
                Path.Combine(
                    target,
                    "nivel1",
                    "vacia")),
            "La subcarpeta vacía debería conservarse.");

        return Task.CompletedTask;
    }

    private static async Task<OrganizationAnalysisResult> AnalyzeAsync(
        TestWorkspace workspace)
    {
        var settings =
            AppSettings.CreateDefault();

        settings.SourceFolder =
            workspace.Source;

        settings.DestinationFolder =
            workspace.Destination;

        settings.OrganizeFoldersAsUnits =
            true;

        settings.CreateFolders =
            true;

        settings.ConflictBehavior =
            "Preguntar";

        return await new OrganizationAnalysisService()
            .AnalyzeAsync(
                settings);
    }

    private static OrganizationAnalysisFile SingleFolder(
        OrganizationAnalysisResult result,
        string name)
    {
        var matches =
            result.Files
                .Where(item =>
                    item.IsDirectory &&
                    item.FileName.Equals(
                        name,
                        StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        Equal(
            1,
            matches.Count,
            $"Se esperaba exactamente una carpeta llamada \"{name}\".");

        return matches[0];
    }

    private static void WriteFile(
        string path,
        string content)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                path)!);

        File.WriteAllText(
            path,
            content);
    }

    private static void True(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    private static void Equal<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(
                expected,
                actual))
        {
            throw new InvalidOperationException(
                $"{message} Esperado: {expected}; actual: {actual}.");
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(
            string root,
            string source,
            string destination)
        {
            Root =
                root;
            Source =
                source;
            Destination =
                destination;
        }

        public string Root { get; }
        public string Source { get; }
        public string Destination { get; }

        public static TestWorkspace Create(
            bool destinationInsideSource = false)
        {
            var root =
                Path.Combine(
                    Path.GetTempPath(),
                    "BandaNV_CoreTests",
                    Guid.NewGuid()
                        .ToString(
                            "N"));

            var source =
                Path.Combine(
                    root,
                    "origen");

            var destination =
                destinationInsideSource
                    ? Path.Combine(
                        source,
                        "ORGANIZADO")
                    : Path.Combine(
                        root,
                        "destino");

            Directory.CreateDirectory(
                source);

            Directory.CreateDirectory(
                destination);

            return new TestWorkspace(
                root,
                source,
                destination);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        Root))
                {
                    Directory.Delete(
                        Root,
                        recursive:
                            true);
                }
            }
            catch
            {
            }
        }
    }
}
