using System.Net.Http.Headers;

namespace ToolFleet.Api.Tests;

/// <summary>Sends the same multipart form the website's Publish button sends.</summary>
internal static class FirmwareUpload
{
    public static Task<HttpResponseMessage> PublishAsync(
        HttpClient client, string model, string version, byte[]? content, string fileName = "firmware.bin")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent(version), "version" },
            { new StringContent("Test build"), "notes" },
        };
        if (content is not null)
        {
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
        }
        return client.PostAsync("/api/firmware", form);
    }

    /// <summary>Some made-up firmware bytes.</summary>
    public static byte[] FakeBuild(int size = 4096)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        return bytes;
    }
}
