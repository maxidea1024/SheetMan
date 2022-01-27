using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using Newtonsoft.Json;

#if !NO_UNITY
using UnityEngine;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
#endif

namespace SheetMan.Runtime
{
    [Serializable]
    public class SheetManManifest
    {
        public DateTime LastUpdatedDate;
        public string MasterHash;
        public long TotalSize;

        [Serializable]
        public class Item
        {
            public string Name;
            public long Size;
            public string Hash;
            public DateTime LastUpdatedDate;
        }

        public List<Item> Items = new List<Item>();

        public class UpdateEvents
        {
            public long TotalDownloadSize;

            public List<string> Deleteds = new List<string>();
            public List<Item> AddedOrUpdateds = new List<Item>();
        }

#if !NO_UNITY
        public static async UniTask<SheetManManifest> LoadAsync(string filename)
#else
        public static async Task<SheetManManifest> LoadAsync(string filename)
#endif
        {
            try
            {
                //workaround
                //유니티에서는 File.ReadAllTextAsync 함수가 없음.
                //string json = File.ReadAllTextAsync(filename);
                string json = File.ReadAllText(filename);
                var manifest = JsonConvert.DeserializeObject<SheetManManifest>(json);
#if !NO_UNITY
                return await UniTask.FromResult(manifest);
#else
                return await Task.FromResult(manifest);
#endif
            }
            catch
            {
                // Just return empty
                return new SheetManManifest();
            }
        }

        public UpdateEvents Diff(string remoteManifestJson)
        {
            UpdateEvents result = new UpdateEvents();

            var remoteManifest = JsonConvert.DeserializeObject<SheetManManifest>(remoteManifestJson);
            //if (remoteManifest.MasterHash == MasterHash)
            //{
            //    return result;
            //}

            // detect deleted
            foreach (var previousItem in Items)
            {
                if (remoteManifest.Items.Find(x => x.Name == previousItem.Name) == null)
                {
                    //Debug.Log($"Deleted: {previousItem.Name}");

                    result.Deleteds.Add(previousItem.Name);
                }
            }
            // Remove from entry
            foreach (var deleted in result.Deleteds)
            {
                Items.RemoveAll(x => x.Name == deleted);
            }

            // detect what has been added or changed
            foreach (var remoteItem in remoteManifest.Items)
            {
                var previousItem = Items.Find(x => x.Name == remoteItem.Name);
                if (previousItem == null ||
                    !File.Exists(SheetManUpdater.GetLocalFilename(previousItem.Name)))//todo hash비교까지 해야할까?
                {
                    //Debug.Log($"Add to added: {remoteItem.Name}");

                    Items.Add(remoteItem);

                    result.AddedOrUpdateds.Add(remoteItem);
                    result.TotalDownloadSize += remoteItem.Size;
                }
                else if (remoteItem.Hash != previousItem.Hash)
                {
                    //Debug.Log($"Add to added or updated: {remoteItem.Name}");

                    previousItem.Size = remoteItem.Size;
                    previousItem.Hash = remoteItem.Hash;
                    previousItem.LastUpdatedDate = remoteItem.LastUpdatedDate;

                    result.AddedOrUpdateds.Add(remoteItem);
                    result.TotalDownloadSize += remoteItem.Size;
                }
            }

            if (result.Deleteds.Count > 0 || result.AddedOrUpdateds.Count > 0)
            {
                this.LastUpdatedDate = remoteManifest.LastUpdatedDate;
                this.MasterHash = remoteManifest.MasterHash;
                this.TotalSize = remoteManifest.TotalSize;
            }

            return result;
        }

#if !NO_UNITY
        public async UniTask UpdateAsync(string filename)
#else
        public async Task UpdateAsync(string filename)
#endif
        {
            string json = JsonConvert.SerializeObject(this);

            //workaround 유니티에서는 File.WriteAllTextAsync 함수가 없음.
            //await File.WriteAllTextAsync(filename, json);

#if !NO_UNITY
            await UniTask.Run(() => File.WriteAllText(filename, json));
#else
            await Task.Run(() => File.WriteAllText(filename, json));
#endif
        }
    }
}
