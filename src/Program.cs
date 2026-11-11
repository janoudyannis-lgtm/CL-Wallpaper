using System;
using System.Reflection;
using System.Runtime.Versioning;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("CL-Wallpaper")]
[assembly: AssemblyProduct("CL-Wallpaper")]
[assembly: AssemblyDescription("Fonds d'écran animés pour Windows")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]
[assembly: AssemblyInformationalVersion("1.2.1")]
[assembly: AssemblyCompany("CL-Wallpaper")]

namespace CLWallpaper
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--host") >= 0) return Host.HostApp.Run(args);
                return Ui.UiApp.Run(args);
            }
            catch (Exception ex)
            {
                Store.Log("fatal: " + ex);
                return 1;
            }
        }
    }
}
