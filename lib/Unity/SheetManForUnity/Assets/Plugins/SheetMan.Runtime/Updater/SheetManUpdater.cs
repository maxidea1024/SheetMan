using System.Collections;
using System.Collections.Generic;
using System.IO;

#if !NO_UNITY
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
#endif

namespace SheetMan.Runtime
{
    /// <summary>
    /// SheetMan에서 생성된 데이터를 CDN에서 받아와 최신데이터로 업데이트해주는 기능을 담당합니다.
    /// </summary>
    public static class SheetManUpdater
    {
#if NO_UNITY
        //TODO single-file에서 문제가 있지 않으려나?
        public static readonly string LocalBasePath = Path.Combine(System.Reflection.Assembly.GetEntryAssembly().Location, "cached-sheets-data");
#else
        public static readonly string LocalBasePath = Path.Combine(Application.persistentDataPath, "cached-sheets-data");
#endif

        // Manifest 파일 이름.
        const string ManifestFilename = "manifest.json";

        /// <summary>
        /// 업데이트를 수행합니다.
        /// 원격지의 manifest 파일을 받아와서 로컬에 캐싱된 데이터와 비교하여 최신 데이터를 가져옵니다.
        /// </summary>
#if NO_UNITY
        public static async Task<string> UpdateAsync(string baseUrl)
#else
        public static async UniTask<string> UpdateAsync(string baseUrl)
#endif
        {
            try
            {
                // Load the cached manifest file in the local path.
                var localManifest = await SheetManManifest.LoadAsync(GetLocalFilename(ManifestFilename));

                // Get the manifest file on the remote.
                var remoteManifestJson = await DownloadRemoteManifestAsync(GetRemoteFileUrl(baseUrl, ManifestFilename));

                // Match the local and remote manifest to get the changed event.
                var events = localManifest.Diff(remoteManifestJson);

                // The data is already up-to-date as there are no changed events.
                if (events.Deleteds.Count == 0 && events.AddedOrUpdateds.Count == 0)
                {
                    Log($"[SheetManUpdater] It's already up to date. LastUpdatedDate={localManifest.LastUpdatedDate}, MasterHash={localManifest.MasterHash}");
                    return null;
                }


                Log($"[SheetManUpdater] Update with the latest data. Changeds={events.AddedOrUpdateds.Count}, Deleteds={events.Deleteds.Count}, TotalDownloadSize={events.TotalDownloadSize}B");

                // Remove deleted files.
                foreach (var deleted in events.Deleteds)
                {
                    try
                    {
                        File.Delete(GetLocalFilename(deleted));
                    }
                    catch
                    {
                        // Sunk all exceptions
                    }
                }

                // If there is no added or changed file, return immediately.
                if (events.AddedOrUpdateds.Count == 0)
                    return null;

                // Download added or changed files.
#if NO_UNITY
                List<Task> tasks = new List<Task>(events.AddedOrUpdateds.Count);
                foreach (var addedOrUpdated in events.AddedOrUpdateds)
                    tasks.Add(DownloadFileAsync(GetRemoteFileUrl(baseUrl, addedOrUpdated.Name), GetLocalFilename(addedOrUpdated.Name)));

                await Task.WhenAll(tasks);
#else
                List<UniTask> tasks = new List<UniTask>(events.AddedOrUpdateds.Count);
                foreach (var addedOrUpdated in events.AddedOrUpdateds)
                    tasks.Add(DownloadFileAsync(GetRemoteFileUrl(baseUrl, addedOrUpdated.Name), GetLocalFilename(addedOrUpdated.Name)));

                await UniTask.WhenAll(tasks);
#endif

                // Finally update the local manifest file.
                await localManifest.UpdateAsync(GetLocalFilename(ManifestFilename));

                // OK
                Log($"[SheetManUpdater] Update completed. LastUpdatedDate={localManifest.LastUpdatedDate}, MasterHash={localManifest.MasterHash}");
                return null;
            }
            catch (System.Exception ex)
            {
                //Log(ex);
                return ex.Message;
            }
        }

        //TODO HttpClient
        /// <summary>
        /// 원격지의
        /// </summary>
        private static async UniTask<string> DownloadRemoteManifestAsync(string remoteFileUrl)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(remoteFileUrl))
            {
                await request.SendWebRequest();

                if (IsWebRequestFailed(request))
                    throw new System.Exception(request.error);

                return request.downloadHandler.text;
            }
        }

        //TODO HttpClient
        /// <summary>
        ///
        /// </summary>
        private static async UniTask DownloadFileAsync(string remoteFileUrl, string localFilename)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(remoteFileUrl))
            {
                await request.SendWebRequest();

                if (IsWebRequestFailed(request))
                    throw new System.Exception(request.error);

                var data = request.downloadHandler.data;

                Directory.CreateDirectory(Path.GetDirectoryName(localFilename));

                //workaround 유니티에서는 File.WriteAllBytesAsync 함수가 없음. 유니티 유감..
                //await File.WriteAllBytesAsync(localFilename, data);
                await UniTask.Run(() => File.WriteAllBytes(localFilename, data));
            }
        }

#if !NO_UNITY
        /// <summary>
        ///
        /// </summary>
        private static bool IsWebRequestFailed(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                return true;

            return false;
        }
#endif

        /// <summary>
        ///
        /// </summary>
        public static string GetRemoteFileUrl(string baseUrl, string filename)
            => Path.Combine(baseUrl, filename).Replace("\\", "/");

        /// <summary>
        ///
        /// </summary>
        public static string GetLocalFilename(string filename)
            => Path.Combine(LocalBasePath, filename);

        //TODO delegate로 빼주는게 좋을듯..
        private static void Log(string message)
        {
#if !NO_UNITY
            Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }
    }
}
