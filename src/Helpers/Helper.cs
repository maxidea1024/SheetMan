using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SheetMan.Helpers
{
    /// <summary>
    ///
    /// </summary>
    public static class Helper
    {
        //TODO 아주 제한적으로만 동작하는 함수. 차후에 고칠까?
        public static string StripNumber(string str)
        {
            string result = "";
            for (int i = 0; i < str.Length; i++)
            {
                if (!char.IsDigit(str[i]))
                    result += str[i];
            }

            return result;
        }

        //TODO 아주 제한적으로만 동작하는 함수. 차후에 고칠까?
        public static string ExtractNumber(string str)
        {
            string result = "";
            for (int i = 0; i < str.Length; i++)
            {
                if (char.IsDigit(str[i]))
                    result += str[i];
            }

            return result;
        }

        /// <summary>
        ///
        /// </summary>
        public static string CalculateMD5HashFromBytes(byte[] data)
        {
            using var md5Provider = new MD5CryptoServiceProvider();
            var hash = md5Provider.ComputeHash(data);
            if (hash == null)
                return "";
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        ///
        /// </summary>
        public static string CalculateMD5HashFromString(string str)
        {
            using var md5Provider = new MD5CryptoServiceProvider();
            var hash = md5Provider.ComputeHash(Encoding.UTF8.GetBytes(str));
            if (hash == null)
                return "";
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        ///
        /// </summary>
        public static string CalculateMD5HashFromFile(string filename)
        {
            byte[] data = File.ReadAllBytes(filename);
            return CalculateMD5HashFromBytes(data);
        }

        /// <summary>
        ///
        /// </summary>
        public static string CalculateMd5HashForPath(string path, string filePattern = "*.*", bool allDirectories = true)
        {
            // assuming you want to include nested folders
            var files = Directory.GetFiles(path, filePattern, SearchOption.AllDirectories)
                                 .OrderBy(p => p).ToList();

            MD5 md5 = MD5.Create();

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];

                // hash path
                string relativePath = file.Substring(path.Length + 1);
                byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLower());
                md5.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

                // hash contents
                byte[] contentBytes = File.ReadAllBytes(file);

                if (i == files.Count - 1)
                    md5.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
                else
                    md5.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
            }

            if (md5.Hash == null)
                return "";

            return BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();
        }

        /// <summary>
        ///
        /// </summary>
        public static string CalculateMD5HashFromFiles(string[] filenames)
        {
            MD5 md5 = MD5.Create();

            for (int i = 0; i < filenames.Length; i++)
            {
                var filename = filenames[i];

                byte[] data = File.ReadAllBytes(filename);

                if (i == filenames.Length - 1)
                    md5.TransformFinalBlock(data, 0, data.Length);
                else
                    md5.TransformBlock(data, 0, data.Length, data, 0);
            }

            if (md5.Hash == null)
                return "";

            return BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();
        }
    }
}
