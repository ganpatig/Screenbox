using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class LruCacheTests
{
    [Test]
    public async Task TryGetValue_ShouldReturnCachedValue()
    {
        LruCache<long, string> cache = new(2);
        cache.Set(1, "one");

        bool isFound = cache.TryGetValue(1, out string? value);

        await Assert.That(isFound).IsTrue();
        await Assert.That(value).IsEqualTo("one");
    }

    [Test]
    public async Task TryGetValue_ShouldReportMissingKey()
    {
        LruCache<long, string> cache = new(2);

        bool isFound = cache.TryGetValue(1, out string? value);

        await Assert.That(isFound).IsFalse();
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Set_ShouldEvictLeastRecentlyUsedEntry()
    {
        LruCache<long, string> cache = new(2);
        cache.Set(1, "one");
        cache.Set(2, "two");
        cache.TryGetValue(1, out _);
        cache.Set(3, "three");

        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.TryGetValue(1, out _)).IsTrue();
        await Assert.That(cache.TryGetValue(2, out _)).IsFalse();
        await Assert.That(cache.TryGetValue(3, out _)).IsTrue();
    }

    [Test]
    public async Task Set_ShouldReplaceExistingValueAndMarkItRecentlyUsed()
    {
        LruCache<long, string> cache = new(2);
        cache.Set(1, "one");
        cache.Set(2, "two");
        cache.Set(1, "uno");
        cache.Set(3, "three");

        bool isFound = cache.TryGetValue(1, out string? value);

        await Assert.That(isFound).IsTrue();
        await Assert.That(value).IsEqualTo("uno");
        await Assert.That(cache.TryGetValue(2, out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Clear_ShouldRemoveAllEntries()
    {
        LruCache<long, string> cache = new(2);
        cache.Set(1, "one");
        cache.Set(2, "two");

        cache.Clear();

        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.TryGetValue(1, out _)).IsFalse();
    }

    [Test]
    public async Task Constructor_ShouldRejectNonPositiveCapacity()
    {
        Exception? exception = null;
        try
        {
            _ = new LruCache<long, string>(0);
        }
        catch (Exception e)
        {
            exception = e;
        }

        await Assert.That(exception is ArgumentOutOfRangeException).IsTrue();
    }
}
