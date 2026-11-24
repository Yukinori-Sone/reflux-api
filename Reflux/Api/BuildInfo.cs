using System.Reflection;

namespace Reflux.Api
{
    /// <summary>
    /// Version shown to people: upstream's version plus this custom version's number and the build date ("1.16.6-custom.2+20261124",
    /// InformationalVersion in Reflux.csproj). The update check keeps comparing the plain upstream version (assembly version).
    /// </summary>
    static class BuildInfo
    {
        /// <summary>The N of "-custom.N" (Reflux.csproj CustomRevision), 0 when missing</summary>
        public static int CustomRevision
        {
            get
            {
                var m = System.Text.RegularExpressions.Regex.Match(DisplayVersion, @"-custom\.(\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
        }

        public static string DisplayVersion
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version.ToString(3);
            }
        }
    }
}
