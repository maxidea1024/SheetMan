using SheetMan.Models;
using System.Collections.Generic;

namespace SheetMan.Summary
{
    public static class Summary
    {
        public class Entry
        {
            public string Name;
            public Location Location;
        }
        
        private static readonly List<Entry> _ignoredSheetss = new List<Entry>();
        private static readonly List<Entry> _ignoredSheets = new List<Entry>();
        private static readonly List<Entry> _ignoredFields = new List<Entry>();
        private static readonly List<Entry> _ignoredConstants = new List<Entry>();
        private static readonly List<Entry> _ignoredVariables = new List<Entry>();
        
        public static void AddIgnoredSheets(string sheetsName, Location location)
        {
        }
        
        public static void AddIgnoredSheet(string sheetName, Location location)
        {
        }
        
        public static void AddIgnoredField(string fieldName, Location location)
        {
        }

        public static void WriteToFile(string filename)
        {
        }
    }
}
