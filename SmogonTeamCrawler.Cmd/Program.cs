using NeoSmart.Caching.Sqlite;
using Newtonsoft.Json;

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
        )
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
}
catch (Exception e)
{
    Console.WriteLine($"Unknown error occured during execution: {e.Message}");
    Console.WriteLine(e.StackTrace);
}
