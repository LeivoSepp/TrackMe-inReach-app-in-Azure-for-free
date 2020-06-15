using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    class HelperCheckUser
    {
        public InReachUser LoggedInUser(IEnumerable<InReachUser> inReachUsers, ClaimsPrincipal Identities)
        {
            var loggedInUser = new InReachUser
            {
                id = Identities.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier).Value,
                email = Identities.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Email).Value,
                name = Identities.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name).Value,
                groupid = "user",
                status = Status.NewUser
            };
            var TrackMeUser = loggedInUser;
            if (loggedInUser.id != null && loggedInUser.email != null)
            {
                foreach (var user in inReachUsers)
                {
                    if (user.id == loggedInUser.id)
                    {
                        TrackMeUser = user;
                        TrackMeUser.email = loggedInUser.email; //in case user email has changed
                        TrackMeUser.name = loggedInUser.name; //in case user name has changed
                        TrackMeUser.status = Status.ExistingUser;
                        return TrackMeUser;
                    }
                }
                return TrackMeUser;
            }
            else
            {
                TrackMeUser.status = Status.UserMissing;
                return TrackMeUser;
            }
        }
    }
}
