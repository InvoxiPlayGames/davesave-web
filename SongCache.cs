using System.Security.Cryptography.X509Certificates;

namespace davesave_web
{
    public class SongBinaryFormat
    {
#pragma warning disable CS8618
        public int SongID;
        public string Shortname;
        public string Artist;
        public string Title;
        public string[] Sources;
#pragma warning restore CS8618

        public static SongBinaryFormat ReadFromStream(Stream stream)
        {
            SongBinaryFormat sbf = new();

            sbf.SongID = stream.ReadInt32LE();
            sbf.Shortname = stream.ReadLengthUTF8();
            sbf.Artist = stream.ReadLengthUTF8();
            sbf.Title = stream.ReadLengthUTF8();
            int sourcesLen = stream.ReadInt32LE();
            sbf.Sources = new string[sourcesLen];
            for (int i = 0; i < sourcesLen; i++)
                sbf.Sources[i] = stream.ReadLengthUTF8();

            return sbf;
        }

        public void WriteToStream(Stream stream)
        {
            stream.WriteInt32LE(SongID);
            stream.WriteLengthUTF8(Shortname);
            stream.WriteLengthUTF8(Artist);
            stream.WriteLengthUTF8(Title);
            stream.WriteInt32LE(Sources.Length);
            for (int i = 0; i < Sources.Length; i++)
                stream.WriteLengthUTF8(Sources[i]);
        }
    }

    public class SongCache
    {
        static public Dictionary<int, SongBinaryFormat>? database;
        static public Dictionary<string, int>? shortnameLookup;
        static public bool hasLoaded = false;

        private readonly BrowserLocalStorage localStorage;
        private readonly HttpClient http;

        public SongCache(BrowserLocalStorage localStorage, HttpClient http)
        {
            this.localStorage = localStorage;
            this.http = http;
        }

        public bool HasDatabase()
        {
            return hasLoaded;
        }

        public async Task ReadDatabase()
        {
            // don't bother reading if we already have a database
            if (database != null)
                return;
            // fetch the response
            HttpResponseMessage resp = await http.GetAsync("songCache.bin");
            if (resp.IsSuccessStatusCode)
            {
                Stream str = resp.Content.ReadAsStream();
                int version = str.ReadInt32LE();
                if (version != 1)
                {
                    Console.WriteLine("Invalid version, not loading a database...");
                    return;
                }
                int songCount = str.ReadInt32LE();
                database = new Dictionary<int, SongBinaryFormat>();
                shortnameLookup = new Dictionary<string, int>();
                for (int i = 0; i < songCount; i++)
                {
                    SongBinaryFormat sbf = SongBinaryFormat.ReadFromStream(str);
                    shortnameLookup.Add(sbf.Shortname, sbf.SongID);
                    database.Add(sbf.SongID, sbf);
                }
                Console.WriteLine("Loaded {0} songs", database.Count);
                hasLoaded = true;
            }
        }

        public SongBinaryFormat? GetSongInfo(int songID)
        {
            if (database == null)
                return null;
            if (!database.ContainsKey(songID))
                return null;
            return database[songID];
        }

        public SongBinaryFormat? GetSongInfo(string shortname)
        {
            if (database == null || shortnameLookup == null)
                return null;
            if (!shortnameLookup.ContainsKey(shortname))
                return null;
            int songID = shortnameLookup[shortname];
            return database[songID];
        }

        public int[]? GetDiscSongs()
        {
            if (database == null)
                return null;
            var results = database.Values.Where((s) => { return s.Sources.Length == 1 && s.Sources[0] == "rb4"; });
            List<int> list = new();
            foreach(SongBinaryFormat sng in results)
            {
                list.Add(sng.SongID);
            }
            return list.ToArray();
        }
    }
}
