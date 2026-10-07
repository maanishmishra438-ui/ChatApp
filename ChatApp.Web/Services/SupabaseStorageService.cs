using Supabase;

namespace ChatApp.Web.Services;

public class SupabaseStorageService
{
    private readonly Supabase.Client _client;

    private const string BucketName =
        "chat-media";


    public SupabaseStorageService(
        IConfiguration configuration)
    {
        var url =
            configuration["Supabase:Url"];

        var serviceKey =
            configuration["Supabase:ServiceKey"];


        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "Supabase:Url is not configured.");
        }


        if (string.IsNullOrWhiteSpace(serviceKey))
        {
            throw new InvalidOperationException(
                "Supabase:ServiceKey is not configured.");
        }


        _client =
            new Supabase.Client(
                url,
                serviceKey,
                new SupabaseOptions
                {
                    AutoConnectRealtime = false
                });
    }


    // ============================================================
    // UPLOAD PHOTO
    // ============================================================

    public async Task<string> UploadPhotoAsync(
        byte[] fileBytes,
        string filePath,
        string contentType)
    {
        if (fileBytes is null ||
            fileBytes.Length == 0)
        {
            throw new ArgumentException(
                "Photo file is empty.",
                nameof(fileBytes));
        }


        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "File path is required.",
                nameof(filePath));
        }


        await _client.InitializeAsync();


        await _client.Storage
            .From(BucketName)
            .Upload(
                fileBytes,
                filePath,
                new Supabase.Storage.FileOptions
                {
                    ContentType =
                        contentType,

                    Upsert =
                        false
                });


        return filePath;
    }


    // ============================================================
    // DOWNLOAD PHOTO
    // ============================================================
    //
    // Server-side only.
    //
    // The Supabase bucket stays PRIVATE.
    // The service key never reaches the browser.
    // ============================================================

    public async Task<byte[]> DownloadPhotoAsync(
    string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "File path is required.",
                nameof(filePath));
        }

        await _client.InitializeAsync();

        var bytes =
            await _client.Storage
                .From(BucketName)
                .Download(
                    filePath,
                    (EventHandler<float>?)null);

        if (bytes is null ||
            bytes.Length == 0)
        {
            throw new InvalidOperationException(
                "Photo file is empty or could not be downloaded.");
        }

        return bytes;
    }
}

