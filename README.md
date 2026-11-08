# Smogon Team Crawler

This project is the code-base of the Smogon Team Crawler.

Goes through [Smogon](http://www.smogon.com/forums/) and saves all teams it finds.

A more detailed description: https://www.smogon.com/forums/threads/smogon-rmt-team-dump.3622884/

## Usage

Use the command line application of the `SmogonTeamCrawler.Cmd` project.

```
SmogonTeamCrawler.Cmd [--full] [--delay <seconds>] [--recent-days <days>]
```

The crawl state is kept in `SmogonDump.db` in the working directory. Keep that file between runs:

- The first run reads every listing page and every thread. A `SmogonDump.db` from version 2.x is taken over; threads whose latest post still matches the forum listing are not read again.
- Later runs only read what changed. Forum listings are sorted by latest post, so a run reads listing pages until it reaches a thread without new posts. Of a changed thread it reads the last known page and the new pages. Pastes (pokepast.es, pastebin) are fetched once.
- When a thread has new posts, its pages with posts younger than `--recent-days` (default 7) are read again as well, so that likes on recent teams stay current. Likes and edits on older posts keep the state of their last read; `--full` reads everything again.

Requests go out one at a time per host with a pause of `--delay` seconds (default 2). The crawler identifies itself with the User-Agent `SmogonTeamCrawler/3.0 (+https://github.com/FullLifeGames/Smogon-Team-Crawler)`, waits as long as `Retry-After` asks and does not retry 404s. At the end it prints how many requests it sent.

If Smogon answers with a bot check or an unexpected page, the crawl stops with exit code 2. The team files and the stored state are left as they were; threads read before the stop are kept and the next run continues from there.

### Tests

`dotnet test` runs offline against generated forum pages. The tests that send requests to smogon.com are explicit; run them on purpose with `dotnet test --filter TestCategory=Live`.

## Package

The code can be used as a NuGet package, which can be found [here](https://www.nuget.org/packages/SmogonTeamCrawler.Core/).

### Package Usage Examples

See how the tests use it [here](https://github.com/FullLifeGames/Smogon-Team-Crawler/blob/master/SmogonTeamCrawler.Tests/TestSmogonTeamCrawler.cs).
