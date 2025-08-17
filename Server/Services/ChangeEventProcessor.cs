using Server.Utilities;
using System.Text.Json;
using System.Threading.Channels;

namespace Server.Services
{
    public class ChangeEventProcessor : BackgroundService
    {
        private readonly Channel<SqliteChangeEvent> _channel;
        public ChangeEventProcessor(Channel<SqliteChangeEvent> channel)
        {
            _channel = channel;
        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested) 
            {
                var changeEvent = await _channel.Reader.ReadAsync(stoppingToken);

                Console.WriteLine(JsonSerializer.Serialize(changeEvent)); // simulating pushing change event out
            }
        }
    }
}
