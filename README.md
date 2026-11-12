# Smogon Team Crawler

This project is the code-base of the Smogon Team Crawler.

Goes through [Smogon](http://www.smogon.com/forums/) and saves all teams it finds.

A more detailed description: https://www.smogon.com/forums/threads/smogon-rmt-team-dump.3622884/

## Usage

Use the command line application of the `SmogonTeamCrawler.Cmd` project.

```
SmogonTeamCrawler.Cmd [--full] [--no-search] [--delay <seconds>] [--recent-days <days>] [--headed]
```

The crawl state is kept in `SmogonDump.db` in the working directory. Keep that file between runs:

- Team posts are found through Smogon's search, limited to the crawled forums, and only the pages with hits are read. The first run searches all posts of each forum, later runs the posts since the last run (with a day of margin), all forums in one search. Two searches find team posts:
  - links to pastes and Showdown teams: `pokepast.es | pastebin.com | hastebin.com | psim.us | teams.pokemonshowdown.com | play.pokemonshowdown.com`
  - teams written into a post: the word "Nature" and natures of two groups (physical and special, such as Jolly and Timid). A team almost always has both, a single set in a discussion only one, so this reads about a third of the pages that "EVs Nature" would.
- Smogon shows at most 200 results per search; a search that reaches them is repeated for the days before its oldest result. A day with more results is searched forum by forum.
- Some forums are walked instead: those of generations 1 and 2 (RBY, GSC, and the old-gens and past-generations forums), whose teams have no natures, and forums with threads of a version 2.x `SmogonDump.db`, which the walk takes over (threads whose latest post still matches the listing are not read again). A forum is also walked with `--no-search`, when Smogon's search is not available, or when a single forum has more than 200 results on one day.
- The first walk of a forum reads every listing page and every thread. Later walks read listing pages until they reach a thread without new posts, and of a changed thread the last page read before and the new pages. That also picks up teams the search cannot find, such as posts edited later, so an occasional `--no-search` run fills such gaps.
- Teams come from pastes (pokepast.es, pastebin), Showdown's team links (psim.us/t/…, teams.pokemonshowdown.com/view/…) and code blocks in posts. Pastes and Showdown teams are fetched once, while the crawler goes on reading the next Smogon pages.
- Likes on teams younger than `--recent-days` (default 7) are kept current: a search run reads a page with such teams again once they are twice as old as at its last read; a listing walk re-reads the recent pages of threads with new posts. Likes and edits on older posts keep the state of their last read; `--full` reads everything again.

Pages are loaded with a headless Chromium through [Playwright](https://playwright.dev/dotnet/), so cookies and scripts work like in a browser and a bot check that solves itself is passed. The crawler reads the HTML as the server sent it; images are not loaded. Chromium is downloaded on the first run (to `%LOCALAPPDATA%\ms-playwright` or `~/.cache/ms-playwright`). On Linux, the system libraries Chromium needs are installed once with `pwsh bin/<config>/net8.0/playwright.ps1 install-deps chromium` (or see [the Playwright docs](https://playwright.dev/dotnet/docs/browsers#install-system-dependencies)). `--headed` shows the browser window.

Requests go out one at a time per host with a pause of `--delay` seconds (default 2). The crawler identifies itself with the User-Agent `SmogonTeamCrawler/3.0 (+https://github.com/FullLifeGames/Smogon-Team-Crawler)`, waits as long as `Retry-After` asks and does not retry 404s. At the end it prints how many requests it sent.

If Smogon answers with a bot check or an unexpected page, the crawl stops with exit code 2. The team files and the stored state are left as they were; threads read before the stop are kept and the next run continues from there.

### Tests

`dotnet test` runs offline against generated forum pages. The tests that send requests to smogon.com are explicit; run them on purpose with `dotnet test --filter TestCategory=Live`. The tests of the browser handler run Chromium against a local server: `dotnet test --filter TestCategory=Browser`.

## Package

The code can be used as a NuGet package, which can be found [here](https://www.nuget.org/packages/SmogonTeamCrawler.Core/).

### Package Usage Examples

See how the tests use it [here](https://github.com/FullLifeGames/Smogon-Team-Crawler/blob/master/SmogonTeamCrawler.Tests/TestSmogonTeamCrawler.cs).
