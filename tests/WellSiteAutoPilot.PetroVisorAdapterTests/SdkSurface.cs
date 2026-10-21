using System.Reflection;
using System.Text;

/// <summary>
/// Prints the public shape of the pinned PetroVisor SDK as GitHub notice annotations so it can be read
/// from the check run. Type and member names and parameter types only: no values, no network, no credentials.
/// </summary>
internal static class SdkSurface
{
    private static readonly string[] Exact =
    [
        "AbstractServiceProvider", "ClientConfiguration", "AbstractRepositoryServicesContainer`1", "PetroVisorUserInfo",
        "Entity", "EntityType", "AbstractNamedItem", "AbstractLightPetroVisorItem", "NativeClientTokens",
    ];

    private static bool Wanted(Type type) =>
        Exact.Contains(type.Name, StringComparer.Ordinal) ||
        (type.IsInterface && type.Name.StartsWith("IRepositoryService", StringComparison.Ordinal)) ||
        (type.IsInterface && type.Name.Contains("Entity", StringComparison.Ordinal) && type.Name.EndsWith("Service", StringComparison.Ordinal)) ||
        (type.IsInterface && type.Name.Contains("Workspace", StringComparison.Ordinal)) ||
        type.Name.Contains("Token", StringComparison.Ordinal) || type.Name.Contains("Identity", StringComparison.Ordinal) ||
        type.Name.Contains("Auth", StringComparison.Ordinal) || type.Name.Contains("Windows", StringComparison.Ordinal);

    public static void Emit()
    {
        var lines = new List<string>();
        foreach (var name in new[] { "MyrConn.PetroVisor.Web.Client", "MyrConn.PetroVisor.API" })
        {
            Assembly assembly;
            try { assembly = Assembly.Load(name); }
            catch (Exception exception) { lines.Add(name + ": not loadable (" + exception.GetType().Name + ")"); continue; }

            Type[] types;
            try { types = assembly.GetExportedTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(t => t is not null).Select(t => t!).ToArray(); }

            foreach (var type in types.Where(Wanted).OrderBy(t => t.FullName, StringComparer.Ordinal))
                lines.Add(Describe(type));
        }

        // The annotation text is cut at about 4 KB and a step may carry ten, so pack tightly and stop at the limit.
        var chunk = new StringBuilder();
        var index = 1;
        foreach (var line in lines)
        {
            if (chunk.Length + line.Length > 3800)
            {
                if (index >= 10) return;
                Notice(index++, chunk.ToString());
                chunk.Clear();
            }
            chunk.Append(line.Length > 3800 ? line[..3800] : line).Append('\n');
        }
        if (chunk.Length > 0 && index <= 10) Notice(index, chunk.ToString());
    }

    private static string Describe(Type type)
    {
        var sb = new StringBuilder();
        sb.Append(type.IsInterface ? "interface " : type.IsEnum ? "enum " : "class ").Append(type.FullName);
        if (type.IsEnum)
        {
            sb.Append(" { ").Append(string.Join(", ", Enum.GetNames(type))).Append(" }");
            return sb.ToString();
        }
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            sb.Append("\n    ctor(").Append(Parameters(ctor.GetParameters())).Append(')');
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName).OrderBy(m => m.Name, StringComparer.Ordinal))
            sb.Append("\n    ").Append(Short(method.ReturnType)).Append(' ').Append(method.Name).Append('(')
              .Append(Parameters(method.GetParameters())).Append(')');
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
            sb.Append("\n    prop ").Append(Short(property.PropertyType)).Append(' ').Append(property.Name);
        return sb.ToString();
    }

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => Short(p.ParameterType) + " " + p.Name));

    private static string Short(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        return type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(Short)) + ">";
    }

    private static void Notice(int index, string text) =>
        Console.WriteLine($"::notice title=PetroVisor SDK surface {index}::" +
            text.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal));
}
