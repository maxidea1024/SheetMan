using Newtonsoft.Json;
using System.IO;

namespace SheetMan.Helpers
{
    /// <summary>
    ///
    /// </summary>
    public static class FileHelper
    {
        #region File and Directory relatives

        //https://stackoverflow.com/questions/58744/copy-the-entire-contents-of-a-directory-in-c-sharp

        /// <summary>
        /// Copy directory recursively.
        /// </summary>
        public static void CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
            DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

            CopyDirectory(diSource, diTarget);
        }

        /// <summary>
        /// Copy directory recursively.
        /// </summary>
        public static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                //Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
                CopyDirectory(diSourceSubDir, nextTargetSubDir);
            }
        }

        /// <summary>
        ///
        /// </summary>
        public static void EnsurePathExists(string filename)
        {
            var path = Path.GetDirectoryName(filename);

            var di = new DirectoryInfo(path);
            if (!di.Exists)
                Directory.CreateDirectory(path);
        }

        /// <summary>
        ///
        /// </summary>
        public static long GetFileSize(string filename)
        {
            try
            {
                var fi = new System.IO.FileInfo(filename);
                return fi.Length;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        ///
        /// </summary>
        public static void WriteAllTextToFile(string filename, string text, bool ensurePathExists = false, bool withMd5Hash = false)
        {
            if (ensurePathExists)
                EnsurePathExists(filename);

            File.WriteAllText(filename, text);

            if (withMd5Hash)
                File.WriteAllText(filename + ".md5", Helper.CalculateMD5HashFromString(text));
        }

        /// <summary>
        ///
        /// </summary>
        public static void WriteAllBytesToFile(string filename, byte[] data, bool ensurePathExists = false, bool withMd5Hash = false)
        {
            if (ensurePathExists)
                EnsurePathExists(filename);

            File.WriteAllBytes(filename, data);

            if (withMd5Hash)
                File.WriteAllText(filename + ".md5", Helper.CalculateMD5HashFromBytes(data));
        }

        /// <summary>
        ///
        /// </summary>
        public static string ReadAllTextFromFile(string filename)
        {
            return File.ReadAllText(filename);
        }

        /// <summary>
        ///
        /// </summary>
        public static byte[] ReadAllBytesFromFile(string filename)
        {
            return File.ReadAllBytes(filename);
        }

        /// <summary>
        ///
        /// </summary>
        public static void WriteToJsonFile(string filename, object obj, bool ensurePathExists = false, bool indented = true, bool withMd5Hash = false)
        {
            if (ensurePathExists)
                EnsurePathExists(filename);

            string json = JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);
            WriteAllTextToFile(filename, json, withMd5Hash);
        }

        /// <summary>
        ///
        /// </summary>
        public static T ReadFromJsonFile<T>(string filename)
        {
            string json = ReadAllTextFromFile(filename);
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        ///
        /// </summary>
        public static void MakesEmptyPath(string path)
        {
            DeletePathRecursively(path, false);
        }

        /// <summary>
        ///
        /// </summary>
        public static void DeletePathRecursively(string path, bool deleteSelf = true)
        {
            DeletePathRecursively(new DirectoryInfo(path), deleteSelf);
        }

        /// <summary>
        ///
        /// </summary>
        private static void DeletePathRecursively(DirectoryInfo baseDir, bool deleteSelf)
        {
            if (!baseDir.Exists)
                return;

            foreach (var dir in baseDir.EnumerateDirectories())
                DeleteSubPathRecursively(dir);

            var files = baseDir.GetFiles();
            foreach (var file in files)
            {
                file.IsReadOnly = false;
                file.Delete();
            }

            if (deleteSelf)
                baseDir.Delete();
        }

        /// <summary>
        ///
        /// </summary>
        private static void DeleteSubPathRecursively(DirectoryInfo baseDir)
        {
            if (!baseDir.Exists)
                return;

            foreach (var dir in baseDir.EnumerateDirectories())
                DeleteSubPathRecursively(dir);

            var files = baseDir.GetFiles();
            foreach (var file in files)
            {
                file.IsReadOnly = false;
                file.Delete();
            }

            baseDir.Delete();
        }
        #endregion
    }
}
