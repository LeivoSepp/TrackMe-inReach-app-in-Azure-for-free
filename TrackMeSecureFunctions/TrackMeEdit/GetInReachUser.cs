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
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Microsoft.Azure.Documents.SystemFunctions;

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
            IAsyncCollector<KMLInfo> addDocuments,
            ExecutionContext context
            )
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            var SendEmailFunctionKey = config["SendEmailInReachFunctionKey"];
            var SendEmailFunctionUrl = config["SendEmailFunctionUrl"];
            var WebSiteUrl = config["WebSiteUrl"];
            var TodayTrackId = config["TodayTrackId"];

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
                    LoggedInUser.UserTimezone = userDataFromWeb.UserTimezone;
                    await ManageTodayTrack(LoggedInUser, addDocuments, client, collectionUri, TodayTrackId, SendEmailFunctionUrl, SendEmailFunctionKey, WebSiteUrl);
                }
                await output.AddAsync(LoggedInUser);
            }
            return new OkObjectResult(LoggedInUser);
        }
        static async Task ManageTodayTrack(InReachUser LoggedInUser, IAsyncCollector<KMLInfo> addDocuments, DocumentClient client, Uri collectionUri, string TodayTrackId, string SendEmailFunctionUrl, string SendEmailFunctionKey, string WebSiteUrl)
        {
            var dated1 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");
            var dated2 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");
            var dateTimed1 = DateTime.Parse(dated1).AddHours(-LoggedInUser.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var dateTimed2 = DateTime.Parse(dated2).AddDays(1).AddHours(-LoggedInUser.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");

            KMLInfo TodayTrack = new KMLInfo()
            {
                id = TodayTrackId,
                Title = "Today's track",
                groupid = LoggedInUser.userWebId,
                InReachWebAddress = LoggedInUser.InReachWebAddress,
                InReachWebPassword = LoggedInUser.InReachWebPassword,
                d1 = dateTimed1,
                d2 = dateTimed2,
                UserTimezone = LoggedInUser.UserTimezone
            };
            //create Today's track
            if (LoggedInUser.Active)
            {
                HelperGetKMLFromGarmin helperGetKMLFromGarmin = new HelperGetKMLFromGarmin();
                HelperKMLParse helperKMLParse = new HelperKMLParse();

                var emails = new List<Emails>();
                //get feed grom garmin
                var kmlFeedresult = await helperGetKMLFromGarmin.GetKMLAsync(TodayTrack);
                //parse and transform the feed and save to database
                helperKMLParse.ParseKMLFile(kmlFeedresult, TodayTrack, emails, WebSiteUrl);
                await addDocuments.AddAsync(TodayTrack);

                //sending out emails
                if (emails.Any())
                {
                    HttpClient httpClient = new HttpClient();
                    Uri SendEmailFunctionUri = new Uri($"{SendEmailFunctionUrl}?code={SendEmailFunctionKey}");
                    var returnMessage = await httpClient.PostAsJsonAsync(SendEmailFunctionUri, emails);
                    var lastMessage = await returnMessage.Content.ReadAsStringAsync();
                }
            }
            //delete Today's track
            if (!LoggedInUser.Active)
            {
                //select and delete document
                var queryOne = new SqlQuerySpec("SELECT c._self, c.groupid FROM c WHERE c.id = @id",
                    new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = TodayTrack.id } }));
                Document trackItem = client.CreateDocumentQuery(collectionUri, queryOne, new FeedOptions { PartitionKey = new PartitionKey(TodayTrack.groupid) }).AsEnumerable().FirstOrDefault();
                if (!(trackItem is null))
                    await client.DeleteDocumentAsync(trackItem.SelfLink, new RequestOptions { PartitionKey = new PartitionKey(TodayTrack.groupid) });
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
        public string id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string userWebId { get; set; }
        public bool Active { get; set; }
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
        public int UserTimezone { get; set; }
        public string[] subscibers { get; set; }
        public string status { get; set; }
        public string groupid { get; set; }
    }
    public class Status
    {
        public const string ExistingUser = "Existing user";
        public const string NewUser = "New user";
        public const string UserMissing = "user missing";
    };
}
