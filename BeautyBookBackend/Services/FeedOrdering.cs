namespace BeautyBookBackend.Services;
public static class FeedOrdering
{
    // Preserve ranked order within each artist. Defer, never discard, blocked posts.
    public static Guid[] Interleave(IEnumerable<(Guid Id, Guid MuaId)> ranked, Guid? last = null, int run = 0)
    {
        var rows = ranked.Select((row, rank) => (row.Id, row.MuaId, Rank: rank)).ToList();
        var queues = rows.GroupBy(row => row.MuaId).ToDictionary(group => group.Key, group => new Queue<(Guid Id, int Rank)>(group.Select(row => (row.Id, row.Rank))));
        var ready = new PriorityQueue<Guid, int>();
        foreach (var (artist, queue) in queues) ready.Enqueue(artist, queue.Peek().Rank);
        var result = new List<Guid>(rows.Count);
        while (ready.Count > 0)
        {
            var artist = ready.Dequeue();
            if (artist == last && run >= 2 && ready.Count > 0)
            {
                var alternate = ready.Dequeue();
                ready.Enqueue(artist, queues[artist].Peek().Rank);
                artist = alternate;
            }
            var post = queues[artist].Dequeue();
            result.Add(post.Id);
            run = artist == last ? run + 1 : 1; last = artist;
            if (queues[artist].Count > 0) ready.Enqueue(artist, queues[artist].Peek().Rank);
        }
        return result.ToArray();
    }
}
