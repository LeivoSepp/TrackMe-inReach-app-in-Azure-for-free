using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public class KMLData
    {
        public string id { get; set; }
        public string Title { get; set; }
        public string d1 { get; set; }
        public string d2 { get; set; }
        public string PlannedTrack { get; set; }
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
    }

    public static class GetKMLFeedMetadata
    {
        private static HelperKMLParse helperKMLParse = new HelperKMLParse();

        [FunctionName("GetKMLFeedMetadata")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetKMLFeedMetadata/{GroupId}/{id}")] HttpRequest req,
            string GroupId,
             [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                PartitionKey = "{GroupId}",
                Id = "{id}"
                )]
            KMLInfo kMLInfo,
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
            var StorageContainerConnectionString = config["StorageContainerConnectionString"];

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageContainerConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(inReachUsers, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            KMLData kMLData = new KMLData()
            {
                id = kMLInfo.id,
                d1 = kMLInfo.d1,
                d2 = kMLInfo.d2,
                Title = kMLInfo.Title,
                InReachWebAddress = kMLInfo.InReachWebAddress,
                InReachWebPassword = kMLInfo.InReachWebPassword
            };
            var blobName = $"{kMLInfo.groupid}/{kMLInfo.id}/plannedtrack.kml";
            kMLData.PlannedTrack = await helperKMLParse.GetFromBlobAsync(blobName, blobClient);

            if (IsAuthenticated)
            {
                if (GroupId == LoggedInUser.userWebId)
                    return new OkObjectResult(kMLData);
                else
                    return new OkObjectResult("Not your track");
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}
