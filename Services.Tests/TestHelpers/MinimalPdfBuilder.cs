using System.Text;

namespace Services.Tests.TestHelpers;

/// <summary>
/// Builds minimal, hand-crafted single-page PDF byte content (uncompressed, ASCII-only) for
/// tests that need a real file <see cref="UglyToad.PdfPig.PdfDocument"/> can parse, without
/// pulling in a PDF-writer dependency.
/// </summary>
internal static class MinimalPdfBuilder
{
    /// <param name="text">
    /// Text placed on the single page via a <c>Tj</c> show-text operator. Must not contain
    /// PDF string-literal special characters ('(', ')', '\\').
    /// </param>
    public static byte[] CreateWithText(string text)
    {
        var contentStream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /MediaBox [0 0 612 792] /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentStream.Length} >>\nstream\n{contentStream}\nendstream",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = sb.Length;
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n");
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        sb.Append($"startxref\n{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
