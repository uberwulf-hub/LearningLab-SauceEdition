// Learning Lab - where saved data lives. Windows: %APPDATA%\LearningLabSE\<app>; Android: the app's private files
// folder (Environment.SpecialFolder.ApplicationData maps there). The checks use a folder under %TEMP%.
using System;
using System.IO;

namespace LearningLab
{
    public static class Data
    {
        static string root;

        /// <summary>Learning Lab's own data folder (made when first asked for).</summary>
        public static string Root
        {
            get
            {
                if (root == null) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LearningLabSE");
                Directory.CreateDirectory(root);
                return root;
            }
        }

        /// <summary>The folder for one app's data (LearningGames, EmulatorLab, ...).</summary>
        public static string For(string app)
        {
            string d = Path.Combine(Root, app);
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>True in the checks: nothing touches the person's real data.</summary>
        public static bool Testing { get; private set; }


        static string cache;

        /// <summary>A folder for things that can be made again (spoken lessons): %LOCALAPPDATA%\LearningLabSE\<app> on
        /// Windows, the app's cache-like files folder on Android. Never holds anything the person made.</summary>
        public static string Cache(string app)
        {
            if (cache == null) cache = Testing ? Path.Combine(Path.GetDirectoryName(root), "cache") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LearningLabSE");
            string d = Path.Combine(cache, app);
            Directory.CreateDirectory(d);
            return d;
        }

        public static void UseTestFolder()
        {
            Testing = true;
            cache = null;
            root = Path.Combine(Path.GetTempPath(), "LearningLabSE-test", "data");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
        }

        /// <summary>App Hub's data folder for an app on this PC (Windows only; null elsewhere), read once on first start.</summary>
        public static string AppHubFolder(string app)
        {
            if (Testing || !OperatingSystem.IsWindows()) return null;
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app);
            return Directory.Exists(d) ? d : null;
        }
    }
}
