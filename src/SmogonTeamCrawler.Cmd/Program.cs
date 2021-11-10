var teamCrawler = new SmogonTeamCrawler.Core.Crawler.SmogonTeamCrawler();
var crawlRequest = new SmogonTeamCrawler.Core.Data.CrawlRequest()
{
    MainForum = true,
    RMTForum = true,
};
var crawlResult = await teamCrawler.CrawlAsync(crawlRequest);

StreamWriter sw;
if (crawlRequest.MainForum)
{
    sw = new StreamWriter("outputJson.txt");
    sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.SmogonTeams));
    sw.Close();

    sw = new StreamWriter("output.txt");
    sw.Write(crawlResult.SmogonOutput);
    sw.Close();
}

if (crawlRequest.RMTForum)
{
    sw = new StreamWriter("outputRMTJson.txt");
    sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.Rmts));
    sw.Close();

    sw = new StreamWriter("outputRMT.txt");
    sw.Write(crawlResult.RmtsOutput);
    sw.Close();
}

foreach (KeyValuePair<string, string> outputs in crawlResult.TeamsByTier)
{
    var tempImportable = outputs.Value.Replace("\n", "\r\n");
    var tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

    sw = new StreamWriter(tempTier.Replace("[", "").Replace("]", "") + ".txt");
    sw.Write(tempImportable);
    sw.Close();
}

sw = new StreamWriter("finalJson.txt");
sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(crawlResult.TeamsByTier));
sw.Close();