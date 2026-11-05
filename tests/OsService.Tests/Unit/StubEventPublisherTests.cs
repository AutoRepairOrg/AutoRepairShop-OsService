using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OsService.Domain.Events;
using OsService.Infrastructure.Messaging;

namespace OsService.Tests.Unit;

public class StubEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_ShouldCompleteWithoutThrowing()
    {
        var sut = new StubEventPublisher(NullLogger<StubEventPublisher>.Instance);
        var @event = new OsCancelledEvent(Guid.NewGuid(), "Cliente desistiu");

        var act = async () => await sut.PublishAsync(@event);

        await act.Should().NotThrowAsync();
    }
}
