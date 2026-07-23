using System;
using System.IO;
using System.Reflection;

namespace AlacrityTerraria
{
    public static class AlacrityBootstrapRuntime
    {
        private static int _state;

        public static string LastError { get; private set; }

        public static void Load()
        {
            if (_state != 0)
                return;

            try
            {
                LoadAssembly("Alacrity.PluginSdk.dll");
                LoadAssembly("Alacrity.Core.dll");
                _state = 1;
            }
            catch (Exception exception)
            {
                // This test bootstrap must never prevent the original game from launching.
                LastError = exception.GetType().Name + ": " + exception.Message;
                _state = -1;
            }
        }

        public static bool IsReady => _state == 1;

        private static Assembly LoadAssembly(string fileName)
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(AlacrityBootstrapRuntime).Assembly.Location);
            string path = Path.Combine(assemblyDirectory ?? AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Alacrity dependency was not found.", path);

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }

            return Assembly.LoadFrom(path);
        }
    }
}
