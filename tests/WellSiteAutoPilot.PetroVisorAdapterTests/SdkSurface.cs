using System.Reflection;
using System.Text;

/// <summary>
/// Prints the public shape of the pinned PetroVisor SDK as GitHub notice annotations so it can be read
/// from the check run. Type and member names and parameter types only: no values, no network, no credentials.
/// </summary>
internal static class SdkSurface
{
    private static readonly string[] Interesting =
    [
        "ServiceProvider", "EndPoint", "Endpoint", "Authentic", "Credential", "Windows", "Negotiate", "Kerberos",
        "Integrated", "Sso", "Token", "Session", "Workspace", "IRepositoryService", "RepositoryService",
        "IDataService", "DataService", "IEntity", "EntityType", "Entity", "Signal", "DataRequest", "Scope",
    ];

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

            lines.Add($"{name}: {types.Length} exported types");
            lines.Add(name + " type names: " + string.Join(", ", types.Select(t => t.Name).Order(StringComparer.Ordinal)));
            foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (!Interesting.Any(word => type.Name.Contains(word, StringComparison.Ordinal))) continue;
                lines.Add(Describe(type));
            }
        }

        // GitHub allows a limited number of annotations per step, so pack the lines into a few large ones.
        var chunk = new StringBuilder();
        var index = 1;
        foreach (var line in lines)
        {
            if (chunk.Length + line.Length > 30000) { Notice(index++, chunk.ToString()); chunk.Clear(); }
            chunk.Append(line).Append('\n');
        }
        if (chunk.Length > 0) Notice(index, chunk.ToString());
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
