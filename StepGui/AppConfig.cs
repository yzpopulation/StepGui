using System;
using System.IO;
using System.Text;

namespace StepGui
{
    public static class AppConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepGui");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.txt");

        public static string StepPath { get; private set; }
        public static string StorePath { get; private set; }

        public static void Load()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            StepPath = Path.Combine(baseDir, "step.exe");
            StorePath = Path.Combine(baseDir, "certs");
            try
            {
                if (File.Exists(ConfigFile))
                {
                    foreach (string line in File.ReadAllLines(ConfigFile))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string key = line.Substring(0, i).Trim();
                        string val = line.Substring(i + 1).Trim();
                        if (key == "step" && val.Length > 0) StepPath = val;
                        else if (key == "store" && val.Length > 0) StorePath = val;
                    }
                }
            }
            catch { }
            if (!File.Exists(StepPath))
            {
                string fallback = Path.Combine(baseDir, "step.exe");
                if (File.Exists(fallback)) StepPath = fallback;
            }
            EnsureStore();
        }

        public static void SetPaths(string stepPath, string storePath)
        {
            if (!string.IsNullOrEmpty(stepPath)) StepPath = stepPath;
            if (!string.IsNullOrEmpty(storePath)) StorePath = storePath;
            Save();
            EnsureStore();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var sb = new StringBuilder();
                sb.Append("step=").AppendLine(StepPath);
                sb.Append("store=").AppendLine(StorePath);
                File.WriteAllText(ConfigFile, sb.ToString());
            }
            catch { }
        }

        public static void EnsureStore()
        {
            try { Directory.CreateDirectory(StorePath); } catch { }
        }
    }
}
