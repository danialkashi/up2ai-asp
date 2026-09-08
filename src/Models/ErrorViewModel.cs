namespace Up2Ai.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public int OriginalStatusCode { get; set; } = 500;
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public bool IsNotFound => OriginalStatusCode == 404;
}
