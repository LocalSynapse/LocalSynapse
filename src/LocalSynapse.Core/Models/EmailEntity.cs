namespace LocalSynapse.Core.Models;

public sealed class EmailEntity
{
    public required string EmailId { get; set; }
    public required string SourceType { get; set; }
    public string? FileId { get; set; }
    public string? GraphMessageId { get; set; }
    public string? ThreadId { get; set; }
    public string? ConversationId { get; set; }
    public string? InternetMessageId { get; set; }
    public string? InReplyTo { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientsJson { get; set; }
    public string? Subject { get; set; }
    public string? NormalizedSubject { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public bool HasAttachments { get; set; }
    public string? WebLink { get; set; }
    public string? BodyPreview { get; set; }
    public string? FolderName { get; set; }
    public bool IsFromMe { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsGraphEmail => SourceType == "graph";
    public bool IsFileEmail => SourceType == "file";
}

public static class EmailSourceTypes
{
    public const string File = "file";
    public const string Graph = "graph";
}
