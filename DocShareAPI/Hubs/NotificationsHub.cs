using DocShareAPI.Models;
using Microsoft.AspNetCore.SignalR;

namespace DocShareAPI.Hubs
{
    public class NotificationsHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var decodedToken = Context.GetHttpContext()?.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedToken == null)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroupName(decodedToken.userID));
            await base.OnConnectedAsync();
        }

        public static string GetUserGroupName(Guid userId)
        {
            return $"user:{userId}";
        }
    }
}
