using System.Management;

namespace AllyTuner.Platform.Windows;

/// <summary>Small read-only WMI query helpers. Only SELECT queries and class metadata; no method calls.</summary>
internal static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string scope, string wql)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var collection = searcher.Get();
        foreach (var item in collection)
        {
            using (item)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    public static string? Text(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()!.Trim()
            : null;

    /// <summary>Names of WMI classes in a namespace whose name contains <paramref name="fragment"/>.</summary>
    public static List<string> ClassNames(string scope, string fragment)
    {
        // meta_class lists class definitions; LIKE is documented for WQL:
        // https://learn.microsoft.com/windows/win32/wmisdk/like-operator
        var names = new List<string>();
        using var searcher = new ManagementObjectSearcher(scope, $"SELECT * FROM meta_class WHERE __CLASS LIKE '%{fragment}%'");
        using var collection = searcher.Get();
        foreach (var item in collection)
        {
            using (item)
            {
                if (item["__CLASS"] is string name)
                {
                    names.Add(name);
                }
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>Method signatures of a WMI class, read from its definition (nothing is invoked).</summary>
    public static List<string> MethodSignatures(string scope, string className)
    {
        var signatures = new List<string>();
        using var cls = new ManagementClass(scope, className, null);
        foreach (var method in cls.Methods)
        {
            signatures.Add($"{method.Name}({Parameters(method.InParameters)}) -> ({Parameters(method.OutParameters)})");
        }

        return signatures;
    }

    private static string Parameters(ManagementBaseObject? parameters)
    {
        if (parameters is null)
        {
            return string.Empty;
        }

        // Owned by the MethodData; not disposed here.
        return string.Join(", ", parameters.Properties.Cast<PropertyData>()
            .Where(p => !p.Name.StartsWith("__", StringComparison.Ordinal))
            .Select(p => $"{p.Type} {p.Name}"));
    }
}
