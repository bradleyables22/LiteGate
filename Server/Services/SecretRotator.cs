
namespace Server.Services
{
	public class SecretRotator : BackgroundService
	{
		private readonly ServerSettings _settings;

		public SecretRotator(ServerSettings settings)
		{
			_settings = settings;
		}
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested) 
			{
				await Task.Delay(TimeSpan.FromDays(1));

				try
				{
					await _settings.ChangeSecretAsync();
				}
				catch (Exception)
				{
					continue;
				}
			}
		}
	}
}
