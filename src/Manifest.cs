using Newtonsoft.Json;
using SheetMan.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SheetMan;

public class Manifest
{
    public DateTime LastUpdatedDate { get; set; }

    public string MasterHash { get; set; }

    public long TotalSize { get; set; }

    public class Item
    {
        public string Name { get; set; }

        // Relative, because an absolute path is one machine's answer: a manifest written
        // on a build server would name directories that do not exist anywhere else.
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
        // Read from the committed output rather than from staging: staging is emptied
        // once a run commits, so by now there is nothing there.
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
        // Drop what is no longer there.
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
