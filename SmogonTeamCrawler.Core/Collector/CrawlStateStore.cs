using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using SmogonTeamCrawler.Core.Data;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>
    /// Keeps the incremental crawl state in an <see cref="IDistributedCache"/>.
    /// Entries of crawler versions before 3.0 (thread URL to <see cref="ThreadAnalyzeResult"/>) stay readable for the migration.
    /// </summary>
    internal class CrawlStateStore
    {
        private const string KeyPrefix = "stc:v3:";

        private readonly IDistributedCache _cache;

        public CrawlStateStore(IDistributedCache cache)
        {
            _cache = cache;
        }

        public ForumCrawlState? GetForum(string forumUrl) => Get<ForumCrawlState>(KeyPrefix + "forum:" + forumUrl);

        public void SetForum(string forumUrl, ForumCrawlState state) => Set(KeyPrefix + "forum:" + forumUrl, state);

        public ThreadCrawlState? GetThread(long threadId) => Get<ThreadCrawlState>(KeyPrefix + "thread:" + threadId);

        public void SetThread(long threadId, ThreadCrawlState state) => Set(KeyPrefix + "thread:" + threadId, state);

        /// <summary>Team text of a paste, "" if the paste holds no team, null if it was never fetched.</summary>
        public string? GetPaste(string pasteUrl) => GetString(KeyPrefix + "paste:" + pasteUrl);

        public void SetPaste(string pasteUrl, string teamText) => _cache.SetString(KeyPrefix + "paste:" + pasteUrl, teamText);

        /// <summary>Entry written by crawler versions before 3.0, keyed by the thread URL.</summary>
        public ThreadAnalyzeResult? GetLegacyThread(string threadUrl) => Get<ThreadAnalyzeResult>(threadUrl);

        private string? GetString(string key)
        {
            try
            {
                return _cache.GetString(key);
            }
            catch (NullReferenceException)
            {
                // NeoSmart.Caching.Sqlite throws on some missing entries
                return null;
            }
        }

        private T? Get<T>(string key) where T : class
        {
            var json = GetString(key);
            if (json == null)
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void Set<T>(string key, T value) => _cache.SetString(key, JsonConvert.SerializeObject(value));
    }

    /// <summary>Process-local cache used when no persistent cache is passed in; nothing survives the run.</summary>
    internal class InMemoryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();

        public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
