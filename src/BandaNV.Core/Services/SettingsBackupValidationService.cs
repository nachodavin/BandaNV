using System.Text.Json;

namespace BandaNV.Core.Services;

/// <summary>
/// Valida todo el contenido relevante de un archivo .bandanv antes de
/// deserializarlo o realizar cualquier cambio en la configuración o en disco.
/// </summary>
public static class SettingsBackupValidationService
{
    public const string Format = "BandaNV.SettingsBackup.v1";

    public static bool TryValidate(string? json, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json) || json.Length > 4_000_000)
        {
            error = "El archivo de configuración está vacío o excede el tamaño admitido.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 32 });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "El backup no contiene una configuración válida.";
                return false;
            }

            if (!TryGetString(root, "Format", out var format) ||
                !string.Equals(format, Format, StringComparison.Ordinal))
            {
                error = "El archivo no corresponde a una versión compatible de BandaNV.";
                return false;
            }

            foreach (var key in new[]
                     {
                         "SourceFolder", "DestinationFolder",
                         "StartupPage", "CloseBehavior", "Theme",
                         "PrimaryColor", "SecondaryColor", "AccentColor",
                         "ConflictBehavior", "UnknownExtensionBehavior",
                         "HistoryRetention", "SearchDateFilter",
                         "SearchSizeFilter", "SearchSortField",
                         "SearchSortDirection", "SearchGroupField",
                         "OrganizeDateFilter", "OrganizeSizeFilter",
                         "OrganizeSortField", "OrganizeSortDirection",
                         "OrganizeGroupField", "HistoryTypeFilter",
                         "HistoryUndoFilter", "HistoryOriginFilter",
                         "HistorySortMode"
                     })
            {
                if (root.TryGetProperty(key, out var value) &&
                    (value.ValueKind != JsonValueKind.String ||
                     value.GetString()!.Length > 2048))
                {
                    error = $"El campo {key} es inválido.";
                    return false;
                }
            }

            foreach (var key in new[]
                     {
                         "StartWithWindows", "AutoUpdate",
                         "PreviewBeforeOrganize", "OrganizeFoldersAsUnits",
                         "CreateFolders", "DeleteUnusedCategoryFolders",
                         "RecycleBin", "ConfirmDestructive",
                         "SaveHistory", "SaveOrganizeHistory",
                         "SaveSearchHistory"
                     })
            {
                if (root.TryGetProperty(key, out var value) &&
                    value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    error = $"El campo {key} es inválido.";
                    return false;
                }
            }

            if (!TryGetString(root, "SourceFolder", out var source) ||
                !TryGetString(root, "DestinationFolder", out var destination) ||
                !IsValidOptionalAbsolutePath(source) ||
                !IsValidOptionalAbsolutePath(destination))
            {
                error = "El origen o destino del backup tiene una ruta inválida.";
                return false;
            }

            // Las primeras copias v1 podían tener sólo AccentColor.
            // Se conserva su compatibilidad sin aceptar colores corruptos.
            var hasPrimary = TryGetString(root, "PrimaryColor", out var primaryColor);
            var hasLegacyAccent = TryGetString(root, "AccentColor", out var accentColor);
            if ((!hasPrimary && !hasLegacyAccent) ||
                (hasPrimary && !IsHexColor(primaryColor)) ||
                (!hasPrimary && !IsHexColor(accentColor)) ||
                (root.TryGetProperty("SecondaryColor", out var secondary) &&
                 secondary.ValueKind == JsonValueKind.String &&
                 !string.IsNullOrWhiteSpace(secondary.GetString()) &&
                 !IsHexColor(secondary.GetString()!)))
            {
                error = "Los colores del backup no son válidos.";
                return false;
            }

            if (root.TryGetProperty("CreatedAt", out var createdAt) &&
                (createdAt.ValueKind != JsonValueKind.String ||
                 !createdAt.TryGetDateTime(out _)))
            {
                error = "La fecha del backup no es válida.";
                return false;
            }

            if (!ValidateStringList(root, "SearchExtensionFilters", false, out error) ||
                !ValidateStringList(root, "OrganizeExtensionFilters", false, out error) ||
                !ValidateStringList(root, "ProtectedFolders", true, out error))
            {
                return false;
            }

            if (root.TryGetProperty("ProtectedFolders", out var protectedFolders) &&
                protectedFolders.ValueKind == JsonValueKind.Array)
            {
                foreach (var folder in protectedFolders.EnumerateArray())
                {
                    if (!IsValidOptionalAbsolutePath(folder.GetString(), allowEmpty: false))
                    {
                        error = "El backup contiene una carpeta protegida con una ruta inválida.";
                        return false;
                    }
                }
            }

            if (!root.TryGetProperty("Categories", out var categories) ||
                categories.ValueKind != JsonValueKind.Array ||
                categories.GetArrayLength() is < 1 or > 512)
            {
                error = "El backup no contiene una lista válida de categorías.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orders = new HashSet<int>();

            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind != JsonValueKind.Object ||
                    !TryGetString(category, "Id", out var id) ||
                    !TryGetString(category, "Name", out var name) ||
                    !category.TryGetProperty("Order", out var orderElement) ||
                    orderElement.ValueKind != JsonValueKind.Number ||
                    !orderElement.TryGetInt32(out var order) ||
                    order < 1 ||
                    order > 512 ||
                    string.IsNullOrWhiteSpace(id) ||
                    id.Length > 128 ||
                    !ids.Add(id) ||
                    !IsValidCategoryName(name) ||
                    !names.Add(name) ||
                    !orders.Add(order))
                {
                    error = "El backup contiene categorías duplicadas o con nombres, identificadores u órdenes inválidos.";
                    return false;
                }

                if (category.TryGetProperty("ColorHex", out var color) &&
                    (color.ValueKind != JsonValueKind.String ||
                     (!string.IsNullOrWhiteSpace(color.GetString()) &&
                      !IsHexColor(color.GetString()!))))
                {
                    error = $"La categoría {name} tiene un color inválido.";
                    return false;
                }

                if (!ValidateStringList(category, "Extensions", false, out error, required: true))
                {
                    return false;
                }

                foreach (var extension in category.GetProperty("Extensions").EnumerateArray())
                {
                    var value = extension.GetString()!;
                    if (string.IsNullOrWhiteSpace(value) ||
                        value.Length > 40 ||
                        value.Contains('/') ||
                        value.Contains('\\'))
                    {
                        error = $"La categoría {name} contiene una extensión inválida.";
                        return false;
                    }
                }
            }

            if (orders.Min() != 1 ||
                orders.Max() != categories.GetArrayLength())
            {
                error = "El orden de las categorías no es consecutivo.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "El archivo .bandanv está dañado o no contiene JSON válido.";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "El backup contiene rutas o datos que no pueden interpretarse de forma segura.";
            return false;
        }
    }

    private static bool ValidateStringList(
        JsonElement root,
        string key,
        bool allowNull,
        out string error,
        bool required = false)
    {
        error = string.Empty;

        if (!root.TryGetProperty(key, out var value))
        {
            if (!required)
            {
                return true;
            }

            error = $"Falta la lista {key} en el backup.";
            return false;
        }

        if (allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() > 1024 ||
            value.EnumerateArray().Any(item =>
                item.ValueKind != JsonValueKind.String ||
                item.GetString()!.Length > 2048))
        {
            error = $"La lista {key} es inválida.";
            return false;
        }

        return true;
    }

    private static bool TryGetString(
        JsonElement root,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(key, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool IsHexColor(string color) =>
        color.Length == 7 &&
        color[0] == '#' &&
        color.AsSpan(1).ToString().All(Uri.IsHexDigit);

    private static bool IsValidCategoryName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 100 &&
        name.Trim() == name &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains('/') &&
        !name.Contains('\\') &&
        !name.EndsWith('.') &&
        !name.EndsWith(' ');

    private static bool IsValidOptionalAbsolutePath(
        string? path,
        bool allowEmpty = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return allowEmpty;
        }

        return path.Length <= 1024 &&
               Path.IsPathFullyQualified(path) &&
               Path.IsPathRooted(path);
    }
}
