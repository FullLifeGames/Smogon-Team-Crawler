using NeoSmart.Caching.Sqlite;
using Newtonsoft.Json;

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

var crawlResult = await teamCrawler.CrawlAsync(crawlRequest).ConfigureAwait(false);

foreach (var outputs in crawlResult.TeamsByTier)
{
    var tempImportable = outputs.Value.Replace("\n", "\r\n");
    var tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

    await File.WriteAllTextAsync(
        tempTier.Replace("[", "").Replace("]", "") + ".txt",
        tempImportable
    ).ConfigureAwait(false);
}

foreach (var outputs in crawlResult.CreatedTeamsByTiers)
{
    var tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

    // serialize JSON directly to a file
    using (var file = File.CreateText(tempTier.Replace("[", "").Replace("]", "") + ".json"))
    {
        var serializer = new JsonSerializer();
        serializer.Serialize(file, outputs.Value);
    }
}

// serialize JSON directly to a file
using (var file = File.CreateText("finalJson.txt"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, crawlResult.TeamsByTier);
}

// serialize JSON directly to a file
using (var file = File.CreateText("final.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, crawlResult.TeamsByTier);
}

// serialize JSON directly to a file
using (var file = File.CreateText("finalCollected.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, crawlResult.CreatedTeamsByTiers);
}
