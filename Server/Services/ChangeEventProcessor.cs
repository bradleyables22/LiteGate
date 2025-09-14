using Server.Extensions;
using Server.Interaction;
using System.Threading.Channels;

namespace Server.Services
{
    public class ChangeEventProcessor : BackgroundService
    {
        private readonly Channel<SqliteChangeEvent> _channel;
        private readonly UserDatabase _userDb;
        private readonly HttpClient _http;

        public ChangeEventProcessor(Channel<SqliteChangeEvent> channel, UserDatabase userDb, IHttpClientFactory factory)
        {
            _channel = channel;
            _userDb = userDb;
            _http = factory.CreateClient("webhooks");

        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var gate = new SemaphoreSlim(8);

            while (!stoppingToken.IsCancellationRequested)
            {
                while (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (_channel.Reader.TryRead(out var changeEvent))
                    {
                        var subsTry = await _userDb.GetSubscriptionsByKeysAsync(changeEvent.Database, changeEvent.Table, changeEvent.EventType, stoppingToken);

                        if (!subsTry.Success || subsTry.Data is null || subsTry.Data.Count == 0)
                            continue;

                        var tasks = new List<Task>(subsTry.Data.Count);
                        foreach (var sub in subsTry.Data)
                        {
                            await gate.WaitAsync(stoppingToken);
                            tasks.Add(Task.Run(async () =>
                            {
                                try 
                                { 
                                    await _http.DeliverWithRetriesAsync(sub, changeEvent, stoppingToken); 
                                }
                                finally 
                                { 
                                    gate.Release(); 
                                }
                            }, stoppingToken));
                        }
                        await Task.WhenAll(tasks);
                    }
                }
            }
        }
    }
}

