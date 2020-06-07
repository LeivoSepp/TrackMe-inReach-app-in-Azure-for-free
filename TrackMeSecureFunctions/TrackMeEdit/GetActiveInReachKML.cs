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
        public static async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
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
            var emails = new List<Emails>();

            //getting active tracks from cosmos
            var query = new SqlQuerySpec("SELECT c.id, c.d1, c.groupid, c.LastPointTimestamp, c.LastLatitude, c.LastLongitude, c.LastDistance, c.InReachWebAddress, c.InReachWebPassword FROM c WHERE (c.d1 < @localtime and c.d2 > @localtime1) or c.id = @TodayTrack",
                new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@localtime", Value = localTime }, new SqlParameter { Name = "@localtime1", Value = localTime.AddDays(-1) }, new SqlParameter { Name = "@TodayTrack", Value = TodayTrackId } }));
            IEnumerable<KMLInfo> TracksMetadata = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, query, new FeedOptions { EnableCrossPartitionQuery = true }).AsEnumerable();

            //getting all tracks which are currently active 
            foreach (var item in TracksMetadata)
            {
                var saveForTrackd1 = item.d1;
                DateTime lastd1 = DateTime.Parse(item.d1).ToUniversalTime();
                DateTime today = DateTime.UtcNow.ToUniversalTime().AddDays(-1);

                //clearing d1 to wnload only last point from Garmin
                item.d1 = string.Empty;

                //reset Today's track tracking information and set date = today to download full Today Track
                if (lastd1 < today && item.id == TodayTrackId)
                {
                    item.d1 = DateTime.UtcNow.ToUniversalTime().AddHours(3).ToString("yyyy-MM-dd");
                    item.PlacemarksAll = "";
                    item.PlacemarksWithMessages = "";
                    item.LineString = "";
                    item.LastLongitude = 0;
                    item.LastLatitude = 0;
                    item.LastTotalDistance = 0;
                    item.LastPointTimestamp = "";
                    item.TrackStartTime = "";
                    saveForTrackd1 = item.d1;
                }
                //getting always only last point from garmin (except if new day with someone's active tracking has started)
                HelperGetKMLFromGarmin GetKMLFromGarmin = new HelperGetKMLFromGarmin();
                var kmlFeedresult = await GetKMLFromGarmin.GetKMLAsync(item);

                //if there are new points, then load whole track from database and add the point
                if (_helperInReach.IsThereNewPoints(kmlFeedresult, item))
                {
                    var queryOne = new SqlQuerySpec("SELECT * FROM c WHERE c.id = @id",
                        new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@id", Value = item.id } }));
                    KMLInfo fullTrack = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, queryOne, new FeedOptions { PartitionKey = new PartitionKey(item.groupid) }).AsEnumerable().FirstOrDefault();

                    //process the full track
                    fullTrack = _helperInReach.GetAllPlacemarks(kmlFeedresult, fullTrack, emails);
                    //adding back d1 as it was removed
                    fullTrack.d1 = saveForTrackd1;
                    await output.AddAsync(fullTrack);
                }
            }

            //before sending out emails remove all duplicates by DateTime field.
            List<Emails> emailList = emails.GroupBy(x => x.DateTime).Select(x=>x.First()).ToList() ;
            foreach (var email in emailList)
            {
                HttpClient client = new HttpClient();
                Uri SendEmailFunctionUri = new Uri($"https://trackmefunctions.azurewebsites.net/api/SendEmailInReach/{email.UserWebId}?code={FunctionKey}");
                var returnMessage = await client.PostAsJsonAsync(SendEmailFunctionUri, email);
                var lastMessage = await returnMessage.Content.ReadAsStringAsync();
            }
        }
    }
}
