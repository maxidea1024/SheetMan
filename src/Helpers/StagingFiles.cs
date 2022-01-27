using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace SheetMan.Helpers
{
    /// <summary>
    /// If we write the file right away, if an error occurs in the state that it is not finally completed, potential problems may occur,
    /// It is used to move to the actual file only when it is finally completed.
    /// </summary>
    public static class StagingFiles
    {
        static readonly List<(string, string)> _stagingFiles = new List<(string, string)>();

        /// <summary>
        /// Deletes the staging files when an error occurs.
        /// </summary>
        public static void Rollback()
        {
            // Delete all junky artifact files.
            try
            {
                foreach (var kv in _stagingFiles)
                    File.Delete(kv.Item2);
            }
            catch
            {
                // Sink all exceptions. (don't worry)
            }

            _stagingFiles.Clear();
        }

        /// <summary>
        /// Add one staged file and return the staged file name.
        /// </summary>
        public static string RegisterStagingFile(string filename)
        {
            string fullPath = Path.GetFullPath(filename);
            string md5 = Helper.CalculateMD5HashFromString(fullPath);

            string tempPath = Path.GetTempPath();
            string stagingFilename = Path.Combine(tempPath, md5 + ".staging");

            if (_stagingFiles.Where(x => x.Item2 == stagingFilename).Count() > 0)
                return stagingFilename;

            var kv = (fullPath, stagingFilename);
            _stagingFiles.Add(kv);

            return kv.stagingFilename;
        }

        /// <summary>
        /// Commits all staging files to the original files.
        /// </summary>
        public static void CommitFiles(Action<string, string> progressCallback = null)
        {
            while (_stagingFiles.Count > 0)
            {
                var kv = _stagingFiles[0];

                // Progress
                progressCallback?.Invoke(kv.Item1, kv.Item2);

                try
                {
                    File.Delete(kv.Item1);
                }
                catch (DirectoryNotFoundException)
                {
                    // Sink exception
                }

                FileHelper.EnsurePathExists(kv.Item1);
                File.Move(kv.Item2, kv.Item1);

                try
                {
                    File.Delete(kv.Item2);
                }
                catch
                {
                    // Sink exception
                }

                _stagingFiles.RemoveAt(0);
            }
        }

        /// <summary>
        /// Creates a new file, write the contents to the file, and then closes the file.
        /// If the target file already exists, it is overwritten.
        /// </summary>
        public static string WriteAllTextToFile(string filename, string text)
        {
            string stagingFilename = RegisterStagingFile(filename);
            File.WriteAllText(stagingFilename, text);
            return stagingFilename;
        }

        /// <summary>
        /// Creates a new file, writes the specified byte array to the file, and then closes the file.
        /// If the target file already exists, it is overwritten.
        /// </summary>
        public static string WriteAllBytesToFile(string filename, byte[] data)
        {
            string stagingFilename = RegisterStagingFile(filename);
            File.WriteAllBytes(stagingFilename, data);
            return stagingFilename;
        }

        /// <summary>
        /// Creates a new file, writes the specified object to the .json file, and then closes the file.
        /// If the target file already exists, it is overwritten.
        /// </summary>
        public static string WriteToJsonFile(string filename, object obj, bool indented = true)
        {
            string json = JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);
            return WriteAllTextToFile(filename, json);
        }



        //TODO

        /*

        /// <summary>Copy directory recursively</summary>
        public static void CopyDirectory(string sourceDirectory, string targetDirectory)
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
        */
    }
}
