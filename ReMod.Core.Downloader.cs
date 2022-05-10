using System;
using System.Net;
using System.Reflection;

namespace AltNetIk
{
    public static class ReMod_Core_Downloader
    {
        // https://github.com/MintLily/VRChat-TeleporterVR/blob/main/Utils/ReMod.Core.Downloader.cs
        public static void LoadReModCore()
        {
            Assembly loadedAssembly;
            byte[] bytes = null;
            var wc = new WebClient();
            try
            {
                bytes = wc.DownloadData("https://github.com/RequiDev/ReMod.Core/releases/latest/download/ReMod.Core.dll");
                loadedAssembly = Assembly.Load(bytes);
            }
            catch (WebException e)
            {
                AltNetIk.Logger.Msg($"Unable to Load Dependency ReMod.Core: {e}");
            }
            catch (BadImageFormatException)
            {
                loadedAssembly = null;
            }
        }
    }
}