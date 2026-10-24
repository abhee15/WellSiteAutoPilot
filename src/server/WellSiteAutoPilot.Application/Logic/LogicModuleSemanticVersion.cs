using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Application.Logic;

// SemVer precedence without integer conversion: arbitrarily large numeric identifiers
// are compared by digit count, then ordinal value. Build metadata has no precedence.
internal sealed partial class LogicModuleSemanticVersion(string[] core, string[] prerelease)
    : IComparable<LogicModuleSemanticVersion>
{
    private readonly string[] _core = core;
    private readonly string[] _prerelease = prerelease;

    public static bool TryParse(string? value, out LogicModuleSemanticVersion? version)
    {
        version = null;
        if (value is null || value.Length > 256) return false;
        var match = Pattern().Match(value);
        if (!match.Success) return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0')) return false;
        version = new LogicModuleSemanticVersion(
            [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], prerelease);
        return true;
    }

    public int CompareTo(LogicModuleSemanticVersion? other)
    {
        if (other is null) return 1;
        for (var index = 0; index < _core.Length; index++)
        {
            var comparison = CompareNumeric(_core[index], other._core[index]);
            if (comparison != 0) return comparison;
        }
        if (_prerelease.Length == 0 || other._prerelease.Length == 0)
            return (_prerelease.Length == 0 ? 1 : 0).CompareTo(other._prerelease.Length == 0 ? 1 : 0);
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            var left = _prerelease[index];
            var right = other._prerelease[index];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var comparison = leftNumeric && rightNumeric ? CompareNumeric(left, right)
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1)
                : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    private static bool IsNumeric(string value) => value.All(char.IsAsciiDigit);
    private static int CompareNumeric(string left, string right) => left.Length != right.Length
        ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
