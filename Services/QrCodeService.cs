using System.IO;
using Avalonia.Media.Imaging;
using QRCoder;

namespace VoidUI.Services;

/// <summary>
/// Renders QR codes for arbitrary payloads (crypto addresses, trade links, urls).
/// Any block of the app can share this generator.
/// </summary>
public static class QrCodeService
{
    private static readonly byte[] Dark = { 0, 0, 0 };
    private static readonly byte[] Light = { 255, 255, 255 };

    /// <summary>Builds a QR bitmap, or null when the payload cannot be encoded.</summary>
    public static Bitmap? Render(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            using var stream = new MemoryStream(png.GetGraphic(10, Dark, Light));
            return new Bitmap(stream);
        }
        catch
        {
            return null; // a broken payload must never take the dialog down
        }
    }
}
