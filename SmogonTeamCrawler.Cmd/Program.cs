using NeoSmart.Caching.Sqlite;

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

    await File.WriteAllTextAsync(
        tempTier.Replace("[", "").Replace("]", "") + ".json",
        Newtonsoft.Json.JsonConvert.SerializeObject(outputs.Value)
    ).ConfigureAwait(false);
}

var dumpEverything = false;
if (dumpEverything)
{
    await File.WriteAllTextAsync(
        "finalJson.txt",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.TeamsByTier)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "final.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.TeamsByTier)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "finalCollected.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.CreatedTeamsByTiers)
    ).ConfigureAwait(false);
}
