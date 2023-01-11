using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WAPPOPInvoice
{
    /// <summary>
    /// Class to resolve assemblies from the Sage Server
    /// </summary>
    internal static class AssemblyResolver
    {
        #region Members

        private const string CLIENT_INSTALL_KEY = @"Software\Sage\MMS\";
        private const string CLIENT_INSTALL_LOCATION = @"ClientInstallLocation";
        private const string SAGE_COMMON_ASSEMBLY = "Sage.Common.dll";
        private const string SAGE_ASSEMBLY_RESOLVER = "Sage.Common.Utilities.AssemblyResolver";
        private const string SAGE_RESOLVER_METHOD = "GetResolver";

        #endregion Members

        #region Methods

        /// <summary>
        /// Resolves the assemblies
        /// </summary>
        internal static void ResolveAssemblies()
        {
            try
            {
                RegistryKey MMS_Key = Registry.CurrentUser.OpenSubKey(CLIENT_INSTALL_KEY, false);

                if (MMS_Key == null)
                    throw new ApplicationException("Could not find Sage Client Install Location Registry Key.\r\nIs the Sage Client Installed on this machine?");

                string clientInstallLocation = MMS_Key.GetValue(CLIENT_INSTALL_LOCATION)?.ToString() ?? string.Empty;

                if (!Directory.Exists(clientInstallLocation))
                    throw new ApplicationException($"Sage client Install Location could not be found or accessed at '{clientInstallLocation}'.");

                string location = AppDomain.CurrentDomain.BaseDirectory;
                string currentDirectory = new FileInfo(location).Directory.FullName;

                //Clean existing Sage Assebmlies that may have been copied local by mistake
                try
                {
                    AssemblyResolver.DeleteSageAssemblies(currentDirectory, clientInstallLocation);
                }
                catch (Exception) { }

                //Find where Sage 200 is installed
                FindCore200();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Locates and invokes assemblies from the client folder at runtime.
        /// </summary>
        private static void FindCore200()
        {
            try
            {
                // get registry info for Sage 200 server path
                string path = string.Empty;
                RegistryKey root = Registry.CurrentUser;
                RegistryKey key = root.OpenSubKey(CLIENT_INSTALL_KEY);

                if (key != null)
                {
                    object value = key.GetValue(CLIENT_INSTALL_LOCATION);
                    if (value != null)
                    {
                        path = value as string;
                    }
                }

                // refer to all installed assemblies based on location of default one
                if (string.IsNullOrEmpty(path) == false)
                {
                    string commonDllAssemblyName = Path.Combine(path, SAGE_COMMON_ASSEMBLY);

                    if (System.IO.File.Exists(commonDllAssemblyName))
                    {
                        System.Reflection.Assembly defaultAssembly = System.Reflection.Assembly.LoadFrom(commonDllAssemblyName);
                        Type type = defaultAssembly.GetType(SAGE_ASSEMBLY_RESOLVER);
                        MethodInfo method = type.GetMethod(SAGE_RESOLVER_METHOD);
                        method.Invoke(null, null);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Deletes Sage assemblies from the specified file
        /// </summary>
        /// <param name="path">The Path</param>
        /// <param name="clientInstallLocation">The Client Install Location</param>
        private static void DeleteSageAssemblies(string path, string clientInstallLocation)
        {
            try
            {
                if (!Directory.Exists(path))
                    throw new ApplicationException($"Could not delete Sage Assemblies. Path '{path}' does not exist.");

                if (AssemblyResolver.NormalisePath(path) == AssemblyResolver.NormalisePath(clientInstallLocation))
                    throw new ApplicationException("Sage assemblies cannot be deleted from the Sage Client Install Location.");

                foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("Sage*.dll"))
                    file.Delete();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Normalises a path for Comparison
        /// </summary>
        /// <param name="path">The Path</param>
        /// <returns>string</returns>
        private static string NormalisePath(string path)
        {
            try
            {
                return Path.GetFullPath(new Uri(path).LocalPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToUpperInvariant();
            }
            catch (Exception)
            {
                throw;
            }
        }

        #endregion Methods
    }
}
