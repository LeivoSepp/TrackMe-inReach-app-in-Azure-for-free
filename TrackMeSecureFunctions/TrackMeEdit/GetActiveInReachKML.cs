using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class GetActiveInReachKML
    {
        private static HelperKMLParse helperKMLParse = new HelperKMLParse();

        [FunctionName("GetActiveInReachKML")]
        public static async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
        [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
                )]
             IAsyncCollector<KMLInfo> asyncCollectorKMLInfo,
           [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree"
           )] DocumentClient documentClient,
             [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> inReachUsers,
            ExecutionContext context)
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
            List<Emails> emails = new List<Emails>();

            DateTime dateTimeUTC = DateTime.UtcNow.ToUniversalTime();

            //getting active tracks from CosmosDB
            var query = new SqlQuerySpec("SELECT * FROM c WHERE (c.d1 < @dateTimeUTC and c.d2 > @dateTimeUTC) or c.id = @TodayTrack",
                new SqlParameterCollection(new SqlParameter[] { new SqlParameter { Name = "@dateTimeUTC", Value = dateTimeUTC }, new SqlParameter { Name = "@TodayTrack", Value = TodayTrackId } }));
            IEnumerable<KMLInfo> kMLInfos = documentClient.CreateDocumentQuery<KMLInfo>(collectionUri, query, new FeedOptions { EnableCrossPartitionQuery = true }).AsEnumerable();

            //remove all duplicates by LastPointTimestamp and groupid field to make only one query to Garmin per user (if user has multiple Live tracks in same time)
            IEnumerable<KMLInfo> kMLInfoDeDups = kMLInfos.GroupBy(x => new { x.LastPointTimestamp, x.groupid }).Select(x => x.First()).ToList();

            //getting feed from garmin, one feed for each user if LastpointTimestamp is same
            foreach (var kMLInfoDeDup in kMLInfoDeDups)
            {
                DateTime lastd1 = DateTime.SpecifyKind(DateTime.Parse(kMLInfoDeDup.d1, CultureInfo.InvariantCulture), DateTimeKind.Utc);
                DateTime today = DateTime.UtcNow.ToUniversalTime().AddDays(-1);

                //set d1 to LastPointTimestamp + 1 second (if LastTimestamp exist) to download the feed from that point forward from Garmin
                if (!string.IsNullOrEmpty(kMLInfoDeDup.LastPointTimestamp))
                    kMLInfoDeDup.d1 = DateTime.Parse(kMLInfoDeDup.LastPointTimestamp, CultureInfo.InvariantCulture).AddSeconds(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
                else
                    kMLInfoDeDup.d1 = DateTime.Parse(kMLInfoDeDup.d1, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");

                //resetting Today's track, once at night according to user Timezone
                if (lastd1 < today && kMLInfoDeDup.id == TodayTrackId)
                {
                    var dated1 = DateTime.UtcNow.ToUniversalTime().AddHours(kMLInfoDeDup.UserTimezone).ToString("yyyy-MM-dd");
                    var dateTimed1 = DateTime.Parse(dated1).AddHours(-kMLInfoDeDup.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    var dateTimed2 = DateTime.Parse(dated1).AddDays(1).AddHours(-kMLInfoDeDup.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    kMLInfoDeDup.d1 = dateTimed1;
                    kMLInfoDeDup.d2 = dateTimed2;
                    kMLInfoDeDup.LastPointTimestamp = "";
                    kMLInfoDeDup.LastLatitude = 0;
                    kMLInfoDeDup.LastLongitude = 0;
                    kMLInfoDeDup.LastTotalDistance = 0;
                    kMLInfoDeDup.LastTotalTime = "";
                    kMLInfoDeDup.TrackStartTime = "";
                    await asyncCollectorKMLInfo.AddAsync(kMLInfoDeDup);
                    //delete Today's blobs
                    foreach (var blob in helperKMLParse.Blobs)
                        await helperKMLParse.RemoveKMLBlobAsync(kMLInfoDeDup, StorageContainerConnectionString, blob.BlobName);
                }
                //getting always only last point from garmin (except if new day with active tracking has started)
                HelperGetKMLFromGarmin GetKMLFromGarmin = new HelperGetKMLFromGarmin();
                var kmlFeedresult = await GetKMLFromGarmin.GetKMLAsync(kMLInfoDeDup);
                kMLInfoDeDup.LastPoint = kmlFeedresult;
            }

            foreach (var kMLInfo in kMLInfos)
            {
                var kmlFeedresult = kMLInfoDeDups.First(x => x.groupid == kMLInfo.groupid).LastPoint;
                //if there are new points, then load whole track from database and add the point
                if (helperKMLParse.IsThereNewPoints(kmlFeedresult, kMLInfo))
                {
                    var user = new InReachUser();
                    foreach (var usr in inReachUsers)
                    {
                        if (usr.userWebId == kMLInfo.groupid)
                        {
                            user = usr;
                            break;
                        }
                    }
                    //open KML feeds from Blobstorage
                    var blobs = helperKMLParse.Blobs;
                    foreach (var blob in blobs)
                        blob.BlobValue = await helperKMLParse.GetKMLFromBlobAsync(kMLInfo, StorageContainerConnectionString, blob.BlobName);

                    //process the full track
                    helperKMLParse.ParseKMLFile(kmlFeedresult, kMLInfo, blobs, emails, user, WebSiteUrl);

                    kMLInfo.d1 = DateTime.Parse(kMLInfo.d1, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    kMLInfo.d2 = DateTime.Parse(kMLInfo.d2, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");

                    await asyncCollectorKMLInfo.AddAsync(kMLInfo);
                    //save blobs
                    foreach (var blob in blobs)
                        await helperKMLParse.AddKMLToBlobAsync(kMLInfo, blob.BlobValue, StorageContainerConnectionString, blob.BlobName);
                }
            }

            //remove all duplicates by DateTime field and sending the list to SendEmailFunction
            if (emails.Any())
            {
                List<Emails> emailList = emails.GroupBy(x => x.DateTime).Select(x => x.First()).ToList();
                HttpClient client = new HttpClient();
                Uri SendEmailFunctionUri = new Uri($"{SendEmailFunctionUrl}?code={SendEmailFunctionKey}");
                var returnMessage = await client.PostAsJsonAsync(SendEmailFunctionUri, emailList);
            }
        }
    }
}
