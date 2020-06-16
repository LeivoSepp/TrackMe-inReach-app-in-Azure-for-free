using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class DeleteInReachFeedFromCosmos
    {
        [FunctionName("DeleteInReachFeedFromCosmos")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "DeleteInReachFeedFromCosmos/{userWebId}/{trackId}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TrackMe",
                ConnectionStringSetting = "CosmosDBForFree",
                PartitionKey = "{userWebId}",
                Id = "{trackId}"
                )] KMLInfo kMLInfo,
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
            )] DocumentClient documentClient,
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

            HelperKMLParse helperKMLParse = new HelperKMLParse();
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(inReachUsers, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            if (IsAuthenticated)
            {
                //selfLink is like this: "dbs/auo6AA==/colls/auo6AOdfluE=/docs/auo6AOdfluEnFwIAAAAAAA==/";
                await documentClient.DeleteDocumentAsync(kMLInfo._self, new RequestOptions { PartitionKey = new PartitionKey(LoggedInUser.userWebId) });
                //delete blobs
                foreach (var blob in helperKMLParse.Blobs)
                {
                    var blobName = $"{kMLInfo.groupid}/{kMLInfo.id}/{blob.BlobName}.kml";
                    await helperKMLParse.RemoveBlobAsync(blobName, blobClient);
                }
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}
