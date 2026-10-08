using BandaNV.Core.Models;
using BandaNV.Core.Services;

namespace BandaNV.Core.SmokeTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly List<OrganizationExecutionResult> _generatedExecutionArtifacts = new();

    public static async Task<int> Main()
    {
        Console.OutputEncoding =
            System.Text.Encoding.UTF8;

        Console.WriteLine();
        Console.WriteLine(
            "BandaNV v2.0 - Smoke tests del motor");
        Console.WriteLine();

        await RunAsync(
            "Carpeta profunda se mantiene como unidad y requiere asignación manual",
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

        await RunAsync(
            "Archivo contra archivo se renombra automáticamente sin pisar destino",
            FileFileAutoRenameConflictAsync);

        await RunAsync(
            "Carpeta contra carpeta se renombra automáticamente como unidad",
            FolderFolderAutoRenameConflictAsync);

        await RunAsync(
            "Archivo puede reemplazar de forma segura una carpeta homónima",
            FileReplacesFolderConflictAsync);

        await RunAsync(
            "Carpeta puede reemplazar de forma segura un archivo homónimo",
            FolderReplacesFileConflictAsync);

        await RunAsync(
            "Omitir elemento conserva origen y destino",
            SkipConflictKeepsBothSidesAsync);

        await RunAsync(
            "Preguntar sin resolución no mueve el elemento",
            AskConflictWithoutResolverStopsAsync);

        await RunAsync(
            "Conflicto creado después del análisis se detecta al ejecutar",
            LateConflictIsResolvedAtExecutionAsync);

        await RunAsync(
            "Reemplazar pide decisión cuando Confirmar acciones está activo",
            ReplaceRequiresDecisionWhenConfirmationEnabledAsync);

        await RunAsync(
            "Reemplazar es automático cuando Confirmar acciones está desactivado",
            ReplaceIsAutomaticWhenConfirmationDisabledAsync);

        await RunAsync(
            "Historial desactivado conserva Undo técnico sin mostrarlo",
            HiddenHistoryKeepsTechnicalUndoAsync);

        await RunAsync(
            "Undo funciona aunque el historial visible esté desactivado",
            HiddenHistoryStillSupportsUndoAsync);

        await RunAsync(
            "Historial activado mantiene la ejecución visible",
            VisibleHistoryRemainsVisibleAsync);

        await RunAsync(
            "Reanálisis detecta un archivo agregado externamente",
            ReanalysisDetectsExternalCreateAsync);

        await RunAsync(
            "Reanálisis detecta un renombre externo",
            ReanalysisDetectsExternalRenameAsync);

        await RunAsync(
            "Reanálisis actualiza carpeta tras un borrado interno externo",
            ReanalysisDetectsNestedDeleteAsync);

        await RunAsync(
            "Defaults oficiales arrancan sin rutas y con flujo seguro activo",
            DefaultSettingsMatchProductDefinitionAsync);

        await RunAsync(
            "Contador de categoría incluye archivos de todas las subcarpetas",
            CategoryFileCountIsRecursiveAsync);

        await RunAsync(
            "Categoría eliminada con contenido conserva carpeta sin prefijo",
            DeletedCategoryWithContentBecomesTrackedOrphanAsync);

        await RunAsync(
            "Categoría eliminada vacía se borra cuando la limpieza está activa",
            EmptyDeletedCategoryIsRemovedAsync);

        await RunAsync(
            "Carpeta huérfana vacía se conserva en Off y se limpia al activar",
            OrphanFolderRespectsCleanupToggleAsync);

        await RunAsync(
            "Carpetas protegidas cubren hijos y padres sin bloquear carpetas vecinas",
            ProtectedFolderPathPolicyAsync);

        await RunAsync(
            "Organizar no mueve carpeta que contiene una carpeta protegida",
            OrganizeSkipsProtectedNestedFolderAsync);

        await RunAsync(
            "Organizar no reemplaza contenido de una categoría protegida",
            OrganizeSkipsProtectedDestinationAsync);

        await RunAsync(
            "Buscar no elimina carpetas protegidas y sí puede eliminar vecinas",
            SearchActionsRespectProtectedFoldersAsync);

        await RunAsync(
            "Renombrar desde Organizar respeta las carpetas protegidas",
            SourceActionsRespectProtectedFoldersAsync);

        await RunAsync(
            "Sincronización de categorías no mueve carpetas protegidas",
            CategorySyncRespectsProtectedFoldersAsync);

        await RunAsync(
            "Deshacer no modifica una carpeta protegida",
            UndoRespectsProtectedFoldersAsync);

        Console.WriteLine();
        Console.WriteLine(
            $"Resultado: {_passed} OK · {_failed} error(es)");

        CleanupGeneratedExecutionArtifacts();

        return _failed == 0
            ? 0
            : 1;
    }

    private static Task DefaultSettingsMatchProductDefinitionAsync()
    {
        var settings =
            AppSettings.CreateDefault();

        Equal(
            string.Empty,
            settings.SourceFolder,
            "El origen default debería quedar sin seleccionar.");

        Equal(
            string.Empty,
            settings.DestinationFolder,
            "El destino default debería quedar sin seleccionar.");

        Equal(
            "Inicio",
            settings.StartupPage,
            "La página inicial default debería ser Inicio.");

        Equal(
            "Cerrar BandaNV",
            settings.CloseBehavior,
            "Cerrar debería finalizar BandaNV por defecto.");

        True(
            !settings.StartWithWindows,
            "Iniciar con Windows debería venir desactivado.");

        True(
            settings.AutoUpdate,
            "El aviso de actualización debería venir activado.");

        Equal(
            "Oscuro",
            settings.Theme,
            "El tema default debería ser Oscuro.");

        Equal(
            "#123A34",
            settings.PrimaryColor,
            "El color primario default no coincide.");

        Equal(
            "#4FE0C6",
            settings.SecondaryColor,
            "El color secundario default no coincide.");

        True(
            settings.PreviewBeforeOrganize,
            "La vista previa debería venir activada.");

        True(
            settings.OrganizeFoldersAsUnits,
            "Detectar carpetas completas debería venir activado.");

        True(
            settings.CreateFolders,
            "Crear carpetas faltantes debería venir activado.");

        True(
            settings.DeleteUnusedCategoryFolders,
            "Eliminar carpetas de categorías sin uso debería venir activado.");

        Equal(
            "Preguntar",
            settings.ConflictBehavior,
            "Los conflictos deberían preguntar por defecto.");

        Equal(
            "Preguntar en la vista previa",
            settings.UnknownExtensionBehavior,
            "Las extensiones sin categoría deberían preguntar en la vista previa.");

        True(
            settings.UseRecycleBin,
            "La Papelera debería venir activada.");

        True(
            settings.ConfirmDestructiveActions,
            "Las confirmaciones destructivas deberían venir activadas.");

        Equal(
            0,
            settings.ProtectedFolders.Count,
            "No debería haber carpetas protegidas por defecto.");

        True(
            settings.SaveHistory &&
            settings.SaveOrganizeHistory &&
            settings.SaveSearchHistory,
            "El historial y sus acciones deberían venir activados.");

        Equal(
            "Siempre",
            settings.HistoryRetention,
            "La conservación de historial debería ser Siempre.");

        Equal(
            "All",
            settings.SearchDateFilter,
            "Buscar debería iniciar sin filtro de fecha.");
        Equal(
            "All",
            settings.SearchSizeFilter,
            "Buscar debería iniciar sin filtro de tamaño.");
        Equal(
            0,
            settings.SearchExtensionFilters.Count,
            "Buscar debería iniciar sin filtros de extensión.");
        Equal(
            "DateModified",
            settings.SearchSortField,
            "Buscar debería ordenar por fecha de modificación.");
        Equal(
            "Descending",
            settings.SearchSortDirection,
            "Buscar debería ordenar en forma descendente.");
        Equal(
            "DateModified",
            settings.SearchGroupField,
            "Buscar debería agrupar por fecha de modificación.");

        Equal(
            "All",
            settings.OrganizeDateFilter,
            "Organizar debería iniciar sin filtro de fecha.");
        Equal(
            "All",
            settings.OrganizeSizeFilter,
            "Organizar debería iniciar sin filtro de tamaño.");
        Equal(
            0,
            settings.OrganizeExtensionFilters.Count,
            "Organizar debería iniciar sin filtros de extensión.");
        Equal(
            "DateModified",
            settings.OrganizeSortField,
            "Organizar debería ordenar por fecha de modificación.");
        Equal(
            "Descending",
            settings.OrganizeSortDirection,
            "Organizar debería ordenar en forma descendente.");
        Equal(
            "DateModified",
            settings.OrganizeGroupField,
            "Organizar debería agrupar por fecha de modificación.");

        Equal(
            "All",
            settings.HistoryTypeFilter,
            "Historial debería iniciar sin filtro de tipo.");
        Equal(
            "All",
            settings.HistoryUndoFilter,
            "Historial debería iniciar sin filtro de deshacer.");
        Equal(
            string.Empty,
            settings.HistoryOriginFilter,
            "Historial debería iniciar sin filtro de origen.");
        Equal(
            "Newest",
            settings.HistorySortMode,
            "Historial debería mostrar primero lo más reciente.");

        var expectedCategories =
            new[]
            {
                ("RAR", new[] { ".7z", ".rar", ".zip" }, "#874ADE"),
                ("INSTALADORES", new[] { ".bat", ".exe", ".msi" }, "#59EBA9"),
                ("DOCUMENTOS", new[] { ".docx", ".pdf", ".txt", ".xlsx" }, "#FFFFFF"),
                ("IMAGENES", new[] { ".avif", ".jpeg", ".jpg", ".png", ".webp" }, "#33B2EB"),
                ("GIF", new[] { ".gif" }, "#33B2EB"),
                ("VIDEOS", new[] { ".avi", ".mkv", ".mov", ".mp4" }, "#FF0000"),
                ("AUDIO", new[] { ".aac", ".flac", ".mp3", ".ogg", ".wav" }, "#FF0000"),
                ("EDITABLES", new[] { ".ai", ".eps", ".psd" }, "#FF7C35")
            };

        Equal(
            expectedCategories.Length,
            settings.Categories.Count,
            "La cantidad de categorías default no coincide.");

        for (var index = 0; index < expectedCategories.Length; index++)
        {
            var expected =
                expectedCategories[index];
            var actual =
                settings.Categories[index];

            Equal(
                index + 1,
                actual.Order,
                "El orden default de categorías no coincide.");

            Equal(
                expected.Item1,
                actual.Name,
                "El nombre default de la categoría no coincide.");

            True(
                expected.Item2.SequenceEqual(
                    actual.Extensions,
                    StringComparer.OrdinalIgnoreCase),
                $"Las extensiones default de {actual.Name} no coinciden.");

            Equal(
                expected.Item3,
                actual.ColorHex,
                $"El color default de {actual.Name} no coincide.");
        }

        return Task.CompletedTask;
    }

    private static Task CategoryFileCountIsRecursiveAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        const int order = 1;
        const string categoryName = "VIDEOS";

        var categoryFolder =
            CategoryService.GetFolderPath(
                workspace.Destination,
                order,
                categoryName);

        var nestedFolder =
            Path.Combine(
                categoryFolder,
                "Proyecto",
                "Exportaciones");

        Directory.CreateDirectory(
            nestedFolder);

        WriteFile(
            Path.Combine(
                categoryFolder,
                "raiz.mp4"),
            "raiz");

        WriteFile(
            Path.Combine(
                categoryFolder,
                "Proyecto",
                "clip.mp4"),
            "nivel 1");

        WriteFile(
            Path.Combine(
                nestedFolder,
                "final.mp4"),
            "nivel 2");

        Equal(
            3,
            CategoryService.CountExistingFiles(
                workspace.Destination,
                order,
                categoryName),
            "El contador debería incluir cada archivo individual aunque esté dentro de subcarpetas.");

        return Task.CompletedTask;
    }

    private static async Task DeletedCategoryWithContentBecomesTrackedOrphanAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var settings =
            CreateCategorySyncSettings(
                workspace,
                deleteUnusedFolders:
                    true);

        var previous =
            CreateTwoCategorySet();

        var next =
            new List<CategorySettings>
            {
                new(
                    previous[1].Id,
                    previous[1].Name,
                    previous[1].Extensions,
                    1,
                    previous[1].ColorHex)
            };

        var deletedFolder =
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[0].Order,
                previous[0].Name);

        var survivingFolder =
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[1].Order,
                previous[1].Name);

        Directory.CreateDirectory(
            deletedFolder);
        Directory.CreateDirectory(
            survivingFolder);

        WriteFile(
            Path.Combine(
                deletedFolder,
                "conservar.txt"),
            "dato");

        var result =
            await new CategoryFolderSyncService()
                .SynchronizeAsync(
                    settings,
                    previous,
                    next);

        True(
            result.Success,
            result.ErrorMessage ??
            "La sincronización debería completarse.");

        var orphanFolder =
            Path.Combine(
                workspace.Destination,
                previous[0].Name);

        True(
            Directory.Exists(orphanFolder),
            "La carpeta con contenido debería conservarse sin prefijo numérico.");

        True(
            File.Exists(
                Path.Combine(
                    orphanFolder,
                    "conservar.txt")),
            "El contenido de la categoría eliminada debería conservarse.");

        True(
            !Directory.Exists(deletedFolder),
            "La carpeta numerada eliminada ya no debería quedar activa.");

        True(
            Directory.Exists(
                CategoryService.GetFolderPath(
                    workspace.Destination,
                    1,
                    previous[1].Name)),
            "La categoría restante debería ocupar su nuevo orden.");

        True(
            settings.OrphanedCategoryFolders.Any(path =>
                Path.GetFullPath(path).Equals(
                    Path.GetFullPath(orphanFolder),
                    StringComparison.OrdinalIgnoreCase)),
            "La carpeta conservada debería quedar registrada como huérfana segura.");
    }

    private static async Task EmptyDeletedCategoryIsRemovedAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var settings =
            CreateCategorySyncSettings(
                workspace,
                deleteUnusedFolders:
                    true);

        var previous =
            CreateTwoCategorySet();

        var next =
            new List<CategorySettings>
            {
                new(
                    previous[1].Id,
                    previous[1].Name,
                    previous[1].Extensions,
                    1,
                    previous[1].ColorHex)
            };

        var deletedFolder =
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[0].Order,
                previous[0].Name);

        Directory.CreateDirectory(
            deletedFolder);
        Directory.CreateDirectory(
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[1].Order,
                previous[1].Name));

        var result =
            await new CategoryFolderSyncService()
                .SynchronizeAsync(
                    settings,
                    previous,
                    next);

        True(
            result.Success,
            result.ErrorMessage ??
            "La sincronización debería completarse.");

        True(
            !Directory.Exists(deletedFolder),
            "La carpeta numerada vacía debería eliminarse.");

        True(
            !Directory.Exists(
                Path.Combine(
                    workspace.Destination,
                    previous[0].Name)),
            "No debería quedar una carpeta huérfana vacía.");

        True(
            settings.OrphanedCategoryFolders.Count == 0,
            "No debería registrarse una carpeta que ya fue eliminada.");
    }

    private static async Task OrphanFolderRespectsCleanupToggleAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var settings =
            CreateCategorySyncSettings(
                workspace,
                deleteUnusedFolders:
                    false);

        var previous =
            CreateTwoCategorySet();

        var next =
            new List<CategorySettings>
            {
                new(
                    previous[1].Id,
                    previous[1].Name,
                    previous[1].Extensions,
                    1,
                    previous[1].ColorHex)
            };

        Directory.CreateDirectory(
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[0].Order,
                previous[0].Name));

        Directory.CreateDirectory(
            CategoryService.GetFolderPath(
                workspace.Destination,
                previous[1].Order,
                previous[1].Name));

        var service =
            new CategoryFolderSyncService();

        var result =
            await service.SynchronizeAsync(
                settings,
                previous,
                next);

        True(
            result.Success,
            result.ErrorMessage ??
            "La sincronización debería completarse.");

        var orphanFolder =
            Path.Combine(
                workspace.Destination,
                previous[0].Name);

        True(
            Directory.Exists(orphanFolder),
            "Con la limpieza desactivada, la carpeta vacía debería conservarse.");

        True(
            settings.OrphanedCategoryFolders.Count == 1,
            "La carpeta conservada debería quedar registrada.");

        settings.DeleteUnusedCategoryFolders =
            true;

        var deleted =
            await service.CleanupUnusedCategoryFoldersAsync(
                settings);

        Equal(
            1,
            deleted,
            "Al activar la limpieza debería eliminarse la carpeta huérfana vacía.");

        True(
            !Directory.Exists(orphanFolder),
            "La carpeta huérfana ya no debería existir.");

        Equal(
            0,
            settings.OrphanedCategoryFolders.Count,
            "El registro huérfano debería limpiarse junto con la carpeta.");
    }

    private static AppSettings CreateCategorySyncSettings(
        TestWorkspace workspace,
        bool deleteUnusedFolders)
    {
        var settings =
            AppSettings.CreateDefault();

        settings.SourceFolder =
            workspace.Source;
        settings.DestinationFolder =
            workspace.Destination;
        settings.CreateFolders =
            true;
        settings.DeleteUnusedCategoryFolders =
            deleteUnusedFolders;

        return settings;
    }

    private static List<CategorySettings> CreateTwoCategorySet() =>
    [
        new(
            "test-a",
            "AUDIO",
            [".mp3"],
            1,
            "#4FE0C6"),
        new(
            "test-b",
            "VIDEOS",
            [".mp4"],
            2,
            "#4FE0C6")
    ];

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
            1,
            result.Files.Count,
            "Las subcarpetas no deberían convertirse en unidades independientes.");

        True(
            !folder.IsClassified,
            "Una carpeta completa siempre debería requerir asignación manual.");

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

    private static async Task FileFileAutoRenameConflictAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "duplicado.txt");

        WriteFile(
            sourcePath,
            "nuevo");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "duplicado.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "existente");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Renombrar automáticamente");

        var moved =
            result.Record.Items.Single();

        var renamedTarget =
            Path.Combine(
                Path.GetDirectoryName(
                    desiredTarget)!,
                "duplicado (2).txt");

        Equal(
            OrganizationExecutionItemStatus.Moved,
            moved.Status,
            "El archivo debería moverse con nombre alternativo.");

        Equal(
            renamedTarget,
            moved.FinalPath,
            "El destino final debería usar el sufijo (2).");

        Equal(
            "existente",
            File.ReadAllText(
                desiredTarget),
            "El archivo existente no debería modificarse.");

        Equal(
            "nuevo",
            File.ReadAllText(
                renamedTarget),
            "El archivo nuevo debería quedar en la ruta renombrada.");

        True(
            !File.Exists(
                sourcePath),
            "El archivo de origen debería haberse movido.");
    }

    private static async Task FolderFolderAutoRenameConflictAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourceFolder =
            Path.Combine(
                workspace.Source,
                "Fotos");

        WriteFile(
            Path.Combine(
                sourceFolder,
                "foto.jpg"),
            "nueva");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            AssignFolderToImagesForTest(
                workspace,
                SingleFolder(
                    analysis,
                    "Fotos"));

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            Path.Combine(
                desiredTarget,
                "existente.jpg"),
            "existente");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Renombrar automáticamente");

        var moved =
            result.Record.Items.Single();

        var renamedTarget =
            Path.Combine(
                Path.GetDirectoryName(
                    desiredTarget)!,
                "Fotos (2)");

        Equal(
            OrganizationExecutionItemStatus.Moved,
            moved.Status,
            "La carpeta debería moverse con nombre alternativo.");

        Equal(
            renamedTarget,
            moved.FinalPath,
            "La carpeta debería usar el sufijo (2).");

        True(
            File.Exists(
                Path.Combine(
                    desiredTarget,
                    "existente.jpg")),
            "La carpeta existente debería quedar intacta.");

        True(
            File.Exists(
                Path.Combine(
                    renamedTarget,
                    "foto.jpg")),
            "La carpeta nueva debería conservar su contenido.");

        True(
            !Directory.Exists(
                sourceFolder),
            "La carpeta madre debería haberse movido completa.");
    }

    private static async Task FileReplacesFolderConflictAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "choque.txt");

        WriteFile(
            sourcePath,
            "archivo nuevo");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "choque.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        Directory.CreateDirectory(
            desiredTarget);

        WriteFile(
            Path.Combine(
                desiredTarget,
                "viejo.txt"),
            "carpeta vieja");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Reemplazar");

        var moved =
            result.Record.Items.Single();

        Equal(
            OrganizationExecutionItemStatus.Moved,
            moved.Status,
            "El archivo debería reemplazar a la carpeta existente.");

        True(
            File.Exists(
                desiredTarget),
            "El destino final debería ser un archivo.");

        True(
            !Directory.Exists(
                desiredTarget),
            "La carpeta homónima debería dejar de existir.");

        Equal(
            "archivo nuevo",
            File.ReadAllText(
                desiredTarget),
            "El destino debería contener el archivo nuevo.");

        Equal(
            OrganizationAnalysisItemKind.Folder,
            moved.ReplacedKind,
            "El registro debería recordar que se reemplazó una carpeta.");
    }

    private static async Task FolderReplacesFileConflictAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourceFolder =
            Path.Combine(
                workspace.Source,
                "Album");

        WriteFile(
            Path.Combine(
                sourceFolder,
                "foto.jpg"),
            "imagen");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            AssignFolderToImagesForTest(
                workspace,
                SingleFolder(
                    analysis,
                    "Album"));

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "archivo viejo");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Reemplazar");

        var moved =
            result.Record.Items.Single();

        Equal(
            OrganizationExecutionItemStatus.Moved,
            moved.Status,
            "La carpeta debería reemplazar al archivo existente.");

        True(
            Directory.Exists(
                desiredTarget),
            "El destino final debería ser una carpeta.");

        True(
            !File.Exists(
                desiredTarget),
            "El archivo homónimo debería dejar de existir.");

        True(
            File.Exists(
                Path.Combine(
                    desiredTarget,
                    "foto.jpg")),
            "La carpeta reemplazante debería conservar su contenido.");

        Equal(
            OrganizationAnalysisItemKind.File,
            moved.ReplacedKind,
            "El registro debería recordar que se reemplazó un archivo.");
    }

    private static async Task SkipConflictKeepsBothSidesAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "omitir.txt");

        WriteFile(
            sourcePath,
            "origen");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "omitir.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "destino");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Omitir elemento");

        var skipped =
            result.Record.Items.Single();

        Equal(
            OrganizationExecutionItemStatus.SkippedConflict,
            skipped.Status,
            "El conflicto debería quedar omitido.");

        True(
            File.Exists(
                sourcePath),
            "El archivo de origen debería permanecer.");

        Equal(
            "destino",
            File.ReadAllText(
                desiredTarget),
            "El elemento existente en destino debería permanecer intacto.");
    }

    private static async Task AskConflictWithoutResolverStopsAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "preguntar.txt");

        WriteFile(
            sourcePath,
            "origen");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "preguntar.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "destino");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Preguntar");

        var pending =
            result.Record.Items.Single();

        Equal(
            OrganizationExecutionItemStatus.ConflictNeedsDecision,
            pending.Status,
            "Sin resolver el popup, el motor no debería mover nada.");

        True(
            File.Exists(
                sourcePath),
            "El archivo de origen debería permanecer.");

        Equal(
            "destino",
            File.ReadAllText(
                desiredTarget),
            "El destino debería permanecer intacto.");
    }

    private static async Task LateConflictIsResolvedAtExecutionAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "tardio.txt");

        WriteFile(
            sourcePath,
            "origen");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "tardio.txt");

        True(
            !item.HasDestinationConflict,
            "Durante el análisis todavía no debería existir conflicto.");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        Directory.CreateDirectory(
            desiredTarget);

        var resolver =
            new CapturingConflictResolver(
                OrganizationConflictAction.Rename);

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Preguntar",
                resolver);

        var moved =
            result.Record.Items.Single();

        Equal(
            1,
            resolver.CallCount,
            "El resolver debería invocarse al detectar el conflicto tardío.");

        True(
            resolver.LastConflict is not null,
            "El resolver debería recibir información del conflicto.");

        Equal(
            false,
            resolver.LastConflict!.IsDirectory,
            "El origen debería identificarse como archivo.");

        Equal(
            true,
            resolver.LastConflict.DestinationIsDirectory,
            "El destino existente debería identificarse como carpeta.");

        Equal(
            OrganizationExecutionItemStatus.Moved,
            moved.Status,
            "El archivo debería moverse después de resolver el conflicto.");

        True(
            Directory.Exists(
                desiredTarget),
            "La carpeta que apareció después del análisis debería quedar intacta.");

        True(
            File.Exists(
                Path.Combine(
                    Path.GetDirectoryName(
                        desiredTarget)!,
                    "tardio (2).txt")),
            "El archivo debería usar un nombre alternativo.");
    }

    private static async Task ReplaceRequiresDecisionWhenConfirmationEnabledAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "confirmar.txt");

        WriteFile(
            sourcePath,
            "nuevo");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "confirmar.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "existente");

        var resolver =
            new CapturingConflictResolver(
                OrganizationConflictAction.Skip);

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Reemplazar",
                resolver,
                confirmDestructiveActions:
                    true);

        Equal(
            1,
            resolver.CallCount,
            "Con confirmaciones activas, Reemplazar debería pedir una decisión.");

        Equal(
            OrganizationExecutionItemStatus.SkippedConflict,
            result.Record.Items.Single().Status,
            "Elegir omitir en la confirmación debería conservar ambos elementos.");

        True(
            File.Exists(
                sourcePath),
            "El origen debería seguir existiendo al omitir.");

        Equal(
            "existente",
            File.ReadAllText(
                desiredTarget),
            "El destino existente debería permanecer intacto.");
    }

    private static async Task ReplaceIsAutomaticWhenConfirmationDisabledAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "automatico.txt");

        WriteFile(
            sourcePath,
            "nuevo");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "automatico.txt");

        var desiredTarget =
            GetDesiredTarget(
                workspace,
                item);

        WriteFile(
            desiredTarget,
            "existente");

        var resolver =
            new CapturingConflictResolver(
                OrganizationConflictAction.Skip);

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Reemplazar",
                resolver,
                confirmDestructiveActions:
                    false);

        Equal(
            0,
            resolver.CallCount,
            "Con confirmaciones desactivadas, Reemplazar no debería abrir una decisión.");

        Equal(
            OrganizationExecutionItemStatus.Moved,
            result.Record.Items.Single().Status,
            "El elemento debería reemplazarse automáticamente.");

        Equal(
            "nuevo",
            File.ReadAllText(
                desiredTarget),
            "El destino debería contener el elemento nuevo.");
    }

    private static async Task HiddenHistoryKeepsTechnicalUndoAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        WriteFile(
            Path.Combine(
                workspace.Source,
                "oculto.txt"),
            "contenido");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "oculto.txt");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Preguntar",
                saveHistory:
                    false);

        try
        {
            Equal(
                false,
                result.Record.ShowInHistory,
                "La ejecución técnica no debería marcarse como historial visible.");

            True(
                !string.IsNullOrWhiteSpace(
                    result.HistoryPath) &&
                File.Exists(
                    result.HistoryPath),
                "El registro técnico debería conservarse para Undo.");

            var history =
                new HistoryService();

            var visible =
                await history.LoadAsync();

            True(
                visible.All(record =>
                    !record.ExecutionId.Equals(
                        result.Record.ExecutionId,
                        StringComparison.OrdinalIgnoreCase)),
                "El registro técnico no debería aparecer en el historial visible.");

            var technical =
                await history.LoadTechnicalUndoRecordsAsync();

            True(
                technical.Any(record =>
                    record.ExecutionId.Equals(
                        result.Record.ExecutionId,
                        StringComparison.OrdinalIgnoreCase)),
                "El registro oculto debería estar disponible para Undo técnico.");
        }
        finally
        {
            DeleteExecutionArtifacts(
                result);
        }
    }

    private static async Task HiddenHistoryStillSupportsUndoAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var sourcePath =
            Path.Combine(
                workspace.Source,
                "deshacer.txt");

        WriteFile(
            sourcePath,
            "contenido");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "deshacer.txt");

        var organizeResult =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Preguntar",
                saveHistory:
                    false);

        OrganizationExecutionResult? undoResult =
            null;

        try
        {
            True(
                !File.Exists(
                    sourcePath),
                "Después de organizar, el archivo ya no debería estar en origen.");

            var settings =
                CreateSettings(
                    workspace,
                    "Preguntar");

            settings.SaveHistory =
                false;

            undoResult =
                await new UndoService()
                    .UndoAsync(
                        settings,
                        organizeResult.Record);

            Equal(
                false,
                undoResult.Record.ShowInHistory,
                "El Undo técnico tampoco debería agregarse al historial visible.");

            True(
                File.Exists(
                    sourcePath),
                "Undo debería restaurar el archivo al origen aunque Historial esté desactivado.");

            Equal(
                "contenido",
                File.ReadAllText(
                    sourcePath),
                "El archivo restaurado debería conservar su contenido.");

            True(
                !OrganizationEntrySafety.Exists(
                    organizeResult.Record.Items.Single().FinalPath!),
                "La ubicación organizada debería quedar libre después del Undo.");
        }
        finally
        {
            DeleteExecutionArtifacts(
                organizeResult);

            if (undoResult is not null)
            {
                DeleteExecutionArtifacts(
                    undoResult);
            }
        }
    }

    private static async Task VisibleHistoryRemainsVisibleAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        WriteFile(
            Path.Combine(
                workspace.Source,
                "visible.txt"),
            "contenido");

        var analysis =
            await AnalyzeAsync(
                workspace);

        var item =
            SingleFile(
                analysis,
                "visible.txt");

        var result =
            await ExecuteSingleAsync(
                workspace,
                item,
                "Preguntar",
                saveHistory:
                    true);

        try
        {
            Equal(
                true,
                result.Record.ShowInHistory,
                "La ejecución debería marcarse como historial visible.");

            var history =
                new HistoryService();

            var visible =
                await history.LoadAsync();

            True(
                visible.Any(record =>
                    record.ExecutionId.Equals(
                        result.Record.ExecutionId,
                        StringComparison.OrdinalIgnoreCase)),
                "La ejecución debería aparecer en el historial visible.");

            var technical =
                await history.LoadTechnicalUndoRecordsAsync();

            True(
                technical.All(record =>
                    !record.ExecutionId.Equals(
                        result.Record.ExecutionId,
                        StringComparison.OrdinalIgnoreCase)),
                "Una ejecución visible no debería duplicarse como Undo técnico.");
        }
        finally
        {
            DeleteExecutionArtifacts(
                result);
        }
    }

    private static async Task ReanalysisDetectsExternalCreateAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var initial =
            await AnalyzeAsync(
                workspace);

        Equal(
            0,
            initial.Files.Count,
            "El origen debería empezar vacío.");

        WriteFile(
            Path.Combine(
                workspace.Source,
                "nuevo.txt"),
            "externo");

        var refreshed =
            await AnalyzeAsync(
                workspace);

        var file =
            SingleFile(
                refreshed,
                "nuevo.txt");

        Equal(
            "DOCUMENTOS",
            file.CategoryName,
            "El archivo agregado debería clasificarse al reanalizar.");
    }

    private static async Task ReanalysisDetectsExternalRenameAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var oldPath =
            Path.Combine(
                workspace.Source,
                "antes.txt");

        var newPath =
            Path.Combine(
                workspace.Source,
                "despues.txt");

        WriteFile(
            oldPath,
            "contenido");

        var initial =
            await AnalyzeAsync(
                workspace);

        SingleFile(
            initial,
            "antes.txt");

        File.Move(
            oldPath,
            newPath);

        var refreshed =
            await AnalyzeAsync(
                workspace);

        True(
            refreshed.Files.All(item =>
                !item.FileName.Equals(
                    "antes.txt",
                    StringComparison.OrdinalIgnoreCase)),
            "El nombre anterior no debería seguir en el análisis.");

        SingleFile(
            refreshed,
            "despues.txt");
    }

    private static async Task ReanalysisDetectsNestedDeleteAsync()
    {
        using var workspace =
            TestWorkspace.Create();

        var mother =
            Path.Combine(
                workspace.Source,
                "Cambiante");

        var firstFile =
            Path.Combine(
                mother,
                "uno.jpg");

        var secondFile =
            Path.Combine(
                mother,
                "sub",
                "dos.jpg");

        WriteFile(
            firstFile,
            "uno");

        WriteFile(
            secondFile,
            "dos");

        var initial =
            await AnalyzeAsync(
                workspace);

        var before =
            SingleFolder(
                initial,
                "Cambiante");

        Equal(
            2,
            before.ContainedFileCount,
            "La carpeta debería empezar con dos archivos.");

        File.Delete(
            secondFile);

        var refreshed =
            await AnalyzeAsync(
                workspace);

        var after =
            SingleFolder(
                refreshed,
                "Cambiante");

        Equal(
            1,
            after.ContainedFileCount,
            "El reanálisis debería reflejar el archivo eliminado.");

        True(
            !string.Equals(
                before.ContentFingerprint,
                after.ContentFingerprint,
                StringComparison.OrdinalIgnoreCase),
            "El fingerprint debería cambiar después del borrado externo.");
    }

    private static async Task<OrganizationExecutionResult> ExecuteSingleAsync(
        TestWorkspace workspace,
        OrganizationAnalysisFile item,
        string conflictBehavior,
        IOrganizationConflictResolver? resolver = null,
        bool confirmDestructiveActions = false,
        bool saveHistory = false)
    {
        var settings =
            CreateSettings(
                workspace,
                conflictBehavior);

        settings.SaveHistory =
            saveHistory;

        settings.ConfirmDestructiveActions =
            confirmDestructiveActions;

        var result =
            await new OrganizationExecutionService()
                .ExecuteAsync(
                    settings,
                    [
                        ToExecutionRequest(
                            item)
                    ],
                    conflictResolver:
                        resolver);

        _generatedExecutionArtifacts.Add(
            result);

        return result;
    }

    private static void CleanupGeneratedExecutionArtifacts()
    {
        foreach (var result in
                 _generatedExecutionArtifacts)
        {
            DeleteExecutionArtifacts(
                result);
        }

        _generatedExecutionArtifacts.Clear();
    }

    private static void DeleteExecutionArtifacts(
        OrganizationExecutionResult result)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(
                    result.HistoryPath))
            {
                var historyDirectory =
                    Path.GetDirectoryName(
                        result.HistoryPath);

                if (File.Exists(
                        result.HistoryPath))
                {
                    File.Delete(
                        result.HistoryPath);
                }

                if (!string.IsNullOrWhiteSpace(
                        historyDirectory))
                {
                    var backupDirectory =
                        Path.Combine(
                            historyDirectory,
                            "replaced",
                            result.Record.ExecutionId);

                    if (Directory.Exists(
                            backupDirectory))
                    {
                        Directory.Delete(
                            backupDirectory,
                            recursive:
                                true);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    result.LogPath) &&
                File.Exists(
                    result.LogPath))
            {
                File.Delete(
                    result.LogPath);
            }
        }
        catch
        {
        }
    }

    private static Task ProtectedFolderPathPolicyAsync()
    {
        using var workspace = TestWorkspace.Create();

        var protectedFolder =
            Path.Combine(workspace.Source, "Privado");

        var settings = CreateSettings(workspace, "Preguntar");
        settings.ProtectedFolders = [protectedFolder];

        True(
            ProtectedFolderService.IsProtected(settings, protectedFolder),
            "La carpeta elegida debe quedar protegida.");

        True(
            ProtectedFolderService.IsProtected(
                settings,
                Path.Combine(protectedFolder, "nivel", "archivo.txt")),
            "Los descendientes deben quedar protegidos.");

        True(
            ProtectedFolderService.IsProtected(settings, workspace.Source),
            "Mover una carpeta madre que contenga una protegida debe bloquearse.");

        True(
            !ProtectedFolderService.IsProtected(
                settings,
                Path.Combine(workspace.Source, "Privado2", "vecino.txt")),
            "La comparación no debe confundir prefijos de carpeta.");

        True(
            !ProtectedFolderService.IsProtected(settings, workspace.Destination),
            "Un destino independiente no debe bloquearse.");

        var normalized = ProtectedFolderService.NormalizePaths(
        [
            protectedFolder,
            Path.Combine(protectedFolder, "nivel"),
            protectedFolder + Path.DirectorySeparatorChar
        ]);

        Equal(
            1,
            normalized.Count,
            "Las protecciones superpuestas deberían unificarse.");

        return Task.CompletedTask;
    }

    private static async Task OrganizeSkipsProtectedNestedFolderAsync()
    {
        using var workspace = TestWorkspace.Create();
        var mother = Path.Combine(workspace.Source, "Madre");
        var protectedChild = Path.Combine(mother, "Reservado");
        WriteFile(Path.Combine(protectedChild, "foto.jpg"), "contenido");

        var settings = CreateSettings(
            workspace,
            "Renombrar automáticamente");
        settings.ProtectedFolders = [protectedChild];

        var analysis = await new OrganizationAnalysisService()
            .AnalyzeAsync(settings);

        var folder = AssignFolderToImagesForTest(
            workspace,
            SingleFolder(analysis, "Madre"));

        var result = await new OrganizationExecutionService()
            .ExecuteAsync(settings, [ToExecutionRequest(folder)]);

        _generatedExecutionArtifacts.Add(result);

        Equal(
            OrganizationExecutionItemStatus.Error,
            result.Record.Items.Single().Status,
            "Organizar debe omitir cualquier carpeta que contiene una protegida.");

        True(Directory.Exists(mother),
            "La carpeta madre debe permanecer en origen.");

        True(
            !Directory.Exists(GetDesiredTarget(workspace, folder)),
            "La carpeta protegida no debe terminar movida.");
    }

    private static async Task OrganizeSkipsProtectedDestinationAsync()
    {
        using var workspace = TestWorkspace.Create();
        var source = Path.Combine(workspace.Source, "archivo.txt");
        WriteFile(source, "nuevo");

        var settings = CreateSettings(workspace, "Reemplazar");
        settings.ConfirmDestructiveActions = false;

        var analysis = await new OrganizationAnalysisService()
            .AnalyzeAsync(settings);
        var item = SingleFile(analysis, "archivo.txt");
        var target = GetDesiredTarget(workspace, item);

        WriteFile(target, "anterior");
        settings.ProtectedFolders = [Path.GetDirectoryName(target)!];

        var result = await new OrganizationExecutionService()
            .ExecuteAsync(settings, [ToExecutionRequest(item)]);

        _generatedExecutionArtifacts.Add(result);

        Equal(
            OrganizationExecutionItemStatus.Error,
            result.Record.Items.Single().Status,
            "No se debe reemplazar un archivo dentro de una carpeta protegida.");

        True(File.Exists(source),
            "El archivo original debe seguir en origen.");

        Equal("anterior", File.ReadAllText(target),
            "El destino protegido debe conservar el contenido anterior.");
    }

    private static async Task SearchActionsRespectProtectedFoldersAsync()
    {
        using var workspace = TestWorkspace.Create();

        var protectedFolder = Path.Combine(
            workspace.Destination,
            "Privado");

        var protectedFile = Path.Combine(
            protectedFolder,
            "importante.txt");

        var neighboringFile = Path.Combine(
            workspace.Destination,
            "Vecino",
            "temporal.txt");

        WriteFile(protectedFile, "importante");
        WriteFile(neighboringFile, "temporal");

        var settings = CreateSettings(workspace, "Preguntar");
        settings.UseRecycleBin = false;
        settings.ProtectedFolders = [protectedFolder];

        var actions = new SearchFileActionService();

        var blocked = await actions.DeleteAsync(
            settings,
            [protectedFolder]);

        Equal(
            SearchFileActionStatus.Error,
            blocked.Items.Single().Status,
            "Buscar no debe eliminar una carpeta protegida.");

        True(File.Exists(protectedFile),
            "El contenido protegido debe permanecer intacto.");

        var allowed = await actions.DeleteAsync(
            settings,
            [neighboringFile]);

        Equal(
            SearchFileActionStatus.Completed,
            allowed.Items.Single().Status,
            "Las rutas vecinas deben poder modificarse.");

        True(!File.Exists(neighboringFile),
            "La eliminación de la ruta vecina debería completarse.");
    }

    private static async Task SourceActionsRespectProtectedFoldersAsync()
    {
        using var workspace = TestWorkspace.Create();

        var root = Path.Combine(workspace.Source, "Proyecto");
        var child = Path.Combine(root, "Reservado");

        WriteFile(Path.Combine(child, "documento.txt"), "contenido");

        var settings = CreateSettings(workspace, "Preguntar");
        settings.ProtectedFolders = [child];

        var threw = false;

        try
        {
            await new OrganizationSourceActionService()
                .RenameAsync(settings, root, "NuevoProyecto");
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        True(threw,
            "Renombrar la carpeta superior a una protegida debe bloquearse.");

        True(Directory.Exists(root),
            "La carpeta madre no debe cambiar de nombre.");
    }

    private static async Task CategorySyncRespectsProtectedFoldersAsync()
    {
        using var workspace = TestWorkspace.Create();

        var previous = CreateTwoCategorySet();
        var next = new List<CategorySettings>
        {
            new(
                previous[0].Id,
                previous[0].Name,
                previous[0].Extensions,
                2,
                previous[0].ColorHex),
            new(
                previous[1].Id,
                previous[1].Name,
                previous[1].Extensions,
                1,
                previous[1].ColorHex)
        };

        var existingFolder = CategoryService.GetFolderPath(
            workspace.Destination,
            previous[0].Order,
            previous[0].Name);

        WriteFile(Path.Combine(existingFolder, "conservar.txt"), "seguro");

        var settings = CreateCategorySyncSettings(
            workspace,
            deleteUnusedFolders: true);

        settings.ProtectedFolders = [existingFolder];

        var result = await new CategoryFolderSyncService()
            .SynchronizeAsync(settings, previous, next);

        True(!result.Success,
            "La sincronización no puede renombrar una categoría protegida.");

        True(File.Exists(Path.Combine(existingFolder, "conservar.txt")),
            "El contenido de la carpeta protegida debe conservarse.");
    }

    private static async Task UndoRespectsProtectedFoldersAsync()
    {
        using var workspace = TestWorkspace.Create();

        WriteFile(
            Path.Combine(workspace.Source, "volver.txt"),
            "archivo");

        var settings = CreateSettings(workspace, "Preguntar");
        var analyzed = await new OrganizationAnalysisService()
            .AnalyzeAsync(settings);
        var item = SingleFile(analyzed, "volver.txt");

        var execution = await new OrganizationExecutionService()
            .ExecuteAsync(settings, [ToExecutionRequest(item)]);

        _generatedExecutionArtifacts.Add(execution);

        Equal(
            OrganizationExecutionItemStatus.Moved,
            execution.Record.Items.Single().Status,
            "La organización inicial debe completarse.");

        var movedPath = execution.Record.Items.Single().FinalPath!;

        settings.ProtectedFolders =
        [
            Path.GetDirectoryName(movedPath)!
        ];

        var undo = await new UndoService()
            .UndoAsync(settings, execution.Record);

        _generatedExecutionArtifacts.Add(undo);

        Equal(
            OrganizationExecutionItemStatus.Error,
            undo.Record.Items.Single().Status,
            "Undo debe bloquear un movimiento desde la carpeta protegida.");

        True(File.Exists(movedPath),
            "El archivo debe conservarse en el destino protegido.");

        True(!File.Exists(item.FullPath),
            "Undo no debe restaurar el archivo en origen.");
    }

    private static OrganizationExecutionRequestItem ToExecutionRequest(
        OrganizationAnalysisFile item) =>
        new(
            item.FullPath,
            item.FileName,
            item.SizeBytes,
            item.ModifiedUtcTicks,
            item.CategoryId,
            item.CategoryName,
            item.CategoryOrder,
            item.Kind,
            item.ContainedFileCount,
            item.ContentFingerprint);

    private static OrganizationAnalysisFile AssignFolderToImagesForTest(
        TestWorkspace workspace,
        OrganizationAnalysisFile folder)
    {
        True(
            folder.IsDirectory,
            "La asignación manual de prueba requiere una carpeta.");

        True(
            !folder.IsClassified,
            "La carpeta debe llegar sin clasificar: el motor no debe autoclasificar carpetas completas.");

        var imagesCategory =
            AppSettings.CreateDefault().Categories
                .Single(category =>
                    category.Extensions.Contains(
                        ".jpg",
                        StringComparer.OrdinalIgnoreCase));

        var destinationPath =
            Path.Combine(
                CategoryService.GetFolderPath(
                    workspace.Destination,
                    imagesCategory.Order,
                    imagesCategory.Name),
                folder.FileName);

        // Reproduce la asignación manual en la vista previa sin
        // modificar el contrato del analizador ni el motor.
        return folder with
        {
            CategoryId = imagesCategory.Id,
            CategoryName = imagesCategory.Name,
            CategoryOrder = imagesCategory.Order,
            DestinationPath = destinationPath,
            HasDestinationConflict = false
        };
    }

    private static string GetDesiredTarget(
        TestWorkspace workspace,
        OrganizationAnalysisFile item)
    {
        if (!item.CategoryOrder.HasValue ||
            string.IsNullOrWhiteSpace(
                item.CategoryName))
        {
            throw new InvalidOperationException(
                "El elemento de prueba debería estar clasificado.");
        }

        return Path.Combine(
            CategoryService.GetFolderPath(
                workspace.Destination,
                item.CategoryOrder.Value,
                item.CategoryName),
            item.FileName);
    }

    private static AppSettings CreateSettings(
        TestWorkspace workspace,
        string conflictBehavior)
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
            conflictBehavior;

        return settings;
    }

    private static async Task<OrganizationAnalysisResult> AnalyzeAsync(
        TestWorkspace workspace)
    {
        var settings =
            CreateSettings(
                workspace,
                "Preguntar");

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

    private static OrganizationAnalysisFile SingleFile(
        OrganizationAnalysisResult result,
        string name)
    {
        var matches =
            result.Files
                .Where(item =>
                    !item.IsDirectory &&
                    item.FileName.Equals(
                        name,
                        StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        Equal(
            1,
            matches.Count,
            $"Se esperaba exactamente un archivo llamado \"{name}\".");

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

    private sealed class CapturingConflictResolver(
        OrganizationConflictAction action) :
        IOrganizationConflictResolver
    {
        public int CallCount { get; private set; }
        public OrganizationConflictInfo? LastConflict { get; private set; }

        public Task<OrganizationConflictResolution> ResolveAsync(
            OrganizationConflictInfo conflict,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastConflict =
                conflict;

            return Task.FromResult(
                new OrganizationConflictResolution(
                    action,
                    ApplyToRemaining:
                        false));
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
