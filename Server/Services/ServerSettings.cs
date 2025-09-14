using System.Security.Cryptography;

namespace Server.Services
{
    public class ServerSettings
    {

        public DateTime LastSecretRotation { get; internal set; } = DateTime.UtcNow;
        private string Secret { get; set; } = "357RE877ORJHJ8VGHHTT2PJXTRZLEYXBFGQ2A6VPRBWH1C52WT0QWBXP8ICMAGT3RV16ZU25APXNVA1V061HBB782AZEJ3G49VP352I3N1QSVN533NW5BYXPPIEV5PQU";

        private int BackupMinutes = 10;
        public int TokenExpiryMinutes 
        {
            get 
            {
                var environmentSetting = Environment.GetEnvironmentVariable("LG_TOKEN_EXPIRY_MINUTES");
                
                if (string.IsNullOrEmpty(environmentSetting))
                    return BackupMinutes;

                try
                {
                    return Convert.ToInt32(environmentSetting);
                }
                catch (Exception )
                {
                    return BackupMinutes;
                }

			}
        }

        private SemaphoreSlim SecretLock = new SemaphoreSlim(1,1);
        public async Task<string> GetSecretAsync() 
        {
            try
            {
                await SecretLock.WaitAsync();

                return Secret;
            }
            catch (Exception)
            {
                return Secret;
            }
            finally 
            {
				SecretLock.Release();
			}
        }
        public async Task ChangeSecretAsync() 
        {
			
            try
            {
				await SecretLock.WaitAsync();

                var newSecret = GenerateSecret();
                Secret = newSecret;
                LastSecretRotation = DateTime.UtcNow;
			}
            finally 
            { 
                SecretLock.Release(); 
            }
		}
        private string GenerateSecret()
        {

            byte[] key = RandomNumberGenerator.GetBytes(64); 
            return Convert.ToBase64String(key);
        }

    }
}
