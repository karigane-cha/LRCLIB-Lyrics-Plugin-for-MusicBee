using System;

namespace MusicBeePlugin
{
    internal static class PluginVersionInfo
    {
        public const string ProductVersion = "0.1.0";
        public const string AssemblyVersionString = ProductVersion + ".0";

        public static Version AssemblyVersion
        {
            get { return typeof(PluginVersionInfo).Assembly.GetName().Version; }
        }

        public static string UserAgentVersion
        {
            get { return AssemblyVersion.ToString(3); }
        }
    }
}
