using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class GetActiveInReachKML
    {
        private static HelperKMLParse _helperInReach = new HelperKMLParse();

        [FunctionName("GetActiveInReachKML")]
        public static async void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
        [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
             IAsyncCollector<KMLInfo> output,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user' and c.Active = true"
                )] IEnumerable<InReachUser> users,
           [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
           )] DocumentClient documentClient,
            ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            var FunctionKey = config["SendEmailInReachFunctionKey"];

            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("HomeIoTDB", "GPSTracks");
            string TodayTrackId = "TodayTrack";

            DateTime localTime = _helperInReach.getLocalTime("FLE Standard Time");

            //getting active tracks from cosmos
            var query = new SqlQuerySpec("SELECT c.id, c.d1, c.groupid, c.LastPointTimestamp, c.InReachWebAddress, c.InReachWebPassword FROM c WHERE (c.d1 < @localtime and c.d2 > @localtime1) or c.id = @TodayTrack",
                new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@localtime", Value = localTime }, new SqlParameter { Name = "@localtime1", Value = localTime.AddDays(-1) }, new SqlParameter { Name = "@TodayTrack", Value = TodayTrackId } }));
            IEnumerable<KMLInfo> TracksMetadata = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, query, new FeedOptions { EnableCrossPartitionQuery = true }).AsEnumerable();

            //getting all tracks which are currently active 
            foreach (var item in TracksMetadata)
            {
                var saved1 = item.d1;
                DateTime lastd1 = DateTime.Parse(item.d1).ToUniversalTime();
                DateTime today = DateTime.UtcNow.ToUniversalTime().AddDays(-1);
                if (lastd1 > today)
                    item.d1 = string.Empty;
                else
                    item.d1 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");

                await ProcessKMLAsync(item, documentClient, output, collectionUri, FunctionKey);
            }
            //Getting all Today's active trackings (where user setting Active = true)
            foreach (var user in users)
            {
                var queryMetadata = new SqlQuerySpec("SELECT c.id, c.d1, c.groupid, c.LastPointTimestamp, c.InReachWebAddress, c.InReachWebPassword  FROM c WHERE c.id = @id",
                    new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = TodayTrackId } }));
                KMLInfo item = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, queryMetadata, new FeedOptions { PartitionKey = new PartitionKey(user.userWebId) }).AsEnumerable().FirstOrDefault();
                DateTime lastd1 = DateTime.Parse(item.d1).ToUniversalTime();
                DateTime today = DateTime.UtcNow.ToUniversalTime().AddDays(-1);
                if (lastd1 > today)
                    item.d1 = string.Empty;
                else
                    item.d1 = DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd");
                await ProcessKMLAsync(item, documentClient, output, collectionUri, FunctionKey);
            }
        }
        static async Task ProcessKMLAsync(KMLInfo item, DocumentClient documentClient, IAsyncCollector<KMLInfo> output, Uri collectionUri, string FunctionKey)
        {
            //getting always only last point from garmin (except if new day with someone's active tracking has started)
            HelperGetKMLFromGarmin GetKMLFromGarmin = new HelperGetKMLFromGarmin();
            var kmlFeedresult = await GetKMLFromGarmin.GetKMLAsync(item);

            //if there are new points, then load whole track from database and add the point
            if (_helperInReach.IsThereNewPoints(kmlFeedresult, item))
            {
                var LastPointTimestamp = item.LastPointTimestamp;
                var queryOne = new SqlQuerySpec("SELECT * FROM c WHERE c.id = @id",
                    new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = item.id } }));
                KMLInfo fullTrack = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, queryOne, new FeedOptions { PartitionKey = new PartitionKey(item.groupid) }).AsEnumerable().FirstOrDefault();

                var emails = new List<KeyValuePair<string, string>>();

                //process the full track
                fullTrack = _helperInReach.GetAllPlacemarks(out emails, kmlFeedresult, fullTrack);
                fullTrack.LastPointTimestamp = LastPointTimestamp;

                //send out emails
                foreach (var email in emails)
                {
                    HttpClient client = new HttpClient();
                    Uri SendEmailFunctionUri = new Uri($"https://trackmefunctions.azurewebsites.net/api/SendEmailInReach/{fullTrack.InReachWebAddress}?code={FunctionKey}");
                    var returnMessage = await client.PostAsJsonAsync(SendEmailFunctionUri, new { eMailSubject = email.Key, eMailMessage = email.Value });
                    fullTrack.LasMessage = await returnMessage.Content.ReadAsStringAsync();
                    }

                await output.AddAsync(fullTrack);
            }
        }
    }
}
