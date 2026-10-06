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
            "Reanálisis detecta un archivo agregado externamente",
            ReanalysisDetectsExternalCreateAsync);

        await RunAsync(
            "Reanálisis detecta un renombre externo",
            ReanalysisDetectsExternalRenameAsync);

        await RunAsync(
            "Reanálisis actualiza carpeta tras un borrado interno externo",
            ReanalysisDetectsNestedDeleteAsync);

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
            1,
            result.Files.Count,
            "Las subcarpetas no deberían convertirse en unidades independientes.");

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
            SingleFolder(
                analysis,
                "Fotos");

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
            SingleFolder(
                analysis,
                "Album");

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
            "DOCUMENTS",
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
        bool confirmDestructiveActions = false)
    {
        var settings =
            CreateSettings(
                workspace,
                conflictBehavior);

        settings.SaveHistory =
            false;

        settings.UndoEnabled =
            false;

        settings.ConfirmDestructiveActions =
            confirmDestructiveActions;

        return await new OrganizationExecutionService()
            .ExecuteAsync(
                settings,
                [
                    ToExecutionRequest(
                        item)
                ],
                conflictResolver:
                    resolver);
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
