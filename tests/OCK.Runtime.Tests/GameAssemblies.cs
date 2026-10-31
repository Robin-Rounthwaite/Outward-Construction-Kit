using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace OutwardConstructionKit.Tests
{
    /// <summary>
    /// The plugin's logic is tested against the game's own assemblies, loaded from the game folder rather than
    /// copied here, without needing the game to run.
    /// </summary>
    [SetUpFixture]
    public class GameAssemblies
    {
        [OneTimeSetUp]
        public void FindThemInTheGameFolder()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (AssemblyMetadataAttribute folder in typeof(GameAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                string path = Path.Combine(folder.Value ?? "", file);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            return null;
        }
    }
}
