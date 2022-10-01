var teamCrawler = new SmogonTeamCrawler.Core.Crawler.SmogonTeamCrawler();
var crawlRequest = new SmogonTeamCrawler.Core.Data.CrawlRequest()
{
    MainForum = true,
    RMTForum = true,
};
var crawlResult = await teamCrawler.CrawlAsync(crawlRequest).ConfigureAwait(false);

if (crawlRequest.MainForum)
{
    await File.WriteAllTextAsync(
        "outputJson.txt",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.SmogonTeams)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "output.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.SmogonTeams)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "output.txt",
        crawlResult.SmogonOutput
    ).ConfigureAwait(false);
}

if (crawlRequest.RMTForum)
{
    await File.WriteAllTextAsync(
        "outputRMTJson.txt",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.Rmts)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "outputRMT.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.Rmts)
    ).ConfigureAwait(false);

    await File.WriteAllTextAsync(
        "outputRMT.txt",
        crawlResult.RmtsOutput
    ).ConfigureAwait(false);
}

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
