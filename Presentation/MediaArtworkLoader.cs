using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace IslandDesk.Presentation;

internal static class MediaArtworkLoader
{
    // Small, bounded, frozen thumbnails can be shared by both persistent media views.
    public static Task<BitmapSource?> LoadAsync(IRandomAccessStreamReference? reference, CancellationToken token) =>
        Task.Run(async () =>
        {
            if (reference is null) return null;
            try
            {
                using var random = await reference.OpenReadAsync().AsTask(token).ConfigureAwait(false);
                if (random.Size is 0 or > 4 * 1024 * 1024) return null;
                using var stream = random.AsStreamForRead();
                using var bytes = new MemoryStream((int)random.Size);
                var buffer = new byte[16384];
                int count;
                while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + count > 4 * 1024 * 1024) return null;
                    await bytes.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                bytes.Position = 0;
                var frame = BitmapFrame.Create(bytes, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var width = frame.PixelWidth; var height = frame.PixelHeight;
                if (width <= 0 || height <= 0 || Math.Max(width, height) / (double)Math.Min(width, height) > 20) return null;
                bytes.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (width >= height) image.DecodePixelWidth = 144; else image.DecodePixelHeight = 144;
                image.StreamSource = bytes;
                image.EndInit();
                image.Freeze();
                token.ThrowIfCancellationRequested();
                return (BitmapSource)image;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Media artwork unavailable: {0}", error.Message);
                return null;
            }
        }, token);
}
