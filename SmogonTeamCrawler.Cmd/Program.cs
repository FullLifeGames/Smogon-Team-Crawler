using NeoSmart.Caching.Sqlite;
using Newtonsoft.Json;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Util;
using System.Globalization;

const string Usage = """
    Usage: SmogonTeamCrawler.Cmd [--full] [--delay <seconds>] [--recent-days <days>]

      --full                Ignore the stored state and read every listing and thread page again.
      --delay <seconds>     Pause between two requests to the same host (default 2).
      --recent-days <days>  When a thread has new posts, also re-read its pages with posts younger
                            than this, to refresh their likes (default 7, 0 turns it off).

    The crawl state is kept in SmogonDump.db. After the first run, only new activity is read.
    """;

var collectorOptions = new CollectorOptions();
var httpOptions = new PoliteHttpOptions();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--full":
            collectorOptions.FullRefresh = true;
            break;
        case "--delay" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0:
            httpOptions.MinDelay = TimeSpan.FromSeconds(seconds);
            i++;
            break;
        case "--recent-days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var days) && days >= 0:
            collectorOptions.RecentPageRefreshWindow = TimeSpan.FromDays(days);
            i++;
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return 64;
    }
}

var httpHandler = new PoliteHttpMessageHandler(httpOptions);
Common.HttpClient = PoliteHttpMessageHandler.CreateClient(httpHandler);
collectorOptions.HttpClient = Common.HttpClient;

try
{
    // Configure memory optimization for Linux environments
    var folderPath = "Teams/";
    Directory.CreateDirectory(folderPath);

    var teamCrawler = new SmogonTeamCrawler.Core.Crawler.SmogonTeamCrawler(
        new SqliteCache(
            new SqliteCacheOptions()
            {
                MemoryOnly = false,
                CachePath = "SmogonDump.db",
            }
        ),
        collectorOptions
    );

    var crawlRequest = new SmogonTeamCrawler.Core.Data.CrawlRequest()
    {
        MainForum = true,
        RMTForum = true,
    };

    Console.WriteLine("Starting team crawl...");
    var crawlResult = await teamCrawler.CrawlAsync(crawlRequest).ConfigureAwait(false);
    
    // Force garbage collection after crawling to free memory
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine($"Memory after crawl: {GC.GetTotalMemory(false) / (1024 * 1024)}MB");

    Console.WriteLine("Writing team data to files...");
    foreach (var outputs in crawlResult.TeamsByTier)
    {
        var tempImportable = outputs.Value.Replace("\n", "\r\n");
        var tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

        var localFilePath = folderPath + tempTier.Replace("[", "").Replace("]", "") + ".txt";
        Console.WriteLine($"Write {localFilePath}");
        await File.WriteAllTextAsync(
            localFilePath,
            tempImportable
        ).ConfigureAwait(false);
    }

    // Force garbage collection after writing team strings
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine($"Memory after tier files: {GC.GetTotalMemory(false) / (1024 * 1024)}MB");

    foreach (var outputs in crawlResult.CreatedTeamsByTiers)
    {
        var tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

        // serialize JSON directly to a file with streaming to reduce memory
        var localFilePath = folderPath + tempTier.Replace("[", "").Replace("]", "") + ".json";
        Console.WriteLine($"Write {localFilePath}");
        using (var file = File.CreateText(localFilePath))
        {
            var serializer = new JsonSerializer();
            serializer.Serialize(file, outputs.Value);
        }
    }

    // serialize JSON directly to a file
    var filePath = folderPath + "finalJson.txt";
    Console.WriteLine($"Write {filePath}");
    using (var file = File.CreateText(filePath))
    {
        var serializer = new JsonSerializer();
        serializer.Serialize(file, crawlResult.TeamsByTier);
    }

    // serialize JSON directly to a file
    filePath = folderPath + "final.json";
    Console.WriteLine($"Write {filePath}");
    using (var file = File.CreateText(filePath))
    {
        var serializer = new JsonSerializer();
        serializer.Serialize(file, crawlResult.TeamsByTier);
    }

    // serialize JSON directly to a file
    filePath = folderPath + "finalCollected.json";
    Console.WriteLine($"Write {filePath}");
    using (var file = File.CreateText(filePath))
    {
        var serializer = new JsonSerializer();
        serializer.Serialize(file, crawlResult.CreatedTeamsByTiers);
    }
    
    // Final GC
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine("Crawl completed successfully!");
    return 0;
}
catch (Exception e) when (e is SmogonAccessBlockedException or UnexpectedPageException)
{
    // Threads read before the stop are stored; the next run continues from there. Team files were not touched.
    Console.Error.WriteLine(e.Message);
    return 2;
}
catch (Exception e)
{
    Console.WriteLine($"Unknown error occured during execution: {e.Message}");
    Console.WriteLine(e.StackTrace);
    return 1;
}
finally
{
    Console.WriteLine(httpHandler.Statistics);
}
