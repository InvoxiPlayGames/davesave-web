using System.Text.Json.Serialization;

namespace davesave_web
{
    public class ErrorResponse
    {
        [JsonPropertyName("source")] public string? ErrorSource { get; set; }
        [JsonPropertyName("code")] public uint ErrorCode { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    public class XboxProfileInformation
    {
        [JsonPropertyName("gamertag")] public string? Gamertag { get; set; }
        [JsonPropertyName("xuid")] public string? XUID { get; set; }
        [JsonPropertyName("picture")] public string? ProfilePictureURI { get; set; }
    }

    public class TicketInformation
    {
        [JsonPropertyName("jwt")] public string? JWT { get; set; }
        [JsonPropertyName("expiry")] public DateTime Expiry { get; set; }
    }

    public class RedeemAuthResponse
    {
        [JsonPropertyName("profile")] public XboxProfileInformation? Profile { get; set; }
        [JsonPropertyName("access")] public TicketInformation? AccessTicket { get; set; }
        [JsonPropertyName("refresh")] public TicketInformation? RefreshTicket { get; set; }
    }

    public class StartAuthResponse
    {
        [JsonPropertyName("loginUri")] public string? LoginURI { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }

    public class SavegameMetaResponse
    {
        [JsonPropertyName("lastModified")] public DateTime LastModified { get; set; }
        [JsonPropertyName("size")] public int FileSize { get; set; }
    }
}
