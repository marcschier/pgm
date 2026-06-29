// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Pgm;
using Pgm.Net;

InMemoryMulticastBus bus = new(seed: 42);

await using PgmPublisher publisher = new PgmPublisher(bus.CreateChannel());
await using PgmSubscriber first = new PgmSubscriber(bus.CreateChannel());
await using PgmSubscriber second = new PgmSubscriber(bus.CreateChannel());
using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

await first.StartAsync(timeout.Token);
await second.StartAsync(timeout.Token);
await publisher.StartAsync(timeout.Token);

string[] messages =
[
    "hello",
    "from",
    "PGM",
];

foreach (string message in messages)
{
    await publisher.PublishAsync(Encoding.UTF8.GetBytes(message), timeout.Token);
}

await PrintMessagesAsync("subscriber-1", first, messages.Length, timeout.Token);
await PrintMessagesAsync("subscriber-2", second, messages.Length, timeout.Token);

static async Task PrintMessagesAsync(
    string name,
    PgmSubscriber subscriber,
    int count,
    CancellationToken cancellationToken)
{
    for (int index = 0; index < count; index++)
    {
        byte[] message = await subscriber.ReceiveAsync(cancellationToken);
        Console.WriteLine(name + " received: " + Encoding.UTF8.GetString(message));
    }
}
