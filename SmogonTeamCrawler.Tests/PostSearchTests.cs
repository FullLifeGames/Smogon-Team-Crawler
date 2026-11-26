#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class PostSearchTests
    {
        /// <summary>Result items as Smogon renders them (shortened): a post, a thread, a profile post, a thread without slug.</summary>
        private const string ResultPage = """
            <html id="XF" data-template="search_results">
            <ol class="block-body">
            	<li class="block-row block-row--separated  js-inlineModContainer" data-author="Haok">
            	<div class="contentRow ">
            		<div class="contentRow-main">
            			<h3 class="contentRow-title">
            				<a href="/forums/threads/national-dex-uu-team-bazaar.3712239/post-9428584"><span class="label label--blue" dir="auto">Projects</span><span class="label-append">&nbsp;</span>National Dex UU Team Bazaar</a>
            			</h3>
            			<div class="contentRow-snippet">https://<em class="textHighlight">pokepast.es</em>/47e07d44a1132f67</div>
            			<div class="contentRow-minor contentRow-minor--hideLinks">
            				<ul class="listInline listInline--bullet">
            					<li><a href="/forums/members/haok.487206/" class="username " dir="auto" data-user-id="487206">Haok</a></li>
            					<li>Post #1,207</li>
            					<li><time  class="u-dt" dir="auto" datetime="2022-12-09T03:53:17Z" data-timestamp="1670557997" data-date="Dec 8, 2022" data-time="10:53 PM" title="Dec 8, 2022 at 10:53 PM">Dec 8, 2022</time></li>
            					<li>Forum: <a href="/forums/forums/national-dex-uu.859/">National Dex UU</a></li>
            				</ul>
            			</div>
            		</div>
            	</div>
            </li>
            	<li class="block-row block-row--separated  js-inlineModContainer" data-author="RauSakura">
            	<div class="contentRow ">
            		<div class="contentRow-main">
            			<h3 class="contentRow-title">
            				<a href="/forums/threads/vgc-fall-seasonal-2026-round-3.3791117/"><span class="label label--blue" dir="auto">VGC &amp; More</span><span class="label-append">&nbsp;</span>VGC Fall Seasonal 2026 - Round 3</a>
            			</h3>
            			<div class="contentRow-minor contentRow-minor--hideLinks">
            				<ul class="listInline listInline--bullet">
            					<li>Thread</li>
            					<li><time  class="u-dt" dir="auto" datetime="2026-11-09T23:08:13Z" data-timestamp="1794265693" title="Nov 9, 2026 at 6:08 PM">Monday at 6:08 PM</time></li>
            					<li>Replies: 19</li>
            					<li>Forum: <a href="/forums/forums/other-circuits.473/">Other Circuits</a></li>
            				</ul>
            			</div>
            		</div>
            	</div>
            </li>
            	<li class="block-row block-row--separated  js-inlineModContainer" data-author="Someone">
            	<div class="contentRow ">
            		<div class="contentRow-main">
            			<h3 class="contentRow-title">
            				<a href="/forums/profile-posts/123456/">Profile post by Someone for Other</a>
            			</h3>
            			<div class="contentRow-minor contentRow-minor--hideLinks">
            				<ul class="listInline listInline--bullet">
            					<li><time  class="u-dt" dir="auto" datetime="2026-11-09T22:00:00Z" data-timestamp="1794261600" title="Nov 9, 2026 at 5:00 PM">Monday at 5:00 PM</time></li>
            				</ul>
            			</div>
            		</div>
            	</div>
            </li>
            	<li class="block-row block-row--separated  js-inlineModContainer" data-author="Other">
            	<div class="contentRow ">
            		<div class="contentRow-main">
            			<h3 class="contentRow-title">
            				<a href="/forums/threads/3791200/post-11190000">!!!</a>
            			</h3>
            			<div class="contentRow-minor contentRow-minor--hideLinks">
            				<ul class="listInline listInline--bullet">
            					<li>Post #3</li>
            					<li><time  class="u-dt" dir="auto" datetime="2026-11-09T21:00:00Z" data-timestamp="1794258000" title="Nov 9, 2026 at 4:00 PM">Monday at 4:00 PM</time></li>
            					<li>Forum: <a href="/forums/forums/overused.726/">OU</a></li>
            				</ul>
            			</div>
            		</div>
            	</div>
            </li>
            </ol>
            <div class="pageNav  pageNav--skipEnd">
            	<a href="/forums/search/72803883/?page=2&amp;q=pokepast.es&amp;c[newer_than]=1793491200&amp;o=date" class="pageNav-jump pageNav-jump--next">Next</a>
            </div>
            </html>
            """;

        [Test]
        public void Parse_ReadsPostsAndThreads_AndSkipsProfilePosts()
        {
            var hits = SearchResultParser.Parse(ResultPage, out var next);

            Assert.That(next, Is.EqualTo("https://www.smogon.com/forums/search/72803883/?page=2&q=pokepast.es&c[newer_than]=1793491200&o=date"));
            Assert.That(hits, Is.EqualTo(new[]
            {
                new SearchHit(3712239, "https://www.smogon.com/forums/threads/national-dex-uu-team-bazaar.3712239/", 9428584, 1207,
                    new DateTime(2022, 12, 9, 3, 53, 17, DateTimeKind.Utc), "https://www.smogon.com/forums/forums/national-dex-uu.859/", "Projects"),
                new SearchHit(3791117, "https://www.smogon.com/forums/threads/vgc-fall-seasonal-2026-round-3.3791117/", null, 1,
                    new DateTime(2026, 11, 9, 23, 8, 13, DateTimeKind.Utc), "https://www.smogon.com/forums/forums/other-circuits.473/", "VGC & More"),
                new SearchHit(3791200, "https://www.smogon.com/forums/threads/3791200/", 11190000, 3,
                    new DateTime(2026, 11, 9, 21, 0, 0, DateTimeKind.Utc), "https://www.smogon.com/forums/forums/overused.726/", ""),
            }));
            Assert.That(hits.Select(hit => hit.Page), Is.EqualTo(new[] { 49, 1, 1 }));
        }

        [Test]
        public void Parse_ReadsTheResultsOf2026_11_26()
        {
            var hits = SearchResultParser.Parse(SavedPages.SearchResults, out var next);

            Assert.That(next, Is.Null);
            Assert.That(hits, Is.EqualTo(new[]
            {
                new SearchHit(3712239, "https://www.smogon.com/forums/threads/national-dex-uu-team-bazaar.3712239/", 11201488, 103,
                    new DateTime(2026, 11, 24, 6, 31, 14, DateTimeKind.Utc), "https://www.smogon.com/forums/forums/national-dex-uu.859/", "Projects"),
                new SearchHit(3732644, "https://www.smogon.com/forums/threads/sv-ou-metagame-discussion-v4.3732644/", 11201487, 22242,
                    new DateTime(2026, 11, 24, 6, 30, 21, DateTimeKind.Utc), "https://www.smogon.com/forums/forums/overused.726/", "Metagame"),
            }));
        }

        [Test]
        public void Parse_ForumWithoutIdInItsUrl()
        {
            var hits = SearchResultParser.Parse(ResultPage.Replace("/forums/forums/overused.726/", "/forums/forums/gen1/"), out _);

            Assert.That(hits[^1].ForumUrl, Is.EqualTo("https://www.smogon.com/forums/forums/gen1/"));
        }

        [Test]
        public void Parse_WithoutResults_IsEmpty()
        {
            Assert.That(SearchResultParser.Parse("<html data-template=\"search_results\"><div class=\"blockMessage\">No results found.</div></html>", out var next), Is.Empty);
            Assert.That(next, Is.Null);
        }

        [Test]
        public async Task Search_WaitsOutFloodControl()
        {
            var answers = new Queue<string>(new[]
            {
                "<html data-template=\"error\"><div class=\"blockMessage\">You must wait at least 4 seconds before performing this action.</div></html>",
                ResultPage.Replace("class=\"pageNav-jump pageNav-jump--next\"", ""),
            });
            var waits = new List<TimeSpan>();
            var search = new PostSearch(_ => Task.FromResult(answers.Dequeue()), wait => { waits.Add(wait); return Task.CompletedTask; });

            var result = await search.SearchAsync(new[] { "pokepast.es" }, new long[] { 859 }, new DateTime(2026, 11, 1));

            Assert.That(result!.Hits, Has.Count.EqualTo(3));
            Assert.That(result.Uncovered, Is.Empty);
            Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromSeconds(5) }));
        }

        [Test]
        public async Task Search_LogsThePostsOfEachQuery_AndOfAll()
        {
            // The first window shows 20 pages (cut off), the window before its oldest day the rest.
            var lastPage = ResultPage.Replace("class=\"pageNav-jump pageNav-jump--next\"", "");
            var search = new PostSearch(url => Task.FromResult(url.Contains("c[older_than]") ? lastPage : ResultPage));
            var output = new System.IO.StringWriter();
            var console = Console.Out;
            Console.SetOut(output);
            try
            {
                // Both queries find the same 3 posts.
                await search.SearchAsync(new[] { "pokepast.es", "Nature (Adamant | Jolly | Impish | Careful | Sassy) (Timid | Modest)" }, new long[] { 859 }, null);
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.That(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[]
            {
                "  \"pokepast.es\": 3 post(s) so far, searching before 2022-12-10",
                "  \"Nature (Adamant | Jolly | Impish | Ca...\": 3 post(s) so far, searching before 2022-12-10",
                "Searched posts in 1 forum(s): \"pokepast.es\" 3, \"Nature (Adamant | Jolly | Impish | Ca...\" 3, 3 post(s) in all",
            }));
        }

        [Test]
        public void Search_AnsweredWithAnErrorPage_StopsTheCrawl()
        {
            var search = new PostSearch(_ => Task.FromResult("<html data-template=\"error\"><div class=\"blockMessage\">The search could not be completed because the search keywords were too short, too long, or too common.</div></html>"));

            var stop = Assert.ThrowsAsync<UnexpectedPageException>(() => search.SearchAsync(new[] { CollectorOptions.TypedTeamQuery }, new long[] { 726 }, null));

            Assert.That(stop!.Message, Does.Contain("too short, too long, or too common"));
        }

        [Test]
        public async Task Search_ThatCannotBeLoaded_IsNotAvailable()
        {
            var search = new PostSearch(_ => throw new System.Net.Http.HttpRequestException("Service Unavailable"));

            Assert.That(await search.SearchAsync(new[] { CollectorOptions.PasteQuery }, new long[] { 726 }, null), Is.Null);
        }

        /// <summary>
        /// The page of 2026-11-26 as Smogon answers a search without results: seen in a div in the markup of 2026-11-13, not yet
        /// since. Smogon has turned other divs of the class into elements of its own, as &lt;browser-warning&gt;.
        /// </summary>
        private static string NoResultsPage(string element = "div") => Regex.Replace(SavedPages.SearchResults, "<ol class=\"block-body\">.*?</ol>",
            $"<div class=\"block-body\">\n\t\t\t<{element} class=\"blockMessage\">\n\t\t\t\tNo results found.\n\t\t\t</{element}>\n\t\t</div>", RegexOptions.Singleline);

        [TestCase("div")]
        [TestCase("block-message")]
        public async Task Search_ReadsTheResultsOf2026_11_26_AndAPageWithoutResults(string element)
        {
            var search = new PostSearch(url => Task.FromResult(url.Contains("hastebin") ? SavedPages.SearchResults : NoResultsPage(element)));

            var result = await search.SearchAsync(new[] { CollectorOptions.PasteQuery, CollectorOptions.TypedTeamQuery }, new long[] { 726, 859 }, new DateTime(2026, 11, 24));

            Assert.That(NoResultsPage(element), Does.Not.Contain("block-row--separated"));
            Assert.That(result!.Hits.Select(hit => hit.PostId), Is.EquivalentTo(new long?[] { 11201487, 11201488 }));
        }

        [Test]
        public void Search_StopsAtResultsItCannotRead()
        {
            // The page of 2026-11-26 with a date the parser cannot read, as for the parser before it
            var unreadable = SavedPages.SearchResults.Replace("<dynamic-time timestamp=", "<dynamic-time ts=");
            var search = new PostSearch(_ => Task.FromResult(unreadable));

            var stop = Assert.ThrowsAsync<UnexpectedPageException>(() => search.SearchAsync(new[] { "pokepast.es" }, new long[] { 726 }, null));

            Assert.That(stop!.Url, Does.StartWith("https://www.smogon.com/forums/search/search?keywords=pokepast.es"));
        }

        [Test]
        public void Search_StopsAtAResultsPageWithoutResultsOrNoResultsMessage()
        {
            var empty = Regex.Replace(SavedPages.SearchResults, "<ol class=\"block-body\">.*?</ol>", "", RegexOptions.Singleline);
            var search = new PostSearch(_ => Task.FromResult(empty));

            Assert.ThrowsAsync<UnexpectedPageException>(() => search.SearchAsync(new[] { "pokepast.es" }, new long[] { 726 }, null));
        }

        [Test]
        public void Url_IsTheSearchOfTheForums_ByDate()
        {
            // Unix timestamps: Smogon drops dates such as 2026-11-10 from the search (since 2026-11-16)
            Assert.That(PostSearch.Url("pokepast.es | \"EVs Nature\"", new long[] { 726, 748 }, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 12)),
                Is.EqualTo("https://www.smogon.com/forums/search/search?keywords=pokepast.es%20%7C%20%22EVs%20Nature%22&search_type=post&order=date&c[newer_than]=1794268800&c[older_than]=1794441600&c[nodes][0]=726&c[nodes][1]=748"));
            Assert.That(PostSearch.Url("psim.us", new long[] { 670 }, null, null),
                Is.EqualTo("https://www.smogon.com/forums/search/search?keywords=psim.us&search_type=post&order=date&c[nodes][0]=670"));
        }

        [Test]
        public void Queries_FitIntoSmogonsSearch()
        {
            // Smogon refuses queries of more than about 22 words or 200 characters.
            foreach (var query in new CollectorOptions().SearchQueries)
            {
                Assert.That(query.Split(' ').Count(word => word.Any(char.IsLetter)), Is.LessThanOrEqualTo(21), query);
                Assert.That(query.Length, Is.LessThan(200), query);
            }
        }
    }
}
