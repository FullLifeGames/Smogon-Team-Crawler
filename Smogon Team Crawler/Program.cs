using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Smogon_Team_Crawler
{

    // TODO Read teams out of RMT posts

    class Program
    {

        private static bool mainForum = true;
        private static bool rmtForum = true;

        private static WebClient client;

        private static string[] hardCodedBlacklistedPastes = new string[] { "spEtvevT" };

        static void Main(string[] args)
        {
            Dictionary<string, List<Team>> teamsForTiers = new Dictionary<string, List<Team>>();
            Dictionary<string, List<Team>> rmtForTiers = new Dictionary<string, List<Team>>();

            Dictionary<string, string> tierToLinks = new Dictionary<string, string>();
            Dictionary<string, string> tierToRMTLinks = new Dictionary<string, string>();

            client = new WebClient();
            string smogonMain = client.DownloadString("http://www.smogon.com/forums/");
            bool scanStartOne = false;
            bool scanStartTwo = false;

            foreach (string line in smogonMain.Split('\n'))
            {
                if (scanStartOne)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string tierName = line.Substring(line.IndexOf(">") + 1);
                        tierName = tierName.Substring(0, tierName.IndexOf("<"));
                        
                        string tierUrl = line.Substring(line.IndexOf("\"") + 1);
                        tierUrl = tierUrl.Substring(0, tierUrl.IndexOf("\""));
                        tierUrl = "http://www.smogon.com" + tierUrl;

                        tierToLinks.Add(tierName, tierUrl);
                        teamsForTiers.Add(tierName, new List<Team>());
                    }
                    else if (line.Contains("node-stats\""))
                    {
                        scanStartOne = false;
                    }
                }
                else if (scanStartTwo)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string urlName = line.Substring(line.IndexOf(">") + 1);
                        urlName = urlName.Substring(0, urlName.IndexOf("<"));
                        
                        string url = line.Substring(line.IndexOf("\"") + 1);
                        url = url.Substring(0, url.IndexOf("\""));
                        url = "http://www.smogon.com" + url;

                        tierToRMTLinks.Add(urlName, url);
                    }
                    else if (line.Contains("node-stats\""))
                    {
                        scanStartTwo = false;
                        break;
                    }
                }
                else if (line.Contains("Smogon Metagames"))
                {
                    scanStartOne = true;
                }
                else if (line.Contains(">Rate My Team<"))
                {
                    scanStartTwo = true;
                }
            }

            if (mainForum)
            {
                foreach (KeyValuePair<string, string> kv in tierToLinks)
                {
                    string site = client.DownloadString(kv.Value);
                    int pages = 1;
                    if (site.Contains("<nav class=\"pageNavWrapper"))
                    {
                        string temp = site;
                        while (temp.Contains("pageNav-page"))
                        {
                            temp = temp.Substring(temp.IndexOf("pageNav-page") + "pageNav-page".Length);
                        }
                        temp = temp.Substring(temp.IndexOf(">") + 1);
                        temp = temp.Substring(temp.IndexOf(">") + 1);
                        temp = temp.Substring(0, temp.IndexOf("<"));
                        pages = int.Parse(temp);
                    }

                    for (int pageCount = 1; pageCount <= pages; pageCount++)
                    {
                        site = client.DownloadString(kv.Value + "page-" + pageCount);
                        
                        string prefix = null;

                        foreach (string line in site.Split('\n'))
                        {
                            if (line.Contains("<span class=\"label"))
                            {
                                prefix = line.Substring(line.IndexOf("<span class=\"label") + 1);
                                prefix = prefix.Substring(prefix.IndexOf(">") + 1);
                                prefix = prefix.Substring(0, prefix.IndexOf("<"));
                            }
                            else if (line.Contains("data-preview-url"))
                            {
                                string tempInside = line.Substring(line.IndexOf("data-preview-url") + "data-preview-url".Length);
                                tempInside = tempInside.Substring(tempInside.IndexOf("\"") + 1);
                                if (!tempInside.Contains("/preview"))
                                {
                                    continue;
                                }
                                tempInside = tempInside.Substring(0, tempInside.IndexOf("/preview") + 1);
                                string url = "http://www.smogon.com" + tempInside;
                                Console.WriteLine("Currently Scanning: " + url);
                                int beforeCount = teamsForTiers[kv.Key].Count;
                                AnalyzeTopic(url, kv.Key, teamsForTiers, prefix);
                                int afterCount = teamsForTiers[kv.Key].Count;
                                Console.WriteLine("Added " + (afterCount - beforeCount) + " Teams");
                                Console.WriteLine();
                            }
                        }
                    }
                }
            }

            if (rmtForum)
            {
                foreach (KeyValuePair<string, string> kv in tierToRMTLinks)
                {
                    string site = client.DownloadString(kv.Value);
                    int pages = 1;
                    if (site.Contains("<nav class=\"pageNavWrapper"))
                    {
                        string temp = site;
                        while (temp.Contains("pageNav-page"))
                        {
                            temp = temp.Substring(temp.IndexOf("pageNav-page") + "pageNav-page".Length);
                        }
                        temp = temp.Substring(temp.IndexOf(">") + 1);
                        temp = temp.Substring(temp.IndexOf(">") + 1);
                        temp = temp.Substring(0, temp.IndexOf("<"));
                        pages = int.Parse(temp);
                    }

                    for (int pageCount = 1; pageCount <= pages; pageCount++)
                    {
                        site = client.DownloadString(kv.Value + "page-" + pageCount);

                        string prefix = "";

                        foreach (string line in site.Split('\n'))
                        {
                            if (line.Contains("<span class=\"label"))
                            {
                                prefix = line.Substring(line.IndexOf("<span class=\"label") + 1);
                                prefix = prefix.Substring(prefix.IndexOf(">") + 1);
                                prefix = prefix.Substring(0, prefix.IndexOf("<"));
                            }
                            else if (line.Contains("data-preview-url") && prefix != "")
                            {
                                string tempInside = line.Substring(line.IndexOf("data-preview-url") + "data-preview-url".Length);
                                tempInside = tempInside.Substring(tempInside.IndexOf("\"") + 1);
                                if (!tempInside.Contains("/preview"))
                                {
                                    continue;
                                }
                                tempInside = tempInside.Substring(0, tempInside.IndexOf("/preview") + 1);
                                string url = "http://www.smogon.com" + tempInside;
                                Console.WriteLine("Currently Scanning: " + url);
                                if (!rmtForTiers.ContainsKey(prefix))
                                {
                                    rmtForTiers.Add(prefix, new List<Team>());
                                }
                                int beforeCount = rmtForTiers[prefix].Count;
                                AnalyzeRMTTopic(url, prefix, rmtForTiers);
                                int afterCount = rmtForTiers[prefix].Count;
                                Console.WriteLine("Added " + (afterCount - beforeCount) + " Teams");
                                Console.WriteLine();
                                prefix = "";
                            }
                        }
                    }
                }
            }

            string output = "";
            if (mainForum)
            {
                foreach (KeyValuePair<string, List<Team>> kv in teamsForTiers)
                {
                    if (kv.Value.Count == 0)
                    {
                        continue;
                    }

                    output += "Smogon (" + kv.Key + "):\n\n";

                    kv.Value.Sort((t1, t2) => { return t2.Koeffizient.CompareTo(t1.Koeffizient); });
                    List<Team> teamList = RemoveDuplicates(kv.Value);

                    foreach (Team team in teamList)
                    {
                        string lineup = GetTeamLineupString(team.TeamString);
                        team.TeamLineUp = lineup;
                        string outputString = "";
                        List<string> lines = new List<string>();
                        foreach (string monData in team.TeamString.Split(new string[] { "\n\n" }, StringSplitOptions.None))
                        {
                            if (!monData.Contains("\n"))
                            {
                                lines.Add(monData);
                                continue;
                            }
                            string mon = monData.Substring(0, monData.IndexOf("\n"));
                            mon = mon.Replace(":", "");
                            lines.Add(mon + monData.Substring(monData.IndexOf("\n")));
                        }
                        outputString = String.Join("\n\n", lines);
                        output += lineup + ":\n" + team.URL + "\n" + team.PostedBy + "\n" + team.PostDate.ToString() + "\n" + team.Likes + " Likes\n" + team.Koeffizient + " Calculated Value\n\n" + outputString + "\n\n\n";
                    }

                    output += "\n";
                }
            }

            string outputRMT = "";
            if (rmtForum)
            {
                foreach (KeyValuePair<string, List<Team>> kv in rmtForTiers)
                {
                    if (kv.Value.Count == 0)
                    {
                        continue;
                    }

                    outputRMT += "Smogon (" + kv.Key + "):\n\n";

                    kv.Value.Sort((t1, t2) => { return t2.Koeffizient.CompareTo(t1.Koeffizient); });
                    List<Team> teamList = RemoveDuplicates(kv.Value);

                    foreach (Team team in teamList)
                    {
                        string lineup = GetTeamLineupString(team.TeamString);
                        team.TeamLineUp = lineup;
                        string outputString = "";
                        List<string> lines = new List<string>();
                        foreach (string monData in team.TeamString.Split(new string[] { "\n\n" }, StringSplitOptions.None))
                        {
                            if (!monData.Contains("\n"))
                            {
                                lines.Add(monData);
                                continue;
                            }
                            string mon = monData.Substring(0, monData.IndexOf("\n"));
                            mon = mon.Replace(":", "");
                            lines.Add(mon + monData.Substring(monData.IndexOf("\n")));
                        }
                        outputString = String.Join("\n\n", lines);
                        outputRMT += lineup + ":\n" + team.URL + "\n" + team.PostedBy + "\n" + team.PostDate.ToString() + "\n" + team.Likes + " Likes\n" + team.Koeffizient + " Calculated Value\n\n" + outputString + "\n\n\n";
                    }

                    outputRMT += "\n";
                }
            }

            StreamWriter sw;
            if (mainForum)
            {
                sw = new StreamWriter("outputJson.txt");
                sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(teamsForTiers));
                sw.Close();

                sw = new StreamWriter("output.txt");
                sw.Write(output);
                sw.Close();
            }

            if (rmtForum)
            {
                sw = new StreamWriter("outputRMTJson.txt");
                sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(rmtForTiers));
                sw.Close();

                sw = new StreamWriter("outputRMT.txt");
                sw.Write(outputRMT);
                sw.Close();
            }
        }

        private static void AnalyzeRMTTopic(string url, string prefix, Dictionary<string, List<Team>> rmtForTiers)
        {
            try
            {
                string site = client.DownloadString(url);
                int pages = 1;
                if (site.Contains("<nav class=\"pageNavWrapper"))
                {
                    string temp = site;
                    while (temp.Contains("pageNav-page"))
                    {
                        temp = temp.Substring(temp.IndexOf("pageNav-page") + "pageNav-page".Length);
                    }
                    temp = temp.Substring(temp.IndexOf(">") + 1);
                    temp = temp.Substring(temp.IndexOf(">") + 1);
                    temp = temp.Substring(0, temp.IndexOf("<"));
                    pages = int.Parse(temp);
                }

                for (int pageCount = 1; pageCount <= pages; pageCount++)
                {
                    site = client.DownloadString(url + "page-" + pageCount);

                    bool blockStarted = false;
                    string blockText = "";

                    bool postStarted = false;
                    string postLink = "";
                    int postLikes = 0;
                    DateTime postDate = DateTime.Now;

                    string postedBy = "";

                    bool likeStarted = false;

                    bool timerHeader = false;

                    string lastLine = "";

                    List<string> currentTeams = new List<string>();

                    foreach (string line in site.Split('\n'))
                    {
                        HandleLine(url, prefix, rmtForTiers, pageCount, ref blockStarted, ref blockText, ref postStarted, ref postLink, ref postLikes, ref postDate, ref postedBy, ref likeStarted, ref timerHeader, currentTeams, line, ref lastLine, prefix);
                    }
                }
            }
            catch (WebException)
            {
                Console.WriteLine("WebException bei: " + url);
            }
        }

        private static List<Team> RemoveDuplicates(List<Team> value)
        {
            List<Team> uniqueTeams = new List<Team>();
            foreach(Team team in value)
            {
                bool unique = true;
                foreach(Team uniqueTeam in uniqueTeams)
                {
                    if (uniqueTeam.Equals(team))
                    {
                        unique = false;
                        break;
                    }
                }
                if (unique)
                {
                    uniqueTeams.Add(team);
                }
            }
            return uniqueTeams;
        }

        private static string GetTeamLineupString(string teamString)
        {
            List<string> mons = new List<string>();
            foreach (string monData in teamString.Split(new string[] { "\n\n" }, StringSplitOptions.None))
            {
                if(!monData.Contains("\n"))
                {
                    continue;
                }
                string mon = monData.Substring(0, monData.IndexOf("\n"));
                mon = mon.Replace("(M)", "");
                mon = mon.Replace("(F)", "");
                if (mon.Contains("@"))
                {
                    mon = mon.Substring(0, mon.IndexOf("@")).Trim();
                }
                if (mon.Contains("(") && mon.Contains(")"))
                {
                    mon = mon.Substring(mon.IndexOf("(") + 1);
                    mon = mon.Substring(0, mon.IndexOf(")"));
                }
                mons.Add(mon.Trim());
            }

            return String.Join(", ", mons);
        }

        private static void AnalyzeTopic(string url, string tier, Dictionary<string, List<Team>> teamsForTiers, string prefix)
        {
            try
            {
                string site = client.DownloadString(url);
                int pages = 1;
                if (site.Contains("<nav class=\"pageNavWrapper"))
                {
                    string temp = site;
                    while (temp.Contains("pageNav-page"))
                    {
                        temp = temp.Substring(temp.IndexOf("pageNav-page") + "pageNav-page".Length);
                    }
                    temp = temp.Substring(temp.IndexOf(">") + 1);
                    temp = temp.Substring(temp.IndexOf(">") + 1);
                    temp = temp.Substring(0, temp.IndexOf("<"));
                    pages = int.Parse(temp);
                }

                for (int pageCount = 1; pageCount <= pages; pageCount++)
                {
                    site = client.DownloadString(url + "page-" + pageCount);

                    bool blockStarted = false;
                    string blockText = "";

                    bool postStarted = false;
                    string postLink = "";
                    int postLikes = 0;
                    DateTime postDate = DateTime.Now;

                    string postedBy = "";

                    bool likeStarted = false;

                    bool timerHeader = false;

                    string lastLine = "";

                    List<string> currentTeams = new List<string>();

                    foreach (string line in site.Split('\n'))
                    {
                        HandleLine(url, tier, teamsForTiers, pageCount, ref blockStarted, ref blockText, ref postStarted, ref postLink, ref postLikes, ref postDate, ref postedBy, ref likeStarted, ref timerHeader, currentTeams, line, ref lastLine, prefix);
                    }
                }
            }
            catch (WebException e)
            {
                Console.WriteLine("WebException bei: " + url);
                Console.WriteLine(e.Message);
            }
        }

        private static void HandleLine(string url, string tier, Dictionary<string, List<Team>> teamsForTiers, int pageCount, ref bool blockStarted, ref string blockText, ref bool postStarted, ref string postLink, ref int postLikes, ref DateTime postDate, ref string postedBy, ref bool likeStarted, ref bool timerHeader, List<string> currentTeams, string line, ref string lastLine, string prefix)
        {
            if (!postStarted)
            {
                if (line.Contains("<article class=\"message message--post js-post js-inlineModContainer"))
                {
                    postStarted = true;
                }
            }
            else if (line.Contains("data-author=\""))
            {
                postedBy = line.Substring(line.IndexOf("data-author") + 5);
                postedBy = postedBy.Substring(postedBy.IndexOf("\"") + 1);
                postedBy = postedBy.Substring(0, postedBy.IndexOf("\""));
            }
            else if (line.Contains("data-content=\""))
            {
                postLink = line.Substring(line.IndexOf("data-content=\"") + 5);
                postLink = postLink.Substring(postLink.IndexOf("\"") + 1);
                postLink = postLink.Substring(0, postLink.IndexOf("\""));
            }
            else if (line.StartsWith("\t</article>"))
            {
                postStarted = false;
                foreach (string team in currentTeams)
                {
                    string tmpTeam = team;
                    bool moreTeams = false;
                    while (tmpTeam.Contains("==="))
                    {
                        moreTeams = true;
                        string teamLine = tmpTeam.Substring(tmpTeam.IndexOf("===") + "===".Length);
                        if (!teamLine.Contains("\n"))
                        {
                            break;
                        }
                        teamLine = teamLine.Substring(0, teamLine.IndexOf("\n"));

                        string teamTier = null;
                        if (teamLine.Contains("[") && teamLine.Contains("]"))
                        {
                            teamTier = teamLine.Substring(teamLine.IndexOf("[") + 1, teamLine.IndexOf("]") - teamLine.IndexOf("[") - 1);
                            teamLine = teamLine.Substring(teamLine.IndexOf("]") + 1);
                        }

                        string teamTitle = teamLine.Substring(0, teamLine.IndexOf("===")).Trim();

                        string fullTempTeam = tmpTeam.Substring(tmpTeam.IndexOf("===") + "===".Length);
                        fullTempTeam = fullTempTeam.Substring(fullTempTeam.IndexOf("\n") + 1);

                        if (fullTempTeam.Contains("==="))
                        {
                            fullTempTeam = fullTempTeam.Substring(0, fullTempTeam.IndexOf("==="));
                        }

                        Team teamObject = new Team(fullTempTeam, postLikes, postDate, url + "page-" + pageCount + "#" + postLink, postedBy, prefix);
                        teamObject.TeamTier = teamTier;
                        teamObject.TeamTitle = teamTitle;
                        teamsForTiers[tier].Add(teamObject);

                        tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("===") + "===".Length);
                        tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("\n") + 1);

                        if (tmpTeam.Contains("==="))
                        {
                            tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("==="));
                        }
                    }
                    if (!moreTeams)
                    {
                        Team teamObject = new Team(team, postLikes, postDate, url + "page-" + pageCount + "#" + postLink, postedBy, prefix);
                        teamsForTiers[tier].Add(teamObject);
                    }
                }
                currentTeams.Clear();
                postLikes = 0;
                blockText = "";
                postedBy = "";
                postDate = DateTime.Now;
            }
            else if (line.Contains("<header class=\"message-attribution\">"))
            {
                timerHeader = true;
            }
            else if (line.Contains("data-date-string=\"") && timerHeader)
            {
                string temp = line.Substring(line.IndexOf("data-date-string=\"") + "data-date-string=\"".Length);
                temp = temp.Substring(temp.IndexOf("title") + "title".Length);
                temp = temp.Substring(temp.IndexOf("\"") + 1);
                temp = temp.Substring(0, temp.IndexOf("\""));
                temp = temp.Replace("at ", "");
                postDate = DateTime.ParseExact(temp, "MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                timerHeader = false;
            }
            // Maybe unnecessary due to the Smogon redesign
            /*        
                   else if (line.Contains("<span class=\"DateTime\""))
                    {
                        string temp = line.Substring(line.IndexOf("<span class=\"DateTime\"") + "<span class=\"DateTime\"".Length);
                        temp = temp.Substring(temp.IndexOf("title=\"") + "title=\"".Length);
                        temp = temp.Substring(0, temp.IndexOf("\""));
                        temp = temp.Replace("at ", "");
                        postDate = DateTime.ParseExact(temp, "MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                    }
            */
            else if (line.Contains("<span class=\"u-srOnly\">Likes:</span>"))
            {
                likeStarted = true;
            }
            else if (likeStarted)
            {
                likeStarted = false;
                int likes = CountOccurences(line, "</bdi>");
                if (line.Contains(" others</a>"))
                {
                    string tempLikeString = line;
                    while (tempLikeString.Contains("</bdi>"))
                    {
                        tempLikeString = tempLikeString.Substring(tempLikeString.IndexOf("</bdi>") + "</bdi>".Length);
                    }
                    tempLikeString = tempLikeString.Substring(tempLikeString.IndexOf("and") + "and".Length).Trim();
                    tempLikeString = tempLikeString.Substring(0, tempLikeString.IndexOf(" "));
                    likes += int.Parse(tempLikeString);
                }
                postLikes = likes;
            }
            else if (line.Contains("<div class=\"bbCodeBlock-content\">"))
            {
                blockStarted = true;
                blockText = "";
                string temp = line.Substring(line.IndexOf("<div class=\"bbCodeBlock-content\">") + "<div class=\"bbCodeBlock-content\">".Length).Trim();
                if (temp != "<br />" && temp != "")
                {
                    blockText += temp + "\n";
                }
            }
            else if (blockStarted && ((line.Trim().Replace("\t", "").Contains("</div>") && lastLine.Trim().Replace("\t", "").Contains("</div>")) || line.Contains("</div></div>")))
            {
                blockStarted = false;
                blockText = blockText.Replace("\t", "");
                blockText = blockText.Replace("<br />", "");
                blockText = blockText.Replace("</div>", "");
                if (IsTeam(blockText))
                {
                    currentTeams.Add(blockText);
                }
            }
            else if (blockStarted && (!line.Trim().StartsWith("<") || line.StartsWith("<br />")) && !line.Contains(" -- "))
            {
                blockText += line + "\n";
            }
            if (postStarted)
            {
                if (line.Contains("pokepast.es/"))
                {
                    string pasteUrl = line.Substring(line.IndexOf("pokepast.es/"));
                    if (pasteUrl.Contains(" ") || pasteUrl.Contains("\"") || pasteUrl.Contains("<"))
                    {
                        int nearest = int.MaxValue;
                        int space = pasteUrl.IndexOf(" ");
                        int quotation = pasteUrl.IndexOf("\"");
                        int arrow = pasteUrl.IndexOf("<");

                        foreach (int pos in new int[] { space, quotation, arrow })
                        {
                            if (pos != -1 && pos < nearest)
                            {
                                nearest = pos;
                            }
                        }

                        pasteUrl = pasteUrl.Substring(0, nearest);
                    }
                    pasteUrl = "http://" + pasteUrl;
                    currentTeams.Add(GetTeamFromPokepasteURL(pasteUrl));
                }
                if (line.Contains("pastebin.com/"))
                {
                    string pasteUrl = line.Substring(line.IndexOf("pastebin.com/"));
                    if (pasteUrl.Contains(" ") || pasteUrl.Contains("\"") || pasteUrl.Contains("<"))
                    {
                        int nearest = int.MaxValue;
                        int space = pasteUrl.IndexOf(" ");
                        int quotation = pasteUrl.IndexOf("\"");
                        int arrow = pasteUrl.IndexOf("<");

                        foreach (int pos in new int[] { space, quotation, arrow })
                        {
                            if (pos != -1 && pos < nearest)
                            {
                                nearest = pos;
                            }
                        }

                        pasteUrl = pasteUrl.Substring(0, nearest);
                    }
                    if (pasteUrl.Contains("/raw/"))
                    {
                        pasteUrl = "https://" + pasteUrl;
                    }
                    else
                    {
                        pasteUrl = "https://pastebin.com/raw/" + pasteUrl.Substring(pasteUrl.IndexOf("/") + 1);
                    }
                    string pasteString = GetTeamFromPastebinURL(pasteUrl);
                    if (IsTeam(pasteString))
                    {
                        bool blackListed = false;
                        foreach(string urlPart in hardCodedBlacklistedPastes)
                        {
                            if (pasteUrl.Contains(urlPart))
                            {
                                blackListed = true;
                            }
                        }
                        if (!blackListed)
                        {
                            currentTeams.Add(pasteString);
                        }
                    }
                }
                if (line.Contains("hastebin.com/"))
                {
                    string pasteUrl = line.Substring(line.IndexOf("hastebin.com/"));
                    if (pasteUrl.Contains(" ") || pasteUrl.Contains("\"") || pasteUrl.Contains("<"))
                    {
                        int nearest = int.MaxValue;
                        int space = pasteUrl.IndexOf(" ");
                        int quotation = pasteUrl.IndexOf("\"");
                        int arrow = pasteUrl.IndexOf("<");

                        foreach (int pos in new int[] { space, quotation, arrow })
                        {
                            if (pos != -1 && pos < nearest)
                            {
                                nearest = pos;
                            }
                        }

                        pasteUrl = pasteUrl.Substring(0, nearest);
                    }
                    if (pasteUrl.Contains("/raw/"))
                    {
                        pasteUrl = "https://" + pasteUrl;
                    }
                    else
                    {
                        pasteUrl = "https://hastebin.com/raw/" + pasteUrl.Substring(pasteUrl.IndexOf("/") + 1);
                    }
                    string pasteString = GetTeamFromPastebinURL(pasteUrl);
                    if (IsTeam(pasteString))
                    {
                        currentTeams.Add(pasteString);
                    }
                }
            }
            lastLine = line;
        }

        private static string GetTeamFromPokepasteURL(string pasteUrl)
        {
            string site = "";
            try
            {
                site = client.DownloadString(pasteUrl);
            }
            catch (WebException)
            { }

            string team = "";

            bool addLines = false;

            foreach (string line in site.Split('\n'))
            {
                if (line.Contains("<pre>"))
                {
                    addLines = true;
                }
                else if (line.Contains("</pre>"))
                {
                    addLines = false;
                }
                if (addLines)
                {
                    team += line + "\n";
                }
            }

            team = Regex.Replace(team, "<.*?>", String.Empty);

            return team;
        }

        private static string GetTeamFromPastebinURL(string pasteUrl)
        {
            string site = "";
            try
            {
                site = client.DownloadString(pasteUrl);
            }
            catch (WebException)
            {}

            string team = Regex.Replace(site, "<.*?>", String.Empty);

            return team;
        }
        
        private static Regex doubleRowRegex = new Regex(@"\n\n");

        private static bool IsTeam(string blockText)
        {
            string[] split = doubleRowRegex.Split(blockText.Replace("\r", ""));
            int countFullMoves = 0;
            foreach(string mon in split)
            {
                if(CountOccurences(mon, "\n- ") >= 4)
                {
                    countFullMoves++;
                }
            }
            return countFullMoves >= 6 || (CountOccurences(blockText, "EVs: ") >= 6 && CountOccurences(blockText, "Nature") >= 6 && CountOccurences(blockText, "Ability: ") >= 6);
        }

        private static int CountOccurences(string haystack, string needle)
        {
            return (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
        }
    }
}
