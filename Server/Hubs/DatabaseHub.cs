using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Server.Hubs
{
    [Authorize]
    public class DatabaseHub : Hub
    {
       


    }
}
