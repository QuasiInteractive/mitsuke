using System.Diagnostics;

namespace Mitsuke.Tests;

public class TelemetryTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/dEviCeToKeN:APA91b", "https://fcm.googleapis.com/[device]")]
    [InlineData("https://web.push.apple.com/QGuKr-token", "https://web.push.apple.com/[device]")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc", "https://wns2-par02p.notify.windows.com/[device]")]
    public void Push_endpoints_keep_only_the_host(string endpoint, string recorded)
    {
        using var activity = new Activity("push");
        MitsukeTelemetry.RedactPushEndpoint(activity, new HttpRequestMessage(HttpMethod.Post, endpoint));
        Assert.Equal(recorded, activity.GetTagItem("url.full"));
    }

    [Fact]
    public void Other_calls_are_left_alone()
    {
        using var activity = new Activity("search");
        MitsukeTelemetry.RedactPushEndpoint(activity, new HttpRequestMessage(HttpMethod.Get, "https://thecarapi.com/api/search?make=Nissan"));
        Assert.Null(activity.GetTagItem("url.full"));
        Assert.False(MitsukeTelemetry.IsPushHost("evilfcm.googleapis.com.example"));
    }
}
