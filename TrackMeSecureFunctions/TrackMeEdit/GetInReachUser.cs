using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Security.Claims;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Azure.Documents.Client;
using System.Linq;
using Microsoft.Azure.Documents;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class GetInReachUser
    {
        [FunctionName("GetInReachUser")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> users,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
            )] IAsyncCollector<InReachUser> output,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
           )] DocumentClient client,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
            IAsyncCollector<KMLInfo> addDocuments)
        {
            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("HomeIoTDB", "GPSTracks");
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(users, Identities);
            var IsUserExist = false;
            if (LoggedInUser.status != Status.UserMissing)
                IsUserExist = true;

            if (IsUserExist)
            {
                //this part is for changing of existing user
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (requestBody != "")
                {
                    var userDataFromWeb = JsonConvert.DeserializeObject<InReachUser>(requestBody);
                    if (userDataFromWeb.userWebId != LoggedInUser.userWebId)
                        await ChangePartitionAsync(LoggedInUser.userWebId, userDataFromWeb.userWebId, addDocuments, client, collectionUri); //change partitionIDs
                    LoggedInUser.InReachWebAddress = userDataFromWeb.InReachWebAddress;
                    LoggedInUser.InReachWebPassword = userDataFromWeb.InReachWebPassword;
                    LoggedInUser.userWebId = userDataFromWeb.userWebId;
                    LoggedInUser.Active = userDataFromWeb.Active;
                    await ManageTodayTrack(LoggedInUser, addDocuments, client, collectionUri);
                }
                await output.AddAsync(LoggedInUser);
            }
            return new OkObjectResult(LoggedInUser);
        }
        static async Task ManageTodayTrack(InReachUser LoggedInUser, IAsyncCollector<KMLInfo> addDocuments, DocumentClient client, Uri collectionUri)
        {
            KMLInfo TodayTrack = new KMLInfo()
            {
                id = "TodayTrack",
                Title = "Today's track",
                groupid = LoggedInUser.userWebId,
                InReachWebAddress = LoggedInUser.InReachWebAddress,
                InReachWebPassword = LoggedInUser.InReachWebPassword,
                d1 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd"),
                d2 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd")
            };
            HelperGetKMLFromGarmin GetKMLFromGarmin = new HelperGetKMLFromGarmin();
            HelperKMLParse _helperInReach = new HelperKMLParse();

            //create Today's track
            if (LoggedInUser.Active)
            {
                //get feed grom garmin
                var kmlFeedresult = await GetKMLFromGarmin.GetKMLAsync(TodayTrack);
                //parse and transform the feed
                TodayTrack = _helperInReach.GetAllPlacemarks(kmlFeedresult, TodayTrack);
                //add or update the track based on the id
                await addDocuments.AddAsync(TodayTrack);
            }
            if (!LoggedInUser.Active)
            {
                //select document
                var queryOne = new SqlQuerySpec("SELECT c._self, c.groupid FROM c WHERE c.id = @id",
                    new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = TodayTrack.id } }));
                KMLInfo trackItem = client.CreateDocumentQuery<KMLInfo>(collectionUri, queryOne, new FeedOptions { PartitionKey = new PartitionKey(TodayTrack.groupid) }).AsEnumerable().FirstOrDefault();
                //delete document
                await client.DeleteDocumentAsync(trackItem._self, new RequestOptions { PartitionKey = new PartitionKey(trackItem.groupid) });
            }
        }
        static async Task ChangePartitionAsync(string userWebId, string newUserWebId, IAsyncCollector<KMLInfo> addDocuments, DocumentClient client, Uri collectionUri)
        {
            //getting active tracks from cosmos
            var query = new SqlQuerySpec("SELECT * FROM c WHERE c.groupid = @groupid",
                new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@groupid", Value = userWebId } }));
            IEnumerable<KMLInfo> TracksMetadata = client.CreateDocumentQuery<KMLInfo>(collectionUri, query, new FeedOptions { PartitionKey = new PartitionKey(userWebId) }).AsEnumerable();
            foreach (KMLInfo item in TracksMetadata)
            {
                item.groupid = newUserWebId;
                await addDocuments.AddAsync(item);
                await client.DeleteDocumentAsync(item._self, new RequestOptions { PartitionKey = new PartitionKey(userWebId) });
            }
        }
    }
    public class InReachUser
    {
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
        public string email { get; set; }
        public string name { get; set; }
        public string userWebId { get; set; }
        public bool Active { get; set; }
        public string id { get; set; }
        public string groupid { get; set; }
        public string[] subscibers { get; set; }
        public string status { get; set; }
    }
    public class Status
    {
        public const string ExistingUser = "Existing user";
        public const string NewUser = "New user";
        public const string UserMissing = "user missing";
    };
}
