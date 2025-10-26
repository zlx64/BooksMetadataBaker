using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Models;

public class UploadRequest
{
    public required string Title { get; set; }
    public required BookType Type { get; set; }
}
