using System.Reflection;

namespace Reflux.Api
{
    /// <summary>
    /// Version shown to people: upstream's version plus the custom build mark ("1.16.6+custom.20261107", InformationalVersion
    /// in Reflux.csproj). The update check keeps comparing the plain upstream version (assembly version).
    /// </summary>
    static class BuildInfo
    {
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
