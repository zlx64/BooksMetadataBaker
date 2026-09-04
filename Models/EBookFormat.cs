namespace BooksMetadataBaker.Models;

public enum EBookFormat
{
    Pdf,
    Epub,
    Cbz,
    Cbr,
    Cb7,
    Cbt
}

public static class EBookFormatExtensions
{
    public static bool IsArchive(this EBookFormat format) =>
        format is EBookFormat.Cbz or EBookFormat.Cbr or EBookFormat.Cb7 or EBookFormat.Cbt;

    public static string ToExtension(this EBookFormat format) => format switch
    {
        EBookFormat.Epub => ".epub",
        EBookFormat.Cbz => ".cbz",
        EBookFormat.Cbr => ".cbr",
        EBookFormat.Cb7 => ".cb7",
        EBookFormat.Cbt => ".cbt",
        _ => ".pdf"
    };
}
