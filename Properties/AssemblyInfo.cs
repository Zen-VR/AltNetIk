using MelonLoader;
using System.Reflection;

[assembly: AssemblyTitle(AltNetIk.BuildInfo.Name)]
[assembly: AssemblyCompany(AltNetIk.BuildInfo.Company)]
[assembly: AssemblyProduct(AltNetIk.BuildInfo.Name)]
[assembly: AssemblyCopyright("Created by " + AltNetIk.BuildInfo.Author)]
[assembly: AssemblyTrademark(AltNetIk.BuildInfo.Company)]
[assembly: AssemblyVersion(AltNetIk.BuildInfo.Version)]
[assembly: AssemblyFileVersion(AltNetIk.BuildInfo.Version)]
[assembly: MelonInfo(typeof(AltNetIk.AltNetIk), AltNetIk.BuildInfo.Name, AltNetIk.BuildInfo.Version, AltNetIk.BuildInfo.Author, AltNetIk.BuildInfo.DownloadLink)]
[assembly: MelonGame("VRChat", "VRChat")]
[assembly: MelonOptionalDependencies("ReMod.Core")]

namespace AltNetIk
{
    public static class BuildInfo
    {
        public const string Name = "AltNetIk";
        public const string Author = "Zen.";
        public const string Company = "Lava Gang";
        public const string Version = "1.8.0";
        public const string DownloadLink = "";
    }
}