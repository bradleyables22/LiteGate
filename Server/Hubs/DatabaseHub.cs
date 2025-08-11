using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs
{
    [Authorize]
    public class DatabaseHub : Hub
    {
       


    }
}
