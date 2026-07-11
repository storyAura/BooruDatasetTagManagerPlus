using System.Drawing;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class ImageLruCacheTests
{
    private static Bitmap NewBitmap() => new Bitmap(4, 4);

    private static bool IsDisposed(Image image)
    {
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Fact]
    public void Set_beyond_capacity_evicts_and_disposes_least_recently_used()
    {
        using var cache = new ImageLruCache(2);
        Bitmap first = NewBitmap();
        Bitmap second = NewBitmap();
        Bitmap third = NewBitmap();

        cache.Set("a", first);
        cache.Set("b", second);
        cache.Set("c", third);

        Assert.True(IsDisposed(first));
        Assert.False(IsDisposed(second));
        Assert.False(IsDisposed(third));
        Assert.False(cache.TryGetClone("a", out _));
        Assert.True(cache.TryGetClone("b", out Image bClone));
        bClone.Dispose();
    }

    [Fact]
    public void TryGetClone_returns_independent_clone()
    {
        using var cache = new ImageLruCache(4);
        cache.Set("a", NewBitmap());

        Assert.True(cache.TryGetClone("a", out Image clone));
        clone.Dispose();

        // Disposing the clone must not affect the cached original.
        Assert.True(cache.TryGetClone("a", out Image secondClone));
        Assert.False(IsDisposed(secondClone));
        secondClone.Dispose();
    }

    [Fact]
    public void Set_same_key_with_new_image_disposes_old_image()
    {
        using var cache = new ImageLruCache(4);
        Bitmap old = NewBitmap();
        Bitmap replacement = NewBitmap();

        cache.Set("a", old);
        cache.Set("a", replacement);

        Assert.True(IsDisposed(old));
        Assert.False(IsDisposed(replacement));
    }

    [Fact]
    public void Remove_and_clear_dispose_cached_images()
    {
        using var cache = new ImageLruCache(4);
        Bitmap removed = NewBitmap();
        Bitmap cleared = NewBitmap();

        cache.Set("a", removed);
        cache.Set("b", cleared);

        cache.Remove("a");
        Assert.True(IsDisposed(removed));
        Assert.False(cache.TryGetClone("a", out _));

        cache.Clear();
        Assert.True(IsDisposed(cleared));
        Assert.False(cache.TryGetClone("b", out _));
    }

    [Fact]
    public void Concurrent_set_get_remove_clear_does_not_throw()
    {
        using var cache = new ImageLruCache(8);

        Parallel.For(0, 200, i =>
        {
            string key = "k" + (i % 16);
            switch (i % 4)
            {
                case 0:
                    cache.Set(key, NewBitmap());
                    break;
                case 1:
                    if (cache.TryGetClone(key, out Image clone))
                        clone.Dispose();
                    break;
                case 2:
                    cache.Remove(key);
                    break;
                default:
                    cache.Clear();
                    break;
            }
        });
    }
}
