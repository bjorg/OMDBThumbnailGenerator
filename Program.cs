using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PosterFinder {

    public class Record {

        //--- Properties ---
        public string Title { get; set; }
        public string Year { get; set; }
        public string Rated { get; set; }
        public string Poster { get; set; }
    }

    public class Program {

        //--- Class Fields ---
        private static HttpClient _httpClient = new HttpClient();
        private static Regex _title = new Regex(@"^(.+)\(([0-9]+)\).*\.iso$");

        //--- Class Methods ---
        public static async Task Main(string[] arguments) {

            // check if OMDB API key is set as environment variable
            var apikey = Environment.GetEnvironmentVariable("OMDBAPIKEY");
            if(string.IsNullOrEmpty(apikey)) {
                Console.Error.WriteLine("ERROR: OMDBAPIKEY environment variable is not set");
                return;
            }

            // check if a command line argument was supplied
            if(arguments.Length > 0) {

                // check if argument is a URI to an image
                if(Uri.TryCreate(arguments[0], UriKind.Absolute, out var _)) {
                    try {
                        await GenerateImageFromUriAsync(arguments[0], "thumbnail.jpg");
                    } catch(Exception e) {
                        Console.WriteLine($"ERROR: thumbnail generation failed ({e.Message})");
                        return;
                    }
                } else {
                    await FindFiles(apikey, arguments[0]);
                }
            } else {
                await FindFiles(apikey, Directory.GetCurrentDirectory());
            }
        }

        private static async Task FindFiles(string apikey, string path) {
            Console.WriteLine($"Scanning: {path}");
            var entries = Directory.GetFiles(path, "*.iso", SearchOption.AllDirectories)
                .Select(file => new {
                    Original = Path.GetRelativePath(path, file),
                    FileName = Path.GetFileName(file),
                    Thumbnail = Path.ChangeExtension(file, ".jpg")
                })
                .Where(entry => !File.Exists(entry.Thumbnail))
                .ToArray();
            Console.WriteLine($"Found {entries.Length:N0} files with missing thumbnails");
            var errorCount = 0;
            foreach(var entry in entries) {

                // parse filename
                var match = _title.Match(entry.FileName);
                if(!match.Success) {
                    Console.WriteLine($"Skipping '{entry.Original}'. Unable to parse movie title and year from filename.");
                    continue;
                }
                var title = new string(match.Groups[1].Value.Select(c => (char.IsLetterOrDigit(c) || (c == '\'')) ? c : ' ').ToArray()).Trim();
                var year = match.Groups[2].Value.Trim();

                // retrieve movie information
                Console.WriteLine($"Looking up '{title}' ({year})...");
                Record record;
                try {
                    var uri = new UriBuilder("https://www.omdbapi.com/");
                    uri.Query += $"?apikey={apikey}";
                    uri.Query += $"&t={Uri.EscapeUriString(title)}";
                    uri.Query += $"&y={Uri.EscapeUriString(year)}";
                    var response = await _httpClient.GetAsync(uri.ToString());
                    record = JsonConvert.DeserializeObject<Record>(await response.Content.ReadAsStringAsync());
                } catch {
                    Console.WriteLine("WARN: no movie record found");
                    ++errorCount;
                    continue;
                }

                // check if record has a thumbnail
                if(string.IsNullOrEmpty(record.Poster) || (record.Poster == "N/A")) {
                    Console.WriteLine("WARN: no thumbnail found");
                    continue;
                }

                // we really don't want to be overwriting anything!
                if(File.Exists(entry.Thumbnail)) {
                    continue;
                }

                // generate thumbnail from poster
                try {
                    await GenerateImageFromUriAsync(record.Poster, entry.Thumbnail);
                } catch (Exception e) {
                    Console.WriteLine($"WARN: thumbnail generation failed ({e.Message})");
                    ++errorCount;
                    continue;
                }
            }
        }

        private static async Task GenerateImageFromUriAsync(string uri, string filename) {

                // download poster
                var response = await _httpClient.GetAsync(uri);
                var stream = await response.Content.ReadAsStreamAsync();

                // resize poster to thumbnail
                using(var image = Image.Load(stream)) {
                    image.Mutate(context => context.Resize(new ResizeOptions {
                        Mode = ResizeMode.Pad,
                        Size = new SixLabors.Primitives.Size(600, 600)
                    }));
                    image.Save(filename);
                }
        }
    }
}
