using Avalonia.Media.Imaging;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.SkiaSharp;

namespace PasswordManagerLocal.Services;

public static class EnrollmentQrCodeService
{
    private const string EnrollmentPayloadPrefix = "pml-enrollment-v1:";
    private const string EnrollmentPayloadUriPrefix = "passwordmanagerlocal://enrollment?code=";

    public static Bitmap? CreateQrCodeBitmap(string? enrollmentCode, int size = 260)
    {
        if (string.IsNullOrWhiteSpace(enrollmentCode))
        {
            return null;
        }

        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                CharacterSet = "UTF-8",
                ErrorCorrection = ErrorCorrectionLevel.M,
                Height = size,
                Margin = 2,
                Width = size
            }
        };

        using var qrBitmap = writer.Write(CreateEnrollmentQrPayload(enrollmentCode));
        return ToAvaloniaBitmap(qrBitmap);
    }



    public static string CreateEnrollmentQrPayload(string enrollmentCode) =>
        $"{EnrollmentPayloadPrefix}{enrollmentCode.Trim()}";



    public static string? DecodeQrCode(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        using var imageData = SKData.CreateCopy(imageBytes);
        using var bitmap = SKBitmap.Decode(imageData);

        if (bitmap is null)
        {
            return null;
        }

        var reader = new ZXing.SkiaSharp.BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                CharacterSet = "UTF-8",
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true
            }
        };

        var result = reader.Decode(bitmap);
        return string.IsNullOrWhiteSpace(result?.Text)
            ? null
            : result.Text.Trim();
    }



    public static string? DecodeEnrollmentCodeFromQrImage(byte[]? imageBytes, bool allowPlainEnrollmentCode = true)
    {
        var qrText = DecodeQrCode(imageBytes);
        return ExtractEnrollmentCode(qrText, allowPlainEnrollmentCode);
    }



    public static string? ExtractEnrollmentCode(string? qrText, bool allowPlainEnrollmentCode = true)
    {
        if (string.IsNullOrWhiteSpace(qrText))
        {
            return null;
        }

        var text = qrText.Trim();
        if (text.StartsWith(EnrollmentPayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = text[EnrollmentPayloadPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }

        if (text.StartsWith(EnrollmentPayloadUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encodedCode = text[EnrollmentPayloadUriPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(encodedCode))
            {
                return null;
            }

            try
            {
                var code = Uri.UnescapeDataString(encodedCode).Trim();
                return string.IsNullOrWhiteSpace(code) ? null : code;
            }
            catch
            {
                return null;
            }
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "passwordmanagerlocal", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "enrollment", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var equalsIndex = part.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(part[..equalsIndex]);
                if (!string.Equals(key, "code", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = Uri.UnescapeDataString(part[(equalsIndex + 1)..]).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        if (allowPlainEnrollmentCode && LooksLikeEnrollmentCode(text))
        {
            return text;
        }

        return null;
    }



    private static bool LooksLikeEnrollmentCode(string text)
    {
        var normalized = new string(text.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
        return normalized.StartsWith("PML-", StringComparison.Ordinal)
            || normalized.StartsWith("PML2-", StringComparison.Ordinal)
            || normalized.StartsWith("PML3-", StringComparison.Ordinal);
    }



    private static Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
