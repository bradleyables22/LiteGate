using System.Security.Cryptography;

namespace Server.Services
{
    public class ServerSettings
    {
        public string Secret { get; set; } = "357RE877ORJHJ8VGHHTT2PJXTRZLEYXBFGQ2A6VPRBWH1C52WT0QWBXP8ICMAGT3RV16ZU25APXNVA1V061HBB782AZEJ3G49VP352I3N1QSVN533NW5BYXPPIEV5PQU";
        public int TokenExpiryMinutes { get; set; } = 5;


        public void GenerateSecret()
        {
            byte[] key = RandomNumberGenerator.GetBytes(64); 
            Secret =  Convert.ToBase64String(key);
        }

        public void ChangeTokenExpiry(int minutes) => TokenExpiryMinutes = minutes;
    }
}
