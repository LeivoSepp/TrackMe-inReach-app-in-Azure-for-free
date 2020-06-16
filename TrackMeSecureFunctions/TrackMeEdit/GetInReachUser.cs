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
using System.Globalization;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class GetInReachUser
    {
        private static HelperKMLParse helperKMLParse = new HelperKMLParse();

        [FunctionName("GetInReachUser")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> inReachUsers,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
            )] IAsyncCollector<InReachUser> asyncCollectorInReachUser,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
           )] DocumentClient documentClient,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
                )]
            IAsyncCollector<KMLInfo> asyncCollectorKMLInfo,
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
            var StorageContainerConnectionString = config["StorageContainerConnectionString"];


            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("FreeCosmosDB", "TrackMe");
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(inReachUsers, Identities);
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
                        await ChangePartitionAsync(LoggedInUser.userWebId, userDataFromWeb.userWebId, asyncCollectorKMLInfo, documentClient, collectionUri, StorageContainerConnectionString); //change partitionIDs
                    LoggedInUser.InReachWebAddress = userDataFromWeb.InReachWebAddress;
                    LoggedInUser.InReachWebPassword = userDataFromWeb.InReachWebPassword;
                    LoggedInUser.userWebId = userDataFromWeb.userWebId.ToLower();
                    LoggedInUser.Active = userDataFromWeb.Active;
                    LoggedInUser.UserTimezone = userDataFromWeb.UserTimezone;
                    await ManageTodayTrack(LoggedInUser, asyncCollectorKMLInfo, documentClient, collectionUri, TodayTrackId, SendEmailFunctionUrl, SendEmailFunctionKey, WebSiteUrl, StorageContainerConnectionString);
                }
                await asyncCollectorInReachUser.AddAsync(LoggedInUser);
            }
            return new OkObjectResult(LoggedInUser);
        }
        static async Task ManageTodayTrack(InReachUser LoggedInUser, IAsyncCollector<KMLInfo> addDocuments, DocumentClient client, Uri collectionUri, string TodayTrackId, string SendEmailFunctionUrl, string SendEmailFunctionKey, string WebSiteUrl, string StorageContainerConnectionString)
        {
            var dated1 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");
            var dated2 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");
            var dateTimed1 = DateTime.Parse(dated1).AddHours(-LoggedInUser.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var dateTimed2 = DateTime.Parse(dated2).AddDays(1).AddHours(-LoggedInUser.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");

            KMLInfo kMLInfo = new KMLInfo()
            {
                id = TodayTrackId,
                Title = "Today's track",
                d1 = dateTimed1,
                d2 = dateTimed2,
                groupid = LoggedInUser.userWebId,
                InReachWebAddress = LoggedInUser.InReachWebAddress,
                InReachWebPassword = LoggedInUser.InReachWebPassword,
                UserTimezone = LoggedInUser.UserTimezone,
                IsLongTrack = false
            };
            //create Today's track
            if (LoggedInUser.Active)
            {
                HelperGetKMLFromGarmin helperGetKMLFromGarmin = new HelperGetKMLFromGarmin();

                var emails = new List<Emails>();
                //get feed grom garmin
                var kmlFeedresult = await helperGetKMLFromGarmin.GetKMLAsync(kMLInfo);
                var blobs = helperKMLParse.Blobs;
                //parse and transform the feed and save to database
                helperKMLParse.ParseKMLFile(kmlFeedresult, kMLInfo, blobs, emails, LoggedInUser, WebSiteUrl);
                await addDocuments.AddAsync(kMLInfo);
                //save blobs
                foreach (var blob in blobs)
                    await helperKMLParse.AddKMLToBlobAsync(kMLInfo, blob.BlobValue, StorageContainerConnectionString, blob.BlobName);

                //sending out emails
                if (emails.Any())
                {
                    HttpClient httpClient = new HttpClient();
                    Uri SendEmailFunctionUri = new Uri($"{SendEmailFunctionUrl}?code={SendEmailFunctionKey}");
                    var returnMessage = await httpClient.PostAsJsonAsync(SendEmailFunctionUri, emails);
                }
            }
            //delete Today's track
            if (!LoggedInUser.Active)
            {
                //select and delete document
                var queryOne = new SqlQuerySpec("SELECT c._self, c.groupid, c.id FROM c WHERE c.id = @id",
                    new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = kMLInfo.id } }));
                KMLInfo kML = client.CreateDocumentQuery(collectionUri, queryOne, new FeedOptions { PartitionKey = new PartitionKey(kMLInfo.groupid) }).AsEnumerable().FirstOrDefault();
                if (!(kML is null))
                {
                    //delete metadata
                    await client.DeleteDocumentAsync(kML._self, new RequestOptions { PartitionKey = new PartitionKey(kML.groupid) });
                    //delete blobs
                    foreach (var blob in helperKMLParse.Blobs)
                        await helperKMLParse.RemoveKMLBlobAsync(kML, StorageContainerConnectionString, blob.BlobName);
                }
            }
        }
        static async Task ChangePartitionAsync(string userWebId, string newUserWebId, IAsyncCollector<KMLInfo> addDocuments, DocumentClient client, Uri collectionUri, string StorageContainerConnectionString)
        {
            //getting active tracks from cosmos
            var query = new SqlQuerySpec("SELECT * FROM c WHERE c.groupid = @groupid",
                new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@groupid", Value = userWebId } }));
            IEnumerable<KMLInfo> kMLInfos = client.CreateDocumentQuery<KMLInfo>(collectionUri, query, new FeedOptions { PartitionKey = new PartitionKey(userWebId) }).AsEnumerable();
            foreach (KMLInfo kMLInfo in kMLInfos)
            {
                kMLInfo.groupid = newUserWebId;
                kMLInfo.d1 = DateTime.Parse(kMLInfo.d1, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");
                kMLInfo.d2 = DateTime.Parse(kMLInfo.d2, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");
                if(!string.IsNullOrEmpty(kMLInfo.LastPointTimestamp))
                    kMLInfo.LastPointTimestamp = DateTime.Parse(kMLInfo.LastPointTimestamp, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");
                //create new docs
                await addDocuments.AddAsync(kMLInfo);
                //delete old docs
                await client.DeleteDocumentAsync(kMLInfo._self, new RequestOptions { PartitionKey = new PartitionKey(userWebId) });
                //rename all the blobs
                foreach (var blob in helperKMLParse.Blobs)
                    await helperKMLParse.RenameKMLBlobAsync(userWebId, newUserWebId, kMLInfo.id, StorageContainerConnectionString, blob.BlobName);
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
