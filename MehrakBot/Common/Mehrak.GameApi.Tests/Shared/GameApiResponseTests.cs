#region

using Mehrak.GameApi.Shared.Types;

#endregion

namespace Mehrak.GameApi.Tests.Shared;

[TestFixture]
public class GameApiResponseTests
{
    [Test]
    public void Available_WithPayload_ExposesNonNullPayload()
    {
        var payload = new object();
        var response = GameApiResponse<object>.Available(payload);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Availability, Is.EqualTo(GameApiAvailability.Available));
            Assert.That(response.IsAvailable, Is.True);
            Assert.That(response.Payload, Is.SameAs(payload));
        }
    }

    [Test]
    public void Available_WithNullPayload_Throws()
    {
        Assert.That(() => GameApiResponse<object>.Available(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Empty_HasNoPayloadAndIsNotAvailable()
    {
        var response = GameApiResponse<object>.Empty();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Availability, Is.EqualTo(GameApiAvailability.Empty));
            Assert.That(response.IsAvailable, Is.False);
            Assert.That(response.Payload, Is.Null);
        }
    }

    [Test]
    public void Locked_HasNoPayloadAndIsNotAvailable()
    {
        var response = GameApiResponse<object>.Locked();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Availability, Is.EqualTo(GameApiAvailability.Locked));
            Assert.That(response.IsAvailable, Is.False);
            Assert.That(response.Payload, Is.Null);
        }
    }
}
