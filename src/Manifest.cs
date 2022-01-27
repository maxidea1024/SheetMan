using Newtonsoft.Json;
using SheetMan.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SheetMan
{
    public class Manifest
    {
        public DateTime LastUpdatedDate { get; set; }

        public string MasterHash { get; set; }

        public long TotalSize { get; set; }

        public class Item
        {
            public string Name { get; set; }

            //전체 경로를 저장하게 되면 다른 피씨에서 빌드할때 문제가 될수 있음.
            [JsonIgnore]
            public string Filename { get; set; }

            public long Size { get; set; }

            public string Hash { get; set; }

            public DateTime LastUpdatedDate { get; set; }

            [JsonIgnore]
            public bool Dirty { get; set; }
        }

        public List<Item> Items { get; set; } = new List<Item>();
        private int _dirtyCount = 0;

        public void Add(string name, string filename)
        {
            long size = FileHelper.GetFileSize(filename);
            var hash = Helper.CalculateMD5HashFromFile(filename);

            var existing = Items.Find(x => x.Name == name);
            if (existing != null)
            {
                existing.Filename = filename;

                if (hash != existing.Hash)
                {
                    existing.Dirty = true;
                    existing.Hash = hash;
                    existing.Size = size;
                    existing.LastUpdatedDate = DateTime.Now;
                    _dirtyCount++;
                }
            }
            else
            {
                var item = new Item
                {
                    Dirty = true,
                    Name = name,
                    Hash = hash,
                    Filename = filename,
                    Size = size,
                    LastUpdatedDate = DateTime.Now
                };
                _dirtyCount++;

                Items.Add(item);
            }
        }

        public static Manifest Load(string filename)
        {
            // 불러오는건 staging 에서 하면 안된다. 이미 삭제되었을테니...
            //string stagingFilename = StagingFiles.RegisterStagingFile(filename);

            try
            {
                return FileHelper.ReadFromJsonFile<Manifest>(filename);
            }
            catch
            {
                return new Manifest();
            }
        }

        public void BuildAndWriteToFile(string filename)
        {
            // 삭제된것 반영.
            _dirtyCount += Items.RemoveAll(x => x.Filename == null);

            if (_dirtyCount > 0 || Items.Count == 0)
            {
                LastUpdatedDate = DateTime.Now;
                MasterHash = Helper.CalculateMD5HashFromFiles(Items.Select(x => x.Filename).ToArray());
                TotalSize = 0;

                foreach (var item in Items)
                    TotalSize += item.Size;

                StagingFiles.WriteToJsonFile(filename, this);
            }
        }
    }
}
