namespace QueryExplorer.Application.DTOs;

public class SubscribePushRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public class UnsubscribePushRequest
{
    public string Endpoint { get; set; } = string.Empty;
}
